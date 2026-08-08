using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace MeshhessenClient.Converters;

/// <summary>
/// Converts a message string to a FlowDocument, rendering the Meshtastic text
/// formatting subset and clickable links. Used in the message list RichTextBox
/// binding (channel chat and DMs share this converter).
///
/// Supported markup (the marker characters are part of the sent text and count
/// toward the length limit — only the rendering changes):
///   **bold**   *italic*   ~~strikethrough~~   `monospace`   [label](https://url)
/// plus bare http(s) URLs, which stay auto-linked.
/// </summary>
public class MessageDocumentConverter : IValueConverter
{
    // Bare URL autolink (used inside literal text runs). Excludes trailing ) ] > "
    // so it doesn't swallow surrounding markup punctuation.
    private static readonly Regex UrlRegex = new(
        @"https?://[^\s\)\]\>""]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // [label](https://url) — anchored, matched at the current scan position.
    private static readonly Regex LinkRegex = new(
        @"^\[([^\]]*)\]\((https?://[^\s)]+)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly FontFamily MonoFont = new("Consolas");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string ?? string.Empty;
        var para = new Paragraph { Margin = new Thickness(0) };
        AppendFormatted(para.Inlines, text);

        return new FlowDocument(para)
        {
            PagePadding = new Thickness(0),
            LineHeight  = double.NaN,
        };
    }

    /// <summary>
    /// Parse the Meshtastic formatting subset into inlines. Recurses into the
    /// content of bold/italic/strikethrough spans so formatting can nest; the
    /// content of a `code` span is taken literally.
    /// </summary>
    private static void AppendFormatted(InlineCollection target, string text)
    {
        int i = 0;
        var literal = new StringBuilder();

        void FlushLiteral()
        {
            if (literal.Length > 0)
            {
                AppendWithUrls(target, literal.ToString());
                literal.Clear();
            }
        }

        while (i < text.Length)
        {
            char c = text[i];

            // `monospace` — literal content, no inner formatting
            if (c == '`')
            {
                int close = text.IndexOf('`', i + 1);
                if (close > i)
                {
                    FlushLiteral();
                    target.Add(new Run(text.Substring(i + 1, close - i - 1)) { FontFamily = MonoFont });
                    i = close + 1;
                    continue;
                }
            }

            // [label](https://url)
            if (c == '[')
            {
                var m = LinkRegex.Match(text[i..]);
                if (m.Success)
                {
                    FlushLiteral();
                    var url = m.Groups[2].Value;
                    var label = m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : url;
                    target.Add(BuildLink(url, label));
                    i += m.Length;
                    continue;
                }
            }

            // **bold**
            if (c == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int close = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (close > i + 1)
                {
                    FlushLiteral();
                    var bold = new Bold();
                    AppendFormatted(bold.Inlines, text.Substring(i + 2, close - i - 2));
                    target.Add(bold);
                    i = close + 2;
                    continue;
                }
            }

            // ~~strikethrough~~
            if (c == '~' && i + 1 < text.Length && text[i + 1] == '~')
            {
                int close = text.IndexOf("~~", i + 2, StringComparison.Ordinal);
                if (close > i + 1)
                {
                    FlushLiteral();
                    var span = new Span { TextDecorations = TextDecorations.Strikethrough };
                    AppendFormatted(span.Inlines, text.Substring(i + 2, close - i - 2));
                    target.Add(span);
                    i = close + 2;
                    continue;
                }
            }

            // *italic*
            if (c == '*')
            {
                int close = text.IndexOf('*', i + 1);
                if (close > i)
                {
                    FlushLiteral();
                    var italic = new Italic();
                    AppendFormatted(italic.Inlines, text.Substring(i + 1, close - i - 1));
                    target.Add(italic);
                    i = close + 1;
                    continue;
                }
            }

            literal.Append(c);
            i++;
        }

        FlushLiteral();
    }

    // Append literal text, auto-linking any bare http(s) URLs inside it.
    private static void AppendWithUrls(InlineCollection target, string text)
    {
        int last = 0;
        foreach (Match m in UrlRegex.Matches(text))
        {
            if (m.Index > last)
                target.Add(new Run(text[last..m.Index]));
            target.Add(BuildLink(m.Value, m.Value));
            last = m.Index + m.Length;
        }
        if (last < text.Length)
            target.Add(new Run(text[last..]));
    }

    private static Inline BuildLink(string url, string display)
    {
        Uri uri;
        try { uri = new Uri(url); }
        catch { return new Run(display); }

        var link = new Hyperlink(new Run(display)) { NavigateUri = uri };
        link.RequestNavigate += (_, e) =>
        {
            try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
            catch { }
            e.Handled = true;
        };
        return link;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
