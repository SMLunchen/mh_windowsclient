using BruTile;

namespace MeshhessenClient.Services;

/// <summary>
/// Serves pre-downloaded map tiles from the local file system.
/// Tile storage format: maptiles/{source}/{z}/{x}/{yOsm}.png  (OSM/XYZ scheme)
/// </summary>
internal class LocalFileTileProvider : ITileProvider
{
    private readonly string _baseDir;
    private readonly string _sourceFolder;

    public LocalFileTileProvider(string baseDir, string sourceFolder)
    {
        _baseDir = baseDir;
        _sourceFolder = sourceFolder;
    }

    public Task<byte[]?> GetTileAsync(TileInfo tileInfo)
    {
        var z = tileInfo.Index.Level;
        var x = tileInfo.Index.Col;
        // BruTile TMS has Row 0 in the south, OSM files have Y=0 in the north → convert
        var yOsm = (1 << z) - 1 - tileInfo.Index.Row;
        var path = System.IO.Path.Combine(_baseDir, _sourceFolder, z.ToString(), x.ToString(), $"{yOsm}.png");
        if (System.IO.File.Exists(path))
        {
            try { return Task.FromResult<byte[]?>(System.IO.File.ReadAllBytes(path)); } catch { }
        }
        return Task.FromResult<byte[]?>(null);
    }
}
