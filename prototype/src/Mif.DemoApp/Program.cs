using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Mif.Client;

var builder = WebApplication.CreateBuilder(args);
var oidc = builder.Configuration.GetSection("Oidc");
var nodeStatusUrl = builder.Configuration["MifNode:StatusUrl"];

builder.Services.AddHttpClient();
builder.Services.AddAntiforgery();
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.ExpireTimeSpan = TimeSpan.FromHours(1);
        options.AccessDeniedPath = "/denied";
    })
    .AddOpenIdConnect(options =>
    {
        // The application trusts the MIF node only. It never talks to Entra.
        options.Authority = oidc["Authority"];
        options.ClientId = oidc["ClientId"];
        options.ClientSecret = oidc["ClientSecret"];
        options.ResponseType = "code";
        options.SaveTokens = true; // sign-out needs the ID token to end the session at the node
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = MifClaimTypes.Username;
        options.TokenValidationParameters.RoleClaimType = MifClaimTypes.Roles;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");

        if (builder.Environment.IsDevelopment())
        {
            // Plain HTTP on localhost for the prototype. Over HTTPS keep the secure defaults.
            options.RequireHttpsMetadata = false;
            options.ResponseMode = "query";
            options.NonceCookie.SameSite = SameSiteMode.Lax;
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        }

        options.Events.OnRedirectToIdentityProvider = context =>
        {
            // Lets a signed-in user start passkey enrolment at the broker ("application initiated action").
            if (context.Properties.Items.TryGetValue("kc_action", out var action)) context.ProtocolMessage.SetParameter("kc_action", action);
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddMifPolicies()
    .AddPolicy("ApprovePayments", policy => policy.RequireRole("finance-approver"));

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("TrustForwardedHeaders"))
{
    // Behind the TLS-terminating proxy of mesh mode: take scheme and host from its headers, so that
    // redirect URIs are built as https. Only safe because the proxy is the sole way in to this container.
    var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost };
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();
    app.UseForwardedHeaders(forwarded);
}
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/", async (ClaimsPrincipal user, IHttpClientFactory http) =>
{
    var node = "unknown";
    if (nodeStatusUrl is not null)
    {
        try { node = await http.CreateClient().GetStringAsync(nodeStatusUrl); }
        catch (HttpRequestException e) { node = $"unreachable: {e.Message}"; }
    }
    var body = user.Identity?.IsAuthenticated == true
        ? "<p><a href=\"/me\">Continue to your account</a></p>"
        : "<p><a id=\"sign-in\" href=\"/me\">Sign in</a></p>";
    return Page("MIF demo portal", $"{body}<h2>MIF node status</h2><pre id=\"node-status\">{E(node)}</pre>");
});

app.MapGet("/me", [Authorize] (ClaimsPrincipal user, HttpContext http, IAntiforgery antiforgery) =>
{
    var token = antiforgery.GetAndStoreTokens(http);
    var claims = string.Join("", user.Claims.Select(c => $"<tr><td>{E(c.Type)}</td><td>{E(c.Value)}</td></tr>"));
    return Page($"Signed in as {user.Identity!.Name}", $"""
        {Banner(user)}
        <p><a id="records" href="/records">View records</a> (low risk: allowed in continuity mode)</p>
        <form method="post" action="/approve">
          <input type="hidden" name="{token.FormFieldName}" value="{token.RequestToken}">
          <button id="approve" type="submit">Approve payment</button> (high risk: needs a live identity)
        </form>
        <p><a id="enrol-passkey" href="/enrol-passkey">Enrol a passkey for outages</a> · <a id="sign-out" href="/signout">Sign out</a></p>
        <h2>Token claims</h2>
        <table>{claims}</table>
        """);
});

app.MapGet("/records", [Authorize] (ClaimsPrincipal user) =>
    Page("Records", $"{Banner(user)}<p id=\"result\">Here are the records. Read access stays available during an identity outage.</p><p><a href=\"/me\">Back</a></p>"));

// Two independent requirements: the role says who may approve, the MIF policy says how they must have signed in.
app.MapPost("/approve", [Authorize(Policy = "ApprovePayments")] [Authorize(Policy = MifPolicies.LiveIdentity)] (ClaimsPrincipal user) =>
    Page("Payment approved", $"{Banner(user)}<p id=\"result\">Payment approved by {E(user.Identity!.Name!)}.</p><p><a href=\"/me\">Back</a></p>"));

app.MapGet("/denied", (ClaimsPrincipal user) => Results.Content(Html("Not allowed", user.IsContinuitySession()
    ? $"{Banner(user)}<p id=\"result\">This action is blocked in continuity mode. It becomes available again once you have signed in through the upstream identity provider.</p><p><a href=\"/me\">Back</a></p>"
    : "<p id=\"result\">Your account does not have the role needed for this action.</p><p><a href=\"/me\">Back</a></p>"),
    "text/html", statusCode: (int)HttpStatusCode.Forbidden));

app.MapGet("/enrol-passkey", [Authorize] () => Results.Challenge(
    new AuthenticationProperties { RedirectUri = "/me", Items = { ["kc_action"] = "webauthn-register-passwordless" } },
    [OpenIdConnectDefaults.AuthenticationScheme]));

app.MapGet("/signout", () => Results.SignOut(
    new AuthenticationProperties { RedirectUri = "/" },
    [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));

app.Run();

static string E(string value) => WebUtility.HtmlEncode(value);

static string Banner(ClaimsPrincipal user) => user.IsContinuitySession()
    ? "<p id=\"mode\" data-mode=\"continuity\" class=\"banner continuity\">CONTINUITY session: signed in with a passkey against a signed identity snapshot. High-risk actions are blocked.</p>"
    : $"<p id=\"mode\" data-mode=\"live\" class=\"banner live\">LIVE session: verified by {E(user.UpstreamIdp()!)}.</p>";

static IResult Page(string title, string body) => Results.Content(Html(title, body), "text/html");

static string Html(string title, string body) => $$"""
    <!doctype html>
    <html lang="en"><head><meta charset="utf-8"><title>{{E(title)}}</title>
    <style>
      body { font-family: system-ui, sans-serif; max-width: 52rem; margin: 2rem auto; padding: 0 1rem; }
      .banner { padding: .75rem 1rem; border-radius: .4rem; font-weight: 600; }
      .live { background: #e6f4ea; color: #14532d; } .continuity { background: #fff4ce; color: #6b4e00; }
      table { border-collapse: collapse; } td { border: 1px solid #ccc; padding: .25rem .5rem; font-family: monospace; }
      pre { background: #f4f4f4; padding: 1rem; overflow-x: auto; }
    </style></head>
    <body><h1>{{E(title)}}</h1>{{body}}</body></html>
    """;
