using System.Net;
using System.Net.Http;
using MeshhessenClient.Services;

namespace MeshhessenClient.Tests;

public class TileAuthTests
{
    // The allowlist is the security boundary: the token header must go ONLY to our
    // own tile hosts, never to OSM, GitHub, or a user's custom tile server.
    [Theory]
    [InlineData("https://tile.meshhessenclient.de/osm/1/2/3.png", true)]
    [InlineData("https://vectortile.meshhessenclient.de/styles/osm.json", true)]
    [InlineData("https://TILE.MESHHESSENCLIENT.DE/osm/1/2/3.png", true)]   // host match is case-insensitive
    [InlineData("https://tile.openstreetmap.org/1/2/3.png", false)]
    [InlineData("https://raw.githubusercontent.com/x/y/CHANNELS.csv", false)]
    [InlineData("https://tile.someones-fork.de/osm/1/2/3.png", false)]
    [InlineData("https://tile.meshhessen.de/osm/1/2/3.png", false)]        // retired/not-ours: excluded
    public void IsOurHost_MatchesOnlyOwnTileHosts(string url, bool expected)
    {
        Assert.Equal(expected, TileAuth.IsOurHost(new Uri(url)));
    }

    [Fact]
    public void IsOurHost_NullUri_IsFalse()
    {
        Assert.False(TileAuth.IsOurHost(null));
    }

    // Fork/source builds carry no token → the handler must add no header at all,
    // even for our own hosts. (Test builds have no injected token, so Token == "".)
    [Fact]
    public async Task Handler_WithoutToken_AddsNoHeader_EvenForOurHost()
    {
        Assert.Equal("", TileAuth.Token); // precondition: test assembly has no injected token
        var capture = new CapturingHandler();
        using var client = new HttpClient(TileAuth.Wrap(capture));

        await client.GetAsync("https://tile.meshhessenclient.de/osm/1/2/3.png");

        Assert.False(capture.LastRequest!.Headers.Contains("X-MH-Client"));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
