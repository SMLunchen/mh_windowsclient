using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace MeshhessenClient.Helpers;

/// <summary>
/// Attached property to allow binding a FlowDocument to RichTextBox.Document.
/// Standard WPF does not support direct binding of Document in a DataTemplate context.
/// Once the document is attached, its emoji are swapped for Emoji.Wpf inlines: WPF has no colour-font
/// support, so message bodies rendered emoji in black and white while names and reactions (emoji:TextBlock)
/// were already in colour. Done here rather than in the converter so the inlines inherit the RichTextBox's
/// real font size and foreground.
/// </summary>
public static class RichTextBoxHelper
{
    public static readonly DependencyProperty BoundDocumentProperty =
        DependencyProperty.RegisterAttached(
            "BoundDocument",
            typeof(FlowDocument),
            typeof(RichTextBoxHelper),
            new PropertyMetadata(null, OnBoundDocumentChanged));

    private static void OnBoundDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RichTextBox rtb && e.NewValue is FlowDocument doc)
        {
            rtb.Document = doc;
            try
            {
                // SubstituteOptions.None: palette colours as the font defines them (ColorBlend would tint the
                // glyphs with the theme's text colour — washed out in dark mode).
                Emoji.Wpf.FlowDocumentExtensions.SubstituteGlyphs(doc, Emoji.Wpf.SubstituteOptions.None);
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"[Emoji] Inline substitution failed: {ex.Message}");
            }
        }
    }

    public static void SetBoundDocument(DependencyObject d, FlowDocument value)
        => d.SetValue(BoundDocumentProperty, value);

    public static FlowDocument GetBoundDocument(DependencyObject d)
        => (FlowDocument)d.GetValue(BoundDocumentProperty);
}
