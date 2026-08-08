using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MeshhessenClient.Helpers;

/// <summary>
/// Applies the Meshtastic text-formatting markers to a message input TextBox and
/// builds the little B / I / S / &lt;/&gt; / link toolbar. The markers are part of
/// the sent text and count toward the length limit, so wrapping respects MaxLength.
/// </summary>
public static class TextFormattingHelper
{
    /// <summary>Wrap the current selection in open/close markers (or insert the
    /// markers at the caret and place the caret between them when nothing is selected).</summary>
    public static void Wrap(TextBox tb, string open, string close)
    {
        if (tb == null) return;

        int selStart = tb.SelectionStart;
        int selLen   = tb.SelectionLength;
        string sel   = tb.SelectedText ?? string.Empty;
        string insert = open + sel + close;

        if (WouldExceed(tb, selLen, insert.Length)) return;

        tb.SelectedText = insert;
        tb.SelectionStart  = selLen > 0 ? selStart + insert.Length : selStart + open.Length;
        tb.SelectionLength = 0;
        tb.Focus();
    }

    /// <summary>Insert a [label](https://) link. If text is selected it becomes the
    /// label and the URL placeholder is selected for typing; otherwise a placeholder
    /// label is inserted and selected.</summary>
    public static void InsertLink(TextBox tb, string defaultLabel)
    {
        if (tb == null) return;

        int selStart = tb.SelectionStart;
        int selLen   = tb.SelectionLength;
        bool hadSel  = selLen > 0;
        string label = hadSel ? tb.SelectedText : (string.IsNullOrEmpty(defaultLabel) ? "Text" : defaultLabel);
        const string urlPart = "https://";
        string insert = $"[{label}]({urlPart})";

        if (WouldExceed(tb, selLen, insert.Length)) return;

        tb.SelectedText = insert;
        if (hadSel)
        {
            // Select the URL placeholder so the user types the address next.
            tb.SelectionStart  = selStart + 1 + label.Length + 2; // past "[label]("
            tb.SelectionLength = urlPart.Length;
        }
        else
        {
            // Select the placeholder label so the user types it first.
            tb.SelectionStart  = selStart + 1;
            tb.SelectionLength = label.Length;
        }
        tb.Focus();
    }

    private static bool WouldExceed(TextBox tb, int selLen, int insertLen)
    {
        if (tb.MaxLength > 0 && tb.Text.Length - selLen + insertLen > tb.MaxLength)
        {
            SystemSounds.Beep.Play();
            return true;
        }
        return false;
    }

    /// <summary>Build the horizontal formatting toolbar wired to <paramref name="target"/>.
    /// Used by the DM window (the channel tab defines the same bar in XAML).</summary>
    public static StackPanel CreateFormatBar(TextBox target, Func<string, string> loc)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(MakeButton(Glyph("B", bold: true),  loc("StrFormatBold"),   () => Wrap(target, "**", "**")));
        bar.Children.Add(MakeButton(Glyph("I", italic: true), loc("StrFormatItalic"), () => Wrap(target, "*", "*")));
        bar.Children.Add(MakeButton(Glyph("S", strike: true), loc("StrFormatStrike"), () => Wrap(target, "~~", "~~")));
        bar.Children.Add(MakeButton(Glyph("</>", mono: true), loc("StrFormatMono"),   () => Wrap(target, "`", "`")));
        bar.Children.Add(MakeButton(Glyph("🔗", emoji: true), loc("StrFormatLink"),   () => InsertLink(target, loc("StrFormatLinkLabel"))));
        return bar;
    }

    private static Button MakeButton(UIElement content, string tooltip, Action onClick)
    {
        var b = new Button
        {
            Content = content,
            ToolTip = tooltip,
            Width = 30,
            Height = 26,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private static TextBlock Glyph(string text, bool bold = false, bool italic = false,
        bool strike = false, bool mono = false, bool emoji = false)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = italic ? FontStyles.Italic : FontStyles.Normal
        };
        if (strike) tb.TextDecorations = TextDecorations.Strikethrough;
        if (mono) { tb.FontFamily = new FontFamily("Consolas"); tb.FontSize = 11; }
        if (emoji) tb.FontFamily = new FontFamily("Segoe UI Emoji");
        return tb;
    }
}
