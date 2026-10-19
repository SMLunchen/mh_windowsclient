using Microsoft.Toolkit.Uwp.Notifications;

namespace MeshhessenClient.Services;

/// <summary>
/// Thin wrapper around Windows toast notifications for new incoming messages.
/// Uses the compat layer, which self-registers an unpackaged WPF app on first use, so no
/// Start-menu shortcut or packaging is required. All calls are best-effort: if the OS has
/// notifications disabled or registration fails, it silently no-ops.
///
/// Behavior:
/// - Clicking a toast activates the app (see <see cref="Initialize"/>).
/// - Requests are batched for a short window: several messages arriving together produce
///   ONE summary toast ("N neue Nachrichten") instead of a minutes-long queue of
///   individual toasts that cannot be dismissed as a whole.
/// - All toasts share one tag, so a newer toast REPLACES the previous one in the
///   Action Center instead of stacking endlessly.
/// </summary>
public static class ToastService
{
    private const string ToastTag   = "mh-messages";
    private const string ToastGroup = "meshhessen";
    private const int    BatchWindowMs = 1500;

    private static readonly object _lock = new();
    private static readonly List<(string Title, string Body)> _pending = new();
    private static System.Threading.Timer? _flushTimer;
    private static Action?  _activateApp;
    private static string   _summaryTitleFormat = "{0} neue Nachrichten";
    private static bool     _activationHooked;

    /// <summary>Wires toast activation (click → bring app to front) and the localized
    /// summary-title format ("{0} …"). Call once from the main window.</summary>
    public static void Initialize(Action activateApp, string summaryTitleFormat)
    {
        _activateApp = activateApp;
        if (!string.IsNullOrWhiteSpace(summaryTitleFormat))
            _summaryTitleFormat = summaryTitleFormat;

        if (_activationHooked) return;
        try
        {
            // Registers the COM activator for the unpackaged app; fires when the user
            // clicks any of our toasts (both while running and from the Action Center).
            ToastNotificationManagerCompat.OnActivated += _ => _activateApp?.Invoke();
            _activationHooked = true;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Toast activation hook failed: {ex.Message}");
        }
    }

    /// <summary>Queues a message toast (title = sender/context, body = message text).
    /// Flushed after a short batch window; multiple queued messages become one summary.</summary>
    public static void ShowMessage(string title, string body)
    {
        lock (_lock)
        {
            _pending.Add((title, body));
            // Fixed (non-sliding) window: the first message opens it, everything arriving
            // within it joins the batch. No starvation during long backlog floods.
            if (_pending.Count == 1)
            {
                _flushTimer?.Dispose();
                _flushTimer = new System.Threading.Timer(_ => Flush(), null, BatchWindowMs,
                    System.Threading.Timeout.Infinite);
            }
        }
    }

    private static void Flush()
    {
        (string Title, string Body)[] batch;
        lock (_lock)
        {
            batch = _pending.ToArray();
            _pending.Clear();
        }
        if (batch.Length == 0) return;

        try
        {
            string title, body;
            if (batch.Length == 1)
            {
                (title, body) = batch[0];
            }
            else
            {
                title = string.Format(_summaryTitleFormat, batch.Length);
                // "Alice (3) · Bob · Kanal X …" — up to 4 distinct senders
                var senders = batch.GroupBy(x => x.Title)
                    .Select(g => g.Count() > 1 ? $"{g.Key} ({g.Count()})" : g.Key)
                    .ToList();
                body = string.Join(" · ", senders.Take(4)) + (senders.Count > 4 ? " …" : "");
            }

            new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .Show(t =>
                {
                    t.Tag   = ToastTag;   // same tag ⇒ replaces the previous toast
                    t.Group = ToastGroup;
                });
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Toast failed: {ex.Message}");
        }
    }
}
