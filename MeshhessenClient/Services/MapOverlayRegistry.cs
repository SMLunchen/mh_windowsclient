namespace MeshhessenClient.Services;

/// <summary>
/// Registry of switchable vector-map overlays (fire service today, THW /
/// hospitals / … tomorrow). The overlay layers ship inside the server styles
/// with visibility:none – the client only flips visibility for all layers
/// whose id starts with <see cref="LayerPrefix"/> (see map.html setOverlay).
/// While an overlay is off, MapLibre sends zero requests to its tile source.
///
/// Adding a new overlay = one entry here + one i18n string + a toggle element.
/// Active overlays are persisted as CSV of keys in AppSettings.MapOverlays.
/// </summary>
public record MapOverlayDef(
    string Key,               // settings key (CSV in AppSettings.MapOverlays)
    string LayerPrefix,       // style layer-id prefix, e.g. "em-"
    string NameResourceKey,   // i18n key; "<key>Tooltip" is used as tooltip
    string TileSource,        // tile endpoint path on the vector server
    int MinZoom,              // tile source zoom window (for offline download)
    int MaxZoom);

public static class MapOverlayRegistry
{
    public static readonly MapOverlayDef[] All =
    {
        // Feuerwehr/Rettung: Hydranten, Wachen, Sirenen, Defis u. a.
        new("emergency", "em-", "StrOverlayEmergency", "emergency", 13, 17),
    };

    public static HashSet<string> ParseActive(string? csv) =>
        (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
