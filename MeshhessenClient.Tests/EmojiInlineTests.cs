using System.Globalization;
using System.Windows.Controls;
using System.Windows.Documents;
using MeshhessenClient.Converters;
using MeshhessenClient.Helpers;

namespace MeshhessenClient.Tests;

/// <summary>
/// Message bodies are rendered through MessageDocumentConverter → FlowDocument → RichTextBoxHelper. WPF draws emoji
/// in a FlowDocument monochrome (no colour-font support); the helper swaps them for Emoji.Wpf inlines once the
/// document is attached. User report: "🫡 wird nicht korrekt angezeigt, und Smileys bitte in bunt" — Emoji.Wpf
/// 0.3.3 knew Emoji 13.1 only, 0.3.4 carries 15.0.
/// </summary>
public class EmojiInlineTests
{
    private static int CountEmojiInlines(InlineCollection inlines)
    {
        int n = 0;
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Emoji.Wpf.EmojiInline: n++; break;
                case Span span: n += CountEmojiInlines(span.Inlines); break;   // Bold, Italic, Hyperlink, Span
            }
        }
        return n;
    }

    private static int CountEmojiInlines(FlowDocument doc)
    {
        int n = 0;
        foreach (var block in doc.Blocks)
            if (block is Paragraph p) n += CountEmojiInlines(p.Inlines);
        return n;
    }

    [Fact]
    public void Emoji_data_knows_unicode_14_and_15()
    {
        Assert.True(Emoji.Wpf.EmojiData.MatchOne.IsMatch("🫡"), "saluting face (U+1FAE1, Emoji 14.0)");
        Assert.True(Emoji.Wpf.EmojiData.MatchOne.IsMatch("🫨"), "shaking face (U+1FAE8, Emoji 15.0)");
        Assert.True(Emoji.Wpf.EmojiData.MatchOne.IsMatch("😁"));
        Assert.False(Emoji.Wpf.EmojiData.MatchOne.IsMatch("Hallo"));
    }

    [Fact]
    public void Bound_message_document_gets_colour_emoji_inlines_and_keeps_text_and_markup()
    {
        WpfTestApp.Run(() =>
        {
            var converter = new MessageDocumentConverter();
            var doc = (FlowDocument)converter.Convert("Hallo zusammen! 🫡 und 😁 **fett** https://example.org/x",
                                                      typeof(FlowDocument), null!, CultureInfo.InvariantCulture);
            Assert.Equal(0, CountEmojiInlines(doc));             // converter stays markup-only

            var rtb = new RichTextBox();
            RichTextBoxHelper.SetBoundDocument(rtb, doc);          // attaches + substitutes

            Assert.Same(doc, rtb.Document);
            Assert.Equal(2, CountEmojiInlines(doc));
            var text = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
            Assert.Contains("Hallo zusammen!", text);
            Assert.Contains("fett", text);
            Assert.DoesNotContain("🫡", text);                     // replaced by an inline, not left as plain text
            Assert.Single(doc.Blocks);
            var para = (Paragraph)doc.Blocks.First();
            Assert.Contains(para.Inlines, i => i is Bold);
            Assert.Contains(para.Inlines, i => i is Hyperlink);
        });
    }

    [Fact]
    public void Document_without_emoji_is_left_untouched()
    {
        WpfTestApp.Run(() =>
        {
            var doc = (FlowDocument)new MessageDocumentConverter().Convert("nur Text", typeof(FlowDocument), null!, CultureInfo.InvariantCulture);
            var rtb = new RichTextBox();
            RichTextBoxHelper.SetBoundDocument(rtb, doc);
            Assert.Equal(0, CountEmojiInlines(doc));
            Assert.Equal("nur Text", new TextRange(doc.ContentStart, doc.ContentEnd).Text.TrimEnd('\r', '\n'));
        });
    }
}
