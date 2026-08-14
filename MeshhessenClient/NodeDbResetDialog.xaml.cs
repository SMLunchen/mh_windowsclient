using System.Windows;

namespace MeshhessenClient;

/// <summary>
/// Confirmation dialog for a device NodeDB reset. Offers two opt-in choices:
/// also wiping favorites, and also resetting the client's internal node cache.
/// </summary>
public partial class NodeDbResetDialog : Window
{
    /// <summary>True → favorites should be wiped too (device sends nodedb_reset=false).</summary>
    public bool WipeFavorites { get; private set; }

    /// <summary>True → also clear this client's in-memory node database.</summary>
    public bool ResetInternalDb { get; private set; }

    /// <param name="showInternalOption">
    /// Hide the "reset internal node DB" option (e.g. for a remote node, where it doesn't apply).
    /// </param>
    public NodeDbResetDialog(bool showInternalOption = true)
    {
        InitializeComponent();
        if (!showInternalOption)
        {
            ResetInternalCheck.Visibility = Visibility.Collapsed;
            InternalHint.Visibility = Visibility.Collapsed;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        WipeFavorites = WipeFavoritesCheck.IsChecked == true;
        ResetInternalDb = ResetInternalCheck.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
