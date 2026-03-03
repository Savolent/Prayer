using System;
using System.IO;
using System.Linq;
using System.Text.Json;

public partial class SpaceMoltHttpClient
{
    private void LoadMarketCachesFromDisk()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.MarketsDir);
        }
        catch
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(AppPaths.MarketsDir, "*.json"))
        {
            try
            {
                string raw = File.ReadAllText(file);
                var snapshot = JsonSerializer.Deserialize<MarketCacheSnapshot>(raw);

                if (snapshot == null ||
                    string.IsNullOrWhiteSpace(snapshot.StationId) ||
                    snapshot.Market == null ||
                    snapshot.CapturedAtUtc == default)
                {
                    SafeDelete(file);
                    continue;
                }

                var age = DateTime.UtcNow - snapshot.CapturedAtUtc;
                if (age > MarketCacheTtl)
                {
                    SafeDelete(file);
                    continue;
                }

                if (!_stationCache.TryGetValue(snapshot.StationId, out var stationInfo))
                {
                    stationInfo = new StationInfo
                    {
                        StationId = snapshot.StationId
                    };
                    _stationCache[snapshot.StationId] = stationInfo;
                }

                stationInfo.Market = snapshot.Market;
            }
            catch
            {
                SafeDelete(file);
            }
        }
    }

    private void LoadShipyardCachesFromDisk()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ShipyardsDir);
        }
        catch
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(AppPaths.ShipyardsDir, "*.json"))
        {
            try
            {
                string raw = File.ReadAllText(file);
                var snapshot = JsonSerializer.Deserialize<ShipyardCacheSnapshot>(raw);

                if (snapshot == null ||
                    string.IsNullOrWhiteSpace(snapshot.StationId) ||
                    snapshot.CapturedAtUtc == default)
                {
                    SafeDelete(file);
                    continue;
                }

                var age = DateTime.UtcNow - snapshot.CapturedAtUtc;
                if (age > ShipyardCacheTtl)
                {
                    SafeDelete(file);
                    continue;
                }

                if (!_stationCache.TryGetValue(snapshot.StationId, out var stationInfo))
                {
                    stationInfo = new StationInfo
                    {
                        StationId = snapshot.StationId
                    };
                    _stationCache[snapshot.StationId] = stationInfo;
                }

                stationInfo.ShipyardShowroomLines = snapshot.ShowroomLines ?? Array.Empty<string>();
                stationInfo.ShipyardListingLines = snapshot.ListingLines ?? Array.Empty<string>();
            }
            catch
            {
                SafeDelete(file);
            }
        }
    }

    private void SaveMarketCacheToDisk(string stationId, MarketState market)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.MarketsDir);
            var snapshot = new MarketCacheSnapshot
            {
                StationId = stationId,
                CapturedAtUtc = DateTime.UtcNow,
                Market = market
            };

            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(GetMarketCachePath(stationId), json);
        }
        catch
        {
            // Ignore write failures; in-memory cache remains available.
        }
    }

    private void SaveShipyardCacheToDisk(
        string stationId,
        string[] showroomLines,
        string[] listingLines)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ShipyardsDir);
            var snapshot = new ShipyardCacheSnapshot
            {
                StationId = stationId,
                CapturedAtUtc = DateTime.UtcNow,
                ShowroomLines = showroomLines ?? Array.Empty<string>(),
                ListingLines = listingLines ?? Array.Empty<string>()
            };

            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(GetShipyardCachePath(stationId), json);
        }
        catch
        {
            // Ignore write failures; in-memory cache remains available.
        }
    }

    private void LoadCatalogueCachesFromDisk()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.CatalogsDir);
        }
        catch
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(AppPaths.CatalogsDir, "*.json"))
        {
            try
            {
                DateTime capturedAtUtc = File.GetLastWriteTimeUtc(file);
                var age = DateTime.UtcNow - capturedAtUtc;
                if (age > CatalogueCacheTtl)
                {
                    SafeDelete(file);
                    continue;
                }

                string raw = File.ReadAllText(file);
                JsonElement response = JsonSerializer.Deserialize<JsonElement>(raw);
                var catalogue = Catalogue.FromJson(response);
                string fileKey = Path.GetFileNameWithoutExtension(file);
                _catalogueCache[fileKey] = new CatalogueCacheEntry
                {
                    Catalogue = catalogue,
                    CachedAtUtc = capturedAtUtc
                };
            }
            catch
            {
                SafeDelete(file);
            }
        }
    }

    private void SaveCatalogueCacheToDisk(string fileKey, string rawResponse, Catalogue catalogue)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.CatalogsDir);
            _catalogueCache[fileKey] = new CatalogueCacheEntry
            {
                CachedAtUtc = DateTime.UtcNow,
                Catalogue = catalogue ?? new Catalogue()
            };

            File.WriteAllText(GetCatalogueCachePath(fileKey), rawResponse);
        }
        catch
        {
            // Ignore write failures; in-memory cache remains available.
        }
    }

    private static string GetMarketCachePath(string stationId)
    {
        return Path.Combine(AppPaths.MarketsDir, SanitizeFileName(stationId) + ".json");
    }

    private static string GetShipyardCachePath(string stationId)
    {
        return Path.Combine(AppPaths.ShipyardsDir, SanitizeFileName(stationId) + ".json");
    }

    private static string GetCatalogueCachePath(string fileKey)
    {
        return Path.Combine(AppPaths.CatalogsDir, fileKey + ".json");
    }

    private static string BuildCatalogueCacheKey(
        string type,
        string? category,
        string? id,
        int? page,
        int? pageSize,
        string? search)
    {
        return string.Join("|",
            type ?? "",
            category ?? "",
            id ?? "",
            page?.ToString() ?? "",
            pageSize?.ToString() ?? "",
            search ?? "");
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown_station";

        var chars = value
            .Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_')
            .ToArray();

        return new string(chars);
    }

    private static void SafeDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }
}
