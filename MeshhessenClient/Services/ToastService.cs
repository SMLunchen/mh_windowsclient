using Microsoft.Toolkit.Uwp.Notifications;

namespace MeshhessenClient.Services;

/// <summary>
/// Thin wrapper around Windows toast notifications for new incoming messages.
/// Uses the compat layer, which self-registers an unpackaged WPF app on first use, so no
/// Start-menu shortcut or packaging is required. All calls are best-effort: if the OS has
/// notifications disabled or registration fails, it silently no-ops.
/// </summary>
public static class ToastService
{
    /// <summary>Shows a message toast (title = sender/context, body = message text).</summary>
    public static void ShowMessage(string title, string body)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .Show();
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Toast failed: {ex.Message}");
        }
    }
}
