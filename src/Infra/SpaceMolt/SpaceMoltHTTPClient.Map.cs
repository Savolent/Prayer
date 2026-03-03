using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

public partial class SpaceMoltHttpClient
{
    public async Task<GalaxyMapSnapshot> GetMapSnapshotAsync(bool forceRefresh = false)
    {
        await _mapCacheLock.WaitAsync();
        try
        {
            if (!forceRefresh && _cachedMap != null && _cachedMap.Systems.Count > 0)
                return _cachedMap;

            if (!forceRefresh && File.Exists(MapCacheFile))
            {
                try
                {
                    string rawCache = await File.ReadAllTextAsync(MapCacheFile);
                    var hydrated = JsonSerializer.Deserialize<GalaxyMapSnapshot>(rawCache);
                    if (hydrated != null && hydrated.Systems.Count > 0)
                    {
                        _cachedMap = hydrated;
                        return _cachedMap;
                    }
                }
                catch
                {
                    // Ignore cache read/parse errors and refresh from API.
                }
            }

            JsonElement mapResult = await ExecuteAsync("get_map");

            try
            {
                string rawMap = JsonSerializer.Serialize(mapResult, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(RawMapCacheFile, rawMap);
            }
            catch
            {
                // Ignore raw cache write failures; continue with parsed map cache.
            }

            var parsed = ParseMapSnapshot(mapResult);
            _cachedMap = parsed;

            try
            {
                string serialized = JsonSerializer.Serialize(parsed, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(MapCacheFile, serialized);
            }
            catch
            {
                // Ignore cache write failures; keep in-memory map.
            }

            return _cachedMap;
        }
        finally
        {
            _mapCacheLock.Release();
        }
    }

    private static GalaxyMapSnapshot ParseMapSnapshot(JsonElement mapResult)
    {
        var systems = new List<GalaxySystemInfo>();

        foreach (var systemObj in EnumerateSystemsFromMap(mapResult))
        {
            if (systemObj.ValueKind != JsonValueKind.Object)
                continue;

            string? systemId = TryGetString(systemObj, "id")
                               ?? TryGetString(systemObj, "system_id");

            if (string.IsNullOrWhiteSpace(systemId))
                continue;

            var poiList = new List<GalaxyPoiInfo>();

            if (systemObj.TryGetProperty("pois", out var pois) &&
                pois.ValueKind == JsonValueKind.Array)
            {
                foreach (var poi in pois.EnumerateArray())
                {
                    string? poiId = TryGetString(poi, "id")
                                    ?? TryGetString(poi, "poi_id");

                    if (string.IsNullOrWhiteSpace(poiId))
                        continue;

                    poiList.Add(new GalaxyPoiInfo { Id = poiId });
                }
            }

            systems.Add(new GalaxySystemInfo
            {
                Id = systemId,
                Pois = poiList
            });
        }

        return new GalaxyMapSnapshot { Systems = systems };
    }

    private static IEnumerable<JsonElement> EnumerateSystemsFromMap(JsonElement map)
    {
        if (map.ValueKind != JsonValueKind.Object)
            yield break;

        if (map.TryGetProperty("systems", out var systems) &&
            systems.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in systems.EnumerateArray())
                yield return s;
            yield break;
        }

        if (map.TryGetProperty("map", out var mapObj) &&
            mapObj.ValueKind == JsonValueKind.Object &&
            mapObj.TryGetProperty("systems", out var nestedSystems) &&
            nestedSystems.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in nestedSystems.EnumerateArray())
                yield return s;
            yield break;
        }
    }

    private static string? TryGetString(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return null;

        if (!obj.TryGetProperty(key, out var prop))
            return null;

        return prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static JsonElement CreateMessage(string? message)
    {
        var json = JsonSerializer.Serialize(new { message });
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
