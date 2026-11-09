using System.Windows;
using System.Windows.Controls;
using MeshhessenClient.Services;

namespace MeshhessenClient;

/// <summary>
/// Minimal dialog for the own node status (StatusMessage module). Empty text clears the
/// status. The wire limit is BYTES (nanopb max_size:80 → 79 usable), not characters.
/// </summary>
public class NodeStatusDialog : Window
{
    private readonly TextBox _textBox;
    private readonly TextBlock _byteHint;
    private const int MaxBytes = 79;

    public string StatusText => _textBox.Text;

    private static string Loc(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;

    public NodeStatusDialog(string currentStatus)
    {
        Title = Loc("StrNodeStatusDialogTitle");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ModernWpf.Controls.Primitives.WindowHelper.SetUseModernWindowStyle(this, true);

        var panel = new StackPanel { Margin = new Thickness(20) };

        panel.Children.Add(new TextBlock
        {
            Text = Loc("StrNodeStatusDialogHint"),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 10)
        });

        _textBox = new TextBox
        {
            Text = currentStatus,
            MaxLength = MaxBytes, // chars; multi-byte input is caught by the byte hint/check
            Padding = new Thickness(8),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false
        };
        _textBox.TextChanged += (_, _) => UpdateByteHint();
        panel.Children.Add(_textBox);

        _byteHint = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.6,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        panel.Children.Add(_byteHint);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var ok = new Button { Content = "OK", Width = 90, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (Utf8Text.ByteCount(_textBox.Text.Trim()) > MaxBytes)
            {
                MessageBox.Show(Loc("StrNodeStatusTooLong"), Loc("StrError"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
            Close();
        };
        var cancel = new Button { Content = Loc("StrCancel"), Width = 100, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;
        UpdateByteHint();
        Loaded += (_, _) => { _textBox.Focus(); _textBox.CaretIndex = _textBox.Text.Length; };
    }

    private void UpdateByteHint()
    {
        int bytes = Utf8Text.ByteCount(_textBox.Text.Trim());
        _byteHint.Text = $"{bytes} / {MaxBytes} Byte";
        _byteHint.Foreground = bytes > MaxBytes
            ? System.Windows.Media.Brushes.OrangeRed
            : System.Windows.Media.Brushes.Gray;
    }
}
