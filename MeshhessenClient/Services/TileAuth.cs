using System.Net.Http;
using System.Reflection;

namespace MeshhessenClient.Services;

/// <summary>
/// Server-signed bearer token, embedded at build time, that authenticates this
/// client against the self-hosted Meshhessen tile servers. The client never holds
/// the signing key — only the finished token string. Official CI builds inject it
/// via the <c>TileAuthToken</c> MSBuild property (→ AssemblyMetadata); a build from
/// source (a fork) has no secret, so the token is empty and the request goes out
/// unauthenticated — which the edge treats exactly like any other tokenless client.
///
/// Honest limit: the token is extractable/replayable from the .exe until it expires
/// or is revoked. The real levers are rotation per release + revocation + per-token
/// edge metrics, not cryptographic secrecy.
/// </summary>
public static class TileAuth
{
    /// <summary>The embedded token, or "" for a source/fork build. Resolved once.</summary>
    public static string Token { get; } = ResolveToken();

    /// <summary>Hosts that receive the token header. ONLY our own tile servers —
    /// never OSM, custom, or third-party hosts (privacy + pointless).</summary>
    private static readonly HashSet<string> OurHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "tile.meshhessenclient.de",        // raster: /osm/ /opentopo/ /dark/
        "vectortile.meshhessenclient.de",  // vector: styles, MVT, glyphs, sprites
    };

    public static bool IsOurHost(Uri? u) => u != null && OurHosts.Contains(u.Host);

    /// <summary>Wrap any inner handler so requests to our hosts carry the token.
    /// Non-our-host requests pass through untouched.</summary>
    public static DelegatingHandler Wrap(HttpMessageHandler inner) => new MeshhessenAuthHandler(inner);

    private static string ResolveToken()
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var a in asm.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false))
            if (a is AssemblyMetadataAttribute m && m.Key == "TileAuthToken")
                return m.Value ?? "";
        return "";
    }
}

/// <summary>Adds <c>X-MH-Client: &lt;token&gt;</c> to outgoing requests, but only to
/// our own tile hosts and only when a token is present.</summary>
public sealed class MeshhessenAuthHandler : DelegatingHandler
{
    public MeshhessenAuthHandler(HttpMessageHandler inner) : base(inner) { }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(TileAuth.Token) && TileAuth.IsOurHost(request.RequestUri))
            request.Headers.TryAddWithoutValidation("X-MH-Client", TileAuth.Token);
        return base.SendAsync(request, ct);
    }
}
