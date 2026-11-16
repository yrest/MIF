// End-to-end outage drill against the docker-compose prototype.
//
// Drives a real browser with a virtual passkey authenticator through the whole story:
// live sign-in -> passkey enrolment -> upstream outage -> passkey sign-in -> revocation -> recovery.
//
//   docker compose up -d --build     (from the prototype directory)
//   cd e2e && npm install && npx playwright install chromium && npm run drill
//
// Run it on the machine that hosts the stack: it stops and starts the fake-entra container itself.
//
// In mesh mode (docker-compose.mesh.yml), set MIF_HOST to the same value as in .env. The browser
// then needs to trust the test certificate authority, and so does Node:
//   MIF_HOST=my-mac.netbird.cloud NODE_EXTRA_CA_CERTS=../mif-test-root-ca.crt npm run drill

const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const path = require('node:path');
const { chromium } = require('playwright');

const MESH_HOST = process.env.MIF_HOST;
const PORTAL = MESH_HOST ? `https://${MESH_HOST}:5443` : 'http://localhost:5080';
const BROKER = MESH_HOST ? `https://${MESH_HOST}:8443` : 'http://localhost:8080';
const UPSTREAM = MESH_HOST ? `https://${MESH_HOST}:8444` : 'http://localhost:8090';
const NODE = MESH_HOST ? `https://${MESH_HOST}:8448` : 'http://localhost:8088';
const OPERATOR = 'http://localhost:8081'; // never published beyond loopback
const UPSTREAM_HOST = new URL(UPSTREAM).host;
const BROKER_HOST = new URL(BROKER).host;
const PROTOTYPE_DIR = path.resolve(__dirname, '..');

const compose = (...args) => execFileSync('docker', ['compose', ...args], { cwd: PROTOTYPE_DIR, stdio: 'pipe' });
const json = async (url, init) => {
  const response = await fetch(url, init);
  return { status: response.status, body: await response.json().catch(() => null) };
};
const post = (url, body) => json(url, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) });

