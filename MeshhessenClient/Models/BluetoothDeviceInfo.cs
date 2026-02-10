namespace MeshhessenClient.Models;

/// <summary>
/// Model for Bluetooth device information displayed in UI
/// </summary>
public class BluetoothDeviceInfo
{
    /// <summary>
    /// Device name (e.g., "T-Deck", "Meshtastic_1234")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Bluetooth device address (64-bit)
    /// </summary>
    public ulong Address { get; set; }

    /// <summary>
    /// Display name combining name and address for UI
    /// </summary>
    public string DisplayName => $"{Name} ({Address:X})";
}
