using System.Windows;

namespace MeshhessenClient;

public partial class NodeKeyMismatchDialog : Window
{
    public NodeKeyMismatchDialog(uint nodeId, string shortName, string oldKeyBase64, string newKeyBase64)
    {
        InitializeComponent();
        NodeLabel.Text = $"!{nodeId:x8}  ({shortName})";
        OldKeyBox.Text = oldKeyBase64;
        NewKeyBox.Text = newKeyBase64;
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Ignore_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
