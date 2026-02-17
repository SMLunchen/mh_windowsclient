using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace MeshhessenClient;

public class CsvChannelEntry
{
    public string Bundesland { get; set; } = "";
    public string Name { get; set; } = "";
    public string Psk { get; set; } = "";
    public string MqttEnabled { get; set; } = "";
    public string Bemerkung { get; set; } = "";
}

public partial class ChannelBrowserWindow : Window
{
    private List<CsvChannelEntry> _allEntries = new();
    public CsvChannelEntry? SelectedChannel { get; private set; }

    public ChannelBrowserWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadChannelsAsync();
    }

    private async Task LoadChannelsAsync()
    {
        string? csvContent = null;

        // Try online first
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            csvContent = await http.GetStringAsync(
                "https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/CHANNELS.csv");
            StatusText.Text = "Online-Liste geladen";
            Services.Logger.WriteLine("Channel list loaded from GitHub");
        }
        catch
        {
            Services.Logger.WriteLine("GitHub download failed, using embedded CHANNELS.csv");
        }

        // Fallback: embedded resource
        if (csvContent == null)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("MeshhessenClient.CHANNELS.csv");
            if (stream == null)
            {
                MessageBox.Show("Kanalliste konnte nicht geladen werden.", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            using var reader = new StreamReader(stream);
            csvContent = await reader.ReadToEndAsync();
            StatusText.Text = "Offline-Liste geladen";
        }

        ParseCsv(csvContent);
        PopulateBundeslandFilter();
        ApplyFilter();
    }

    private void ParseCsv(string csv)
    {
        _allEntries.Clear();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Trim().Split(';');
            if (parts.Length < 3) continue;
            _allEntries.Add(new CsvChannelEntry
            {
                Bundesland = parts[0].Trim(),
                Name = parts[1].Trim(),
                Psk = parts[2].Trim(),
                MqttEnabled = parts.Length > 3 ? parts[3].Trim() : "",
                Bemerkung = parts.Length > 4 ? parts[4].Trim() : ""
            });
        }
    }

    private void PopulateBundeslandFilter()
    {
        BundeslandFilterComboBox.Items.Clear();
        BundeslandFilterComboBox.Items.Add("(Alle)");

        var bundeslaender = _allEntries.Select(e => e.Bundesland).Distinct().OrderBy(b => b);
        foreach (var bl in bundeslaender)
            BundeslandFilterComboBox.Items.Add(bl);

        BundeslandFilterComboBox.SelectedIndex = 0;
    }

    private void BundeslandFilter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void SearchText_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var selectedBl = BundeslandFilterComboBox.SelectedItem as string ?? "";
        if (selectedBl == "(Alle)") selectedBl = "";
        var searchText = SearchTextBox.Text.Trim().ToLowerInvariant();

        var filtered = _allEntries
            .Where(e => string.IsNullOrEmpty(selectedBl) || e.Bundesland == selectedBl)
            .Where(e => string.IsNullOrEmpty(searchText)
                || e.Name.ToLowerInvariant().Contains(searchText)
                || e.Bundesland.ToLowerInvariant().Contains(searchText)
                || e.Bemerkung.ToLowerInvariant().Contains(searchText))
            .ToList();

        ChannelListView.ItemsSource = filtered;
    }

    private void AddSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ChannelListView.SelectedItem is CsvChannelEntry entry)
        {
            SelectedChannel = entry;
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show("Bitte einen Kanal auswählen.", "Hinweis",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
