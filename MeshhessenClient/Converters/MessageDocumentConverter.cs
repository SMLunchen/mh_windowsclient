using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace MeshhessenClient.Converters;

/// <summary>
/// Converts a message string to a FlowDocument with clickable hyperlinks for URLs.
/// Used in the message list RichTextBox binding.
/// </summary>
public class MessageDocumentConverter : IValueConverter
{
    private static readonly Regex UrlRegex = new(
        @"https?://[^\s\)\]\>""]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string ?? string.Empty;
        var para = new Paragraph { Margin = new Thickness(0) };

        int last = 0;
        foreach (Match m in UrlRegex.Matches(text))
        {
            if (m.Index > last)
                para.Inlines.Add(new Run(text[last..m.Index]));

            var link = new Hyperlink(new Run(m.Value));
            try { link.NavigateUri = new Uri(m.Value); }
            catch { para.Inlines.Add(new Run(m.Value)); last = m.Index + m.Length; continue; }
            link.RequestNavigate += (_, e) =>
            {
                try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                catch { }
                e.Handled = true;
            };
            para.Inlines.Add(link);
            last = m.Index + m.Length;
        }

        if (last < text.Length)
            para.Inlines.Add(new Run(text[last..]));

        return new FlowDocument(para)
        {
            PagePadding   = new Thickness(0),
            LineHeight     = double.NaN,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
