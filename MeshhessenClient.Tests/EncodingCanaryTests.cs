using System.Text;

namespace MeshhessenClient.Tests;

// Encoding canary: this repo has twice been hit by files getting saved through a
// Windows-ANSI (cp1252) encoder instead of UTF-8. The fingerprint differs by direction:
//   (a) UTF-8 bytes re-read as ANSI → mojibake ("fÃ¼r", "â€”") — hit README/docs 2026-10.
//   (b) text WRITTEN as ANSI → every char cp1252 can't express becomes '?'
//       (umlauts survive, ✕/→/emoji die) — hit MainWindow.xaml.cs in commit b2c1ada.
// These tests fail the suite as soon as either fingerprint shows up again, so the
// damage is caught at the next `dotnet test` instead of months later in the UI.
public class EncodingCanaryTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MeshhessenClient.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static IEnumerable<string> SourceFiles(params string[] patterns)
    {
        var dirs = new[] { "MeshhessenClient", "MeshhessenClient.Tests", "docs" };
        foreach (var d in dirs)
        {
            var full = Path.Combine(RepoRoot, d);
            if (!Directory.Exists(full)) continue;
            foreach (var pattern in patterns)
                foreach (var f in Directory.EnumerateFiles(full, pattern, SearchOption.AllDirectories))
                    if (!f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}webview2-data{Path.DirectorySeparatorChar}"))
                        yield return f;
        }
        yield return Path.Combine(RepoRoot, "README.md");
        yield return Path.Combine(RepoRoot, "CHANGELOG.md");
    }

    [Fact]
    public void No_mojibake_sequences_in_text_files()
    {
        // "Ã¼"/"Ã¤"/"Ã¶"/"ÃŸ" and "â€" are UTF-8 umlauts/dashes re-decoded as ANSI —
        // they never occur intentionally in this repo.
        string[] fingerprints = ["Ã¼", "Ã¤", "Ã¶", "ÃŸ", "Ã„", "Ã–", "Ãœ", "â€"];
        var hits = new List<string>();
        foreach (var f in SourceFiles("*.cs", "*.xaml", "*.html", "*.md"))
        {
            if (Path.GetFileName(f) == "EncodingCanaryTests.cs") continue; // holds the fingerprints as literals
            var text = File.ReadAllText(f, Encoding.UTF8);
            foreach (var fp in fingerprints)
                if (text.Contains(fp))
                    hits.Add($"{Path.GetFileName(f)}: '{fp}'");
        }
        Assert.True(hits.Count == 0, "Mojibake found (file was UTF-8 but re-saved as ANSI?):\n" + string.Join("\n", hits));
    }

    [Fact]
    public void No_question_mark_pair_literals_in_code()
    {
        // An assigned '"??"' string or a '$"?? …' interpolation is the classic residue
        // of an emoji (two UTF-16 units) written through an ANSI encoder — exactly how
        // commit b2c1ada mangled 📡/🚨/🔔/📍. Legit single-"?" fallbacks and the
        // null-coalescing operator don't match; comments are skipped.
        string[] fingerprints = ["= \"??\"", ": \"??\"", "$\"??"];
        var hits = new List<string>();
        foreach (var f in SourceFiles("*.cs"))
        {
            if (Path.GetFileName(f) == "EncodingCanaryTests.cs") continue;
            int lineNo = 0;
            foreach (var line in File.ReadLines(f, Encoding.UTF8))
            {
                lineNo++;
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("///")) continue;
                if (fingerprints.Any(line.Contains))
                    hits.Add($"{Path.GetFileName(f)}:{lineNo}");
            }
        }
        Assert.True(hits.Count == 0, "'??' string literal found (mangled emoji?):\n" + string.Join("\n", hits));
    }

    [Fact]
    public void Known_ui_glyphs_survive()
    {
        // Manifest of glyphs that MUST exist in these files. An ANSI save kills every
        // non-cp1252 char in the file at once — one canary glyph per file suffices.
        // If a refactor legitimately removes one, update the manifest here.
        (string File, string Glyph, string What)[] manifest =
        [
            (@"MeshhessenClient\MainWindow.xaml.cs", "✕", "traceroute legend remove button"),
            (@"MeshhessenClient\MainWindow.xaml.cs", "📡", "live traceroute indicator"),
            (@"MeshhessenClient\MainWindow.Map.cs", "→", "map status hop arrow"),
            (@"MeshhessenClient\TelemetryDashboardWindow.xaml.cs", "✕", "widget remove button"),
            (@"MeshhessenClient\Resources\Strings.de.xaml", "🗑", "remove-from-map button text"),
            (@"MeshhessenClient\Resources\Strings.en.xaml", "🗑", "remove-from-map button text"),
            (@"MeshhessenClient\MainWindow.xaml", "✖", "close button glyph"),
        ];
        var missing = new List<string>();
        foreach (var (file, glyph, what) in manifest)
        {
            var path = Path.Combine(RepoRoot, file);
            if (!File.ReadAllText(path, Encoding.UTF8).Contains(glyph))
                missing.Add($"{file}: '{glyph}' ({what})");
        }
        Assert.True(missing.Count == 0, "UI glyph vanished (ANSI save?):\n" + string.Join("\n", missing));
    }
}
