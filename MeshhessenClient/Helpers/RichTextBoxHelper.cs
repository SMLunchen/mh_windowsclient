using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace MeshhessenClient.Helpers;

/// <summary>
/// Attached property to allow binding a FlowDocument to RichTextBox.Document.
/// Standard WPF does not support direct binding of Document in a DataTemplate context.
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
            rtb.Document = doc;
    }

    public static void SetBoundDocument(DependencyObject d, FlowDocument value)
        => d.SetValue(BoundDocumentProperty, value);

    public static FlowDocument GetBoundDocument(DependencyObject d)
        => (FlowDocument)d.GetValue(BoundDocumentProperty);
}