async function until(what, check, timeoutMs = 90_000) {
  const deadline = Date.now() + timeoutMs;
  let last;
  while (Date.now() < deadline) {
    try {
      last = await check();
      if (last) return last;
    } catch (error) {
      last = error.message;
    }
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  throw new Error(`Timed out waiting for: ${what} (last: ${JSON.stringify(last)})`);
}

const mode = async () => (await json(`${NODE}/status`)).body.continuity.mode;
const snapshotOf = async username => (await json(`${OPERATOR}/operator/snapshots`)).body.find(s => s.username === username);
const auditTypes = async () => (await json(`${OPERATOR}/operator/audit?max=1000`)).body.records.map(r => r.type);

let stepNumber = 0;
const step = title => console.log(`\n${++stepNumber}. ${title}`);
const ok = message => console.log(`   ok  ${message}`);

async function main() {
  console.log(`Target: ${PORTAL} (${MESH_HOST ? 'mesh mode' : 'localhost mode'})`);
  step('Wait for the stack and a healthy upstream');
  await until('portal, broker and upstream to be up, and the node to have probed its way to Normal mode', async () => {
    const up = await Promise.all([PORTAL, `${BROKER}/realms/mif/.well-known/openid-configuration`, `${UPSTREAM}/realms/upstream/.well-known/openid-configuration`]
      .map(async url => (await fetch(url)).ok));
    // On a cold start the node may pass through Degraded while the upstream container is still booting.
    const { mode, upstreamHealthy, revalidationPending } = (await json(`${NODE}/status`)).body.continuity;
    return up.every(Boolean) && mode === 'Normal' && upstreamHealthy === true && !revalidationPending;
  }, 180_000);
  ok('node reports Normal mode');

  const browser = await chromium.launch();
  const context = await browser.newContext();
  const page = await context.newPage();
  page.on('dialog', dialog => dialog.accept('drill passkey')); // Keycloak asks for a label for the new passkey

  // A virtual platform authenticator: stands in for Windows Hello, Touch ID or a phone.
  const cdp = await context.newCDPSession(page);
  await cdp.send('WebAuthn.enable');
  await cdp.send('WebAuthn.addVirtualAuthenticator', {
    options: { protocol: 'ctap2', transport: 'internal', hasResidentKey: true, hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true },
  });

  const sessionMode = () => page.getAttribute('#mode', 'data-mode');
  const signOutEverywhere = () => context.clearCookies();
  const atPortal = url => url.href.startsWith(PORTAL);

  /** Opens /me and gets through whichever sign-in the broker asks for. Returns the page that results. */
  async function openAccount({ expectDenied = false } = {}) {
    await page.goto(`${PORTAL}/me`);
    for (let hop = 0; hop < 6 && !atPortal(new URL(page.url())); hop++) {
      const host = new URL(page.url()).host;
      if (host === UPSTREAM_HOST) {
        await page.fill('#username', 'alice');
        await page.fill('#password', 'alice-dev-only');
        await Promise.all([page.waitForURL(url => url.host !== UPSTREAM_HOST), page.click('#kc-login')]);
      } else if (host === BROKER_HOST && await page.locator('#registerWebAuthn').count()) {
        await Promise.all([page.waitForURL(atPortal), page.click('#registerWebAuthn')]);
      } else if (host === BROKER_HOST && await page.locator('#authenticateWebAuthnButton').count()) {
        await page.click('#authenticateWebAuthnButton');
        if (expectDenied) {
          await page.waitForSelector('#kc-error-message');
          return;
        }
        await page.waitForURL(atPortal);
      } else {
        throw new Error(`Unexpected sign-in page: ${page.url()}\n${(await page.innerText('body')).slice(0, 400)}`);
      }
    }
    assert.ok(atPortal(new URL(page.url())), `expected to be back at the portal, at ${page.url()}`);
  }

  async function approvePayment() {
    await page.goto(`${PORTAL}/me`);
    const [response] = await Promise.all([page.waitForNavigation(), page.click('#approve')]);
    return { status: response.status(), text: await page.innerText('#result') };
  }

  try {
    step('Normal mode: Alice signs in through the upstream IdP');
    await openAccount();
    assert.equal(await sessionMode(), 'live');
    ok('portal shows a LIVE session verified by the upstream');
    const approved = await approvePayment();
    assert.equal(approved.status, 200);
    assert.match(approved.text, /Payment approved by alice/);
    ok('high-risk action allowed with a live identity');

    step('The node turns the live sign-in into a signed snapshot');
    const snapshot = await until('a valid snapshot for alice', async () => {
      const s = await snapshotOf('alice');
      return s?.valid ? s : null;
    });
    const verified = await post(`${NODE}/snapshots/verify`, { snapshot: snapshot.jws });
    assert.equal(verified.body.valid, true);
    assert.deepEqual(verified.body.roles, ['finance-approver']);
    ok(`snapshot ${snapshot.jti} verifies, expires ${snapshot.expiresAt}`);

    step('Alice enrols a passkey while the upstream is still healthy');
    await page.goto(`${PORTAL}/enrol-passkey`);
    await page.waitForSelector('#registerWebAuthn');
    await Promise.all([page.waitForURL(atPortal), page.click('#registerWebAuthn')]);
    assert.equal(await sessionMode(), 'live');
    ok('passkey registered at the MIF node');

    step('Outage: the upstream IdP goes away');
    const sessionEndsBefore = (await auditTypes()).filter(t => t === 'broker.sessions_ended').length;
    compose('stop', 'fake-entra');
    await until('node to enter Degraded mode', async () => (await mode()) === 'Degraded');
    ok('node detected the outage and switched the broker to the continuity flow');

    step('Degraded mode: Alice signs out, then signs in with her passkey');
    await page.goto(`${PORTAL}/me`);
    await Promise.all([page.waitForURL(`${PORTAL}/`), page.click('#sign-out')]);
    ok('signed out at the portal and at the node');
    await openAccount();
    assert.equal(await sessionMode(), 'continuity');
    ok('portal shows a CONTINUITY session');
    await page.goto(`${PORTAL}/records`);
    assert.match(await page.innerText('#result'), /Here are the records/);
    ok('low-risk action allowed');
    const blocked = await approvePayment();
    assert.equal(blocked.status, 403);
    assert.match(blocked.text, /blocked in continuity mode/);
    ok('high-risk action blocked (403) even though Alice still holds the role');

    step('A passkey sign-in must not renew the snapshot');
    await new Promise(resolve => setTimeout(resolve, 6000)); // several snapshot polls
    assert.equal((await snapshotOf('alice')).jti, snapshot.jti);
    ok('snapshot unchanged: continuity access cannot extend itself');

    step('Operator revokes Alice during the outage');
    const revoke = await post(`${OPERATOR}/operator/revoke`, { username: 'alice', reason: 'drill: laptop reported stolen' });
    assert.equal(revoke.status, 200);
    assert.equal((await post(`${NODE}/snapshots/verify`, { snapshot: snapshot.jws })).body.error, 'revoked');
    await signOutEverywhere();
    await openAccount({ expectDenied: true });
    assert.match(await page.innerText('#kc-error-message'), /no fresh identity snapshot/);
    ok('passkey sign-in refused after revocation');

    step('Operator endpoints are not reachable on the public port');
    assert.equal((await fetch(`${NODE}/operator/audit`)).status, 404);
    ok('public port returns 404 for /operator');

    step('Recovery: the upstream IdP comes back');
    compose('start', 'fake-entra');
    await until('node to return to Normal mode', async () => (await mode()) === 'Normal', 180_000);
    await until('sessions to be ended for live re-verification', async () =>
      (await auditTypes()).filter(t => t === 'broker.sessions_ended').length > sessionEndsBefore);
    ok('node returned to Normal and ended all sessions');

    step('Normal mode again: Alice is verified live, which lifts the revocation');
    await until('upstream sign-in page to answer', async () => (await fetch(`${UPSTREAM}/realms/upstream/.well-known/openid-configuration`)).ok);
    await signOutEverywhere();
    await openAccount();
    assert.equal(await sessionMode(), 'live');
    ok('portal shows a LIVE session again');
    const fresh = await until('a new valid snapshot for alice', async () => {
      const s = await snapshotOf('alice');
      return s?.valid && s.jti !== snapshot.jti ? s : null;
    });
    ok(`new snapshot ${fresh.jti} issued from the live sign-in`);

    step('Audit trail');
    const audit = (await json(`${OPERATOR}/operator/audit?max=1000`)).body;
    assert.equal(audit.chain_intact, true);
    const types = audit.records.map(r => r.type);
    for (const expected of ['snapshot.issued', 'eligibility.granted', 'continuity.mode_changed', 'broker.sign_in_flow_set', 'snapshot.revoked', 'eligibility.removed', 'broker.sessions_ended']) {
      assert.ok(types.includes(expected), `audit log is missing ${expected}`);
    }
    ok(`${audit.records.length} audit records, hash chain intact`);

    console.log('\nDrill passed.');
  } catch (error) {
    await page.screenshot({ path: path.join(__dirname, 'artifacts', 'failure.png') }).catch(() => {});
    console.error(`\nDrill FAILED at step ${stepNumber}: ${error.message}\n   page: ${page.url()}`);
    process.exitCode = 1;
  } finally {
    await browser.close();
    compose('start', 'fake-entra'); // never leave the stack in outage
  }
}

main();
