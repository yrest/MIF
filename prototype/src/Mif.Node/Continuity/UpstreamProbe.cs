using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Mif.Node.Continuity;

public sealed record ProbeResult(bool Healthy, string Detail);

public interface IUpstreamProbe
{
    Task<ProbeResult> ProbeAsync(CancellationToken ct);
}

/// <summary>
/// Checks the upstream IdP the way a sign-in would use it: discovery document, signing keys and,
/// when configured, a real token request. Any failure or timeout counts as unhealthy.
/// </summary>
public sealed class OidcUpstreamProbe(HttpClient http, IOptions<MifOptions> options) : IUpstreamProbe
{
    private readonly UpstreamOptions _upstream = options.Value.Upstream;

    public async Task<ProbeResult> ProbeAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_upstream.TimeoutSeconds));
        var step = "discovery";
        try
        {
            using var discovery = await http.GetAsync(_upstream.DiscoveryUrl, timeout.Token);
            if (!discovery.IsSuccessStatusCode) return new(false, $"discovery returned {(int)discovery.StatusCode}");
            using var document = JsonDocument.Parse(await discovery.Content.ReadAsStringAsync(timeout.Token));

            step = "signing keys";
            var jwksUri = document.RootElement.GetProperty("jwks_uri").GetString()!;
            using var jwks = await http.GetAsync(jwksUri, timeout.Token);
            if (!jwks.IsSuccessStatusCode) return new(false, $"signing keys returned {(int)jwks.StatusCode}");

            // The token request needs its own client. Without one, report what could be checked.
            if (string.IsNullOrEmpty(_upstream.ProbeTokenUrl) || string.IsNullOrEmpty(_upstream.ProbeClientId))
                return new(true, "discovery and signing keys reachable");

            step = "token request";
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _upstream.ProbeClientId ?? "",
                ["client_secret"] = _upstream.ProbeClientSecret ?? "",
            };
            if (!string.IsNullOrEmpty(_upstream.ProbeScope)) form["scope"] = _upstream.ProbeScope;
            using var token = await http.PostAsync(_upstream.ProbeTokenUrl, new FormUrlEncodedContent(form), timeout.Token);
            return token.IsSuccessStatusCode
                ? new(true, "discovery, signing keys and token issuance working")
                : new(false, $"token request returned {(int)token.StatusCode}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, $"{step} timed out after {_upstream.TimeoutSeconds}s");
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException)
        {
            return new(false, $"{step} failed: {e.Message}");
        }
    }
}
