using System.Net;
using System.Text;
using Mif.Node.Continuity;

namespace Mif.Node.Tests;

public class UpstreamProbeTests
{
    private sealed class StubUpstream : HttpMessageHandler
    {
        public HttpStatusCode Discovery { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode Keys { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode Token { get; set; } = HttpStatusCode.OK;
        public bool Hang { get; set; }
        public List<string> Requests { get; } = [];
        public string? TokenRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(path);
            if (Hang) await Task.Delay(Timeout.Infinite, ct);
            if (path == "/discovery") return Json(Discovery, """{"jwks_uri":"http://upstream/keys"}""");
            if (path == "/keys") return Json(Keys, """{"keys":[]}""");
            TokenRequestBody = await request.Content!.ReadAsStringAsync(ct);
            return Json(Token, """{"access_token":"x"}""");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static OidcUpstreamProbe Probe(StubUpstream upstream, bool withTokenProbe = true, string? clientId = "probe") => new(
        new HttpClient(upstream),
        Microsoft.Extensions.Options.Options.Create(new MifOptions
        {
            Upstream =
            {
                DiscoveryUrl = "http://upstream/discovery",
                ProbeTokenUrl = withTokenProbe ? "http://upstream/token" : null,
                ProbeClientId = clientId,
                ProbeClientSecret = "secret",
                ProbeScope = "https://graph.example/.default",
                TimeoutSeconds = 1,
            },
        }));

    [Fact]
    public async Task Healthy_when_discovery_keys_and_a_token_request_all_succeed()
    {
        var upstream = new StubUpstream();

        var result = await Probe(upstream).ProbeAsync(default);

        Assert.True(result.Healthy, result.Detail);
        Assert.Equal(["/discovery", "/keys", "/token"], upstream.Requests);
        Assert.Contains("grant_type=client_credentials", upstream.TokenRequestBody);
        Assert.Contains("scope=https%3A%2F%2Fgraph.example%2F.default", upstream.TokenRequestBody);
    }

    [Fact]
    public async Task Unhealthy_when_tokens_are_not_being_issued_even_though_discovery_still_answers()
    {
        var upstream = new StubUpstream { Token = HttpStatusCode.ServiceUnavailable };

        var result = await Probe(upstream).ProbeAsync(default);

        Assert.False(result.Healthy);
        Assert.Equal("token request returned 503", result.Detail);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, HttpStatusCode.OK, "discovery returned 502")]
    [InlineData(HttpStatusCode.OK, HttpStatusCode.InternalServerError, "signing keys returned 500")]
    public async Task Unhealthy_when_discovery_or_signing_keys_fail(HttpStatusCode discovery, HttpStatusCode keys, string detail)
    {
        var result = await Probe(new StubUpstream { Discovery = discovery, Keys = keys }).ProbeAsync(default);

        Assert.Equal(new ProbeResult(false, detail), result);
    }

    [Fact]
    public async Task Unhealthy_when_the_upstream_does_not_answer_in_time()
    {
        var result = await Probe(new StubUpstream { Hang = true }).ProbeAsync(default);

        Assert.Equal(new ProbeResult(false, "discovery timed out after 1s"), result);
    }

    [Theory]
    [InlineData(false, "probe")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    public async Task Without_a_probe_client_only_discovery_and_keys_are_checked(bool withTokenUrl, string? clientId)
    {
        var upstream = new StubUpstream { Token = HttpStatusCode.Unauthorized };

        var result = await Probe(upstream, withTokenUrl, clientId).ProbeAsync(default);

        Assert.True(result.Healthy, result.Detail);
        Assert.Equal(["/discovery", "/keys"], upstream.Requests);
    }
}
