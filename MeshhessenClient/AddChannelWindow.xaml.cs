using System.Security.Cryptography;
using System.Windows;

namespace MeshhessenClient;

public partial class AddChannelWindow : Window
{
    public string ChannelName => ChannelNameTextBox.Text.Trim();
    public string PskBase64 => PskTextBox.Text.Trim();

    public AddChannelWindow()
    {
        InitializeComponent();
    }

    public AddChannelWindow(string name, string pskBase64) : this()
    {
        ChannelNameTextBox.Text = name;
        PskTextBox.Text = pskBase64;
        ChannelNameTextBox.IsReadOnly = true;
        PskTextBox.IsReadOnly = true;
    }

    private void GeneratePsk_Click(object sender, RoutedEventArgs e)
    {
        var psk = new byte[32];
        RandomNumberGenerator.Fill(psk);
        PskTextBox.Text = Convert.ToBase64String(psk);
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ChannelName))
        {
            MessageBox.Show("Bitte einen Kanalnamen eingeben.", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(PskBase64))
        {
            MessageBox.Show("Bitte einen PSK eingeben oder generieren.", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var bytes = Convert.FromBase64String(PskBase64);
            if (bytes.Length != 32 && bytes.Length != 16 && bytes.Length != 1 && bytes.Length != 0)
            {
                MessageBox.Show("PSK muss 0, 1, 16 oder 32 Bytes lang sein.", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        catch (FormatException)
        {
            MessageBox.Show("PSK ist kein gültiger Base64-String.", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
