using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MeshhessenClient.Services.MeshStick;

/// <summary>One downloadable Windows build of meshtasticd (an MSI attached to a GitHub release).</summary>
public sealed record MeshtasticdRelease(string Source, string Repo, string Tag, string Version, string AssetName,
    string DownloadUrl, long Size, bool Prerelease, DateTimeOffset? Published)
{
    public string Display =>
        $"{Version} ({Tag})  ·  {Source}{(Prerelease ? "  ·  pre-release" : "")}{(Published is { } p ? $"  ·  {p:yyyy-MM-dd}" : "")}";
}

/// <summary>
/// Release lookup for the meshtasticd Windows MSI. The firmware repo builds <c>meshtasticd-&lt;version&gt;-x64.msi</c>
/// (packaging/windows/meshtasticd.wxs) but has not attached it to its releases yet, so the Meshhessen fork of the
/// firmware repo acts as the mirror until upstream ships it. Both are plain GitHub release listings.
/// </summary>
public static class MeshtasticdReleases
{
    public const string OfficialRepo = "meshtastic/firmware";
    public const string DefaultMirrorRepo = "SMLunchen/mh_firmware";

    /// <summary>Asset name produced by the firmware's build-winget-package.ps1: meshtasticd-2.8.3-x64.msi.</summary>
    public static readonly Regex MsiAsset = new(@"^meshtasticd-(?<ver>[0-9][0-9A-Za-z.\-]*?)-(x64|amd64)\.msi$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsValidRepo(string? repo) =>
        !string.IsNullOrWhiteSpace(repo) && Regex.IsMatch(repo.Trim(), @"^[A-Za-z0-9_.\-]+/[A-Za-z0-9_.\-]+$");

    public static IReadOnlyList<MeshtasticdRelease> ParseGitHubReleases(string json, string repo, string sourceLabel)
    {
        var list = new List<MeshtasticdRelease>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var rel in doc.RootElement.EnumerateArray())
        {
            if (rel.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) continue;
            var tag = rel.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            bool pre = rel.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True;
            DateTimeOffset? published = rel.TryGetProperty("published_at", out var pa) && pa.ValueKind == JsonValueKind.String
                                        && DateTimeOffset.TryParse(pa.GetString(), null, System.Globalization.DateTimeStyles.AssumeUniversal, out var dt)
                ? dt : null;
            if (!rel.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var m = MsiAsset.Match(name);
                if (!m.Success) continue;
                var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(url)) continue;
                long size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
                list.Add(new MeshtasticdRelease(sourceLabel, repo, tag, m.Groups["ver"].Value, name, url, size, pre, published));
            }
        }
        return list.OrderByDescending(r => r.Published ?? DateTimeOffset.MinValue).ToList();
    }

    public static async Task<IReadOnlyList<MeshtasticdRelease>> FetchAsync(HttpClient http, string repo, string sourceLabel, CancellationToken ct)
    {
        if (!IsValidRepo(repo)) throw new ArgumentException($"invalid repository '{repo}'", nameof(repo));
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo.Trim()}/releases?per_page=30");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return ParseGitHubReleases(json, repo, sourceLabel);
    }

    /// <summary>Downloads the MSI into <paramref name="targetDir"/> (kept as a cache: an existing complete file is reused).</summary>
    public static async Task<string> DownloadAsync(HttpClient http, MeshtasticdRelease rel, string targetDir,
        IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(targetDir);
        var path = Path.Combine(targetDir, rel.AssetName);
        if (File.Exists(path) && (rel.Size <= 0 || new FileInfo(path).Length == rel.Size))
        {
            progress?.Report(1);
            return path;
        }
        var tmp = path + ".part";
        using (var resp = await http.GetAsync(rel.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? rel.Size;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            var buf = new byte[1 << 16];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report(Math.Min(1.0, (double)done / total));
            }
        }
        if (rel.Size > 0 && new FileInfo(tmp).Length != rel.Size)
        {
            File.Delete(tmp);
            throw new IOException($"download incomplete: {rel.AssetName}");
        }
        File.Move(tmp, path, overwrite: true);
        progress?.Report(1);
        return path;
    }
}
