using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;

public partial class SpaceMoltHttpClient : IDisposable
{
    private sealed class CatalogueCacheEntry
    {
        public Catalogue Catalogue { get; init; } = new();
        public DateTime CachedAtUtc { get; init; }
    }

    private readonly HttpClient _http;
    private string? _sessionId;
    private readonly List<GameNotification> _pendingNotifications = new();
    private readonly List<GameChatMessage> _recentChatMessages = new();
    private int _currentTick;

    private readonly Dictionary<string, StationInfo> _stationCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CatalogueCacheEntry> _catalogueCache = new(StringComparer.Ordinal);
    private static readonly TimeSpan MarketCacheTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan ShipyardCacheTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan CatalogueCacheTtl = TimeSpan.FromHours(24);
    private const int MaxQueuedNotifications = 100;
    private const int MaxChatMessages = 40;

    private const string BaseUrl = "https://game.spacemolt.com/api/v1/";
    private static readonly string MapCacheFile = AppPaths.GalaxyMapCacheFile;
    private static readonly string RawMapCacheFile = AppPaths.RawMapCacheFile;

    private GalaxyMapSnapshot? _cachedMap;
    private readonly SemaphoreSlim _mapCacheLock = new(1, 1);
    private GameContextKind _mode = GameContextKind.Space;
    private int _shipCatalogPage = 1;
    private long _requestSequence;

    public bool DebugEnabled { get; set; } = true;
    public string DebugContext { get; set; } = "";

    public SpaceMoltHttpClient()
    {
        _http = new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(30);
        LoadMarketCachesFromDisk();
        LoadShipyardCachesFromDisk();
        LoadCatalogueCachesFromDisk();
    }

    // =====================================================
    // SESSION
    // =====================================================

    public async Task CreateSessionAsync()
    {
        var response = await _http.PostAsync(BaseUrl + "session", null);
        var raw = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        var json = JsonSerializer.Deserialize<JsonElement>(raw);

        _sessionId = json.GetProperty("session").GetProperty("id").GetString();

        if (string.IsNullOrWhiteSpace(_sessionId))
            throw new Exception("Failed to create session.");
    }

    public async Task LoginAsync(string username, string password)
    {
        EnsureSession();

        var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "login");
        request.Headers.Add("X-Session-Id", _sessionId);
        request.Content = JsonContent.Create(new { username, password });

        var response = await _http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();

        await EnsureSuccess(response, raw);
    }

    public async Task<string> RegisterAsync(string username, string empire, string registrationCode)
    {
        EnsureSession();

        var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "register");
        request.Headers.Add("X-Session-Id", _sessionId);
        request.Content = JsonContent.Create(new
        {
            username,
            empire,
            registration_code = registrationCode
        });

        var response = await _http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();

        var content = JsonSerializer.Deserialize<JsonElement>(raw);
        await EnsureSuccess(response, raw);

        if (content.TryGetProperty("result", out var result) &&
            result.ValueKind == JsonValueKind.Object &&
            result.TryGetProperty("password", out var passwordElement) &&
            passwordElement.ValueKind == JsonValueKind.String)
        {
            var password = passwordElement.GetString();
            if (!string.IsNullOrWhiteSpace(password))
                return password;
        }

        throw new Exception("Register succeeded but no password was returned by the API.");
    }

    // =====================================================
    // GENERIC EXECUTION
    // =====================================================

    public async Task<JsonElement> ExecuteAsync(string command, object? payload = null)
    {
        EnsureSession();
        long requestId = Interlocked.Increment(ref _requestSequence);
        var timer = Stopwatch.StartNew();

        var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + command);
        request.Headers.Add("X-Session-Id", _sessionId);

        if (payload != null)
            request.Content = JsonContent.Create(payload);

        var response = await _http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();

        if (DebugEnabled)
            await SpaceMoltHttpLogging.LogApiResponseAsync(
                command,
                payload,
                (int)response.StatusCode,
                response.ReasonPhrase,
                raw,
                requestId,
                timer.ElapsedMilliseconds,
                DebugContext);

        JsonElement content;

        try
        {
            content = JsonSerializer.Deserialize<JsonElement>(raw);
        }
        catch
        {
            return CreateMessage("Invalid JSON response");
        }

        ObserveTickFromPayload(content);
        QueueNotifications(content);

        // HTTP failure
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                await SpaceMoltHttpLogging.LogBadRequestAsync(command, payload, raw);

            return content;
        }

        // API error
        if (TryExtractApiError(content, out var code, out var message, out var retryAfterSeconds))
        {
            string details = string.IsNullOrWhiteSpace(code)
                ? (message ?? "Unknown API error")
                : $"{code}: {message ?? "Unknown API error"}";
            if (retryAfterSeconds.HasValue)
                details += $" (retry_after={retryAfterSeconds.Value}s)";
            return CreateMessage(details);
        }

        // Success
        if (content.TryGetProperty("result", out var result))
        {
            return result; // no message field
        }

        return CreateMessage("No result returned");
    }

    public async Task<JsonElement> FindRouteAsync(string targetSystem)
    {
        JsonElement routeResult = await ExecuteAsync(
            "find_route",
            new { target_system = targetSystem });

        await SpaceMoltHttpLogging.LogPathfindAsync(targetSystem, routeResult);
        return routeResult;
    }

    public async Task<Catalogue> GetCatalogueAsync(
        string type,
        string? category = null,
        string? id = null,
        int? page = null,
        int? pageSize = null,
        string? search = null)
    {
        string key = BuildCatalogueCacheKey(type, category, id, page, pageSize, search);
        string fileKey = SanitizeFileName(key);
        if (_catalogueCache.TryGetValue(fileKey, out var cached))
        {
            var age = DateTime.UtcNow - cached.CachedAtUtc;
            if (age <= CatalogueCacheTtl)
            {
                if (string.Equals(type, "items", StringComparison.OrdinalIgnoreCase))
                {
                    await SpaceMoltHttpLogging.LogItemCatalogAsync(
                        type,
                        category,
                        id,
                        page,
                        pageSize,
                        search,
                        "cache",
                        JsonSerializer.Serialize(cached.Catalogue));
                }
                return cached.Catalogue;
            }

            _catalogueCache.Remove(fileKey);
            SafeDelete(GetCatalogueCachePath(fileKey));
        }

        JsonElement result = await ExecuteAsync(
            "catalog",
            new
            {
                type,
                category,
                id,
                page,
                page_size = pageSize,
                search
            });

        Catalogue catalogue = Catalogue.FromJson(result);
        SaveCatalogueCacheToDisk(fileKey, result.GetRawText(), catalogue);
        if (string.Equals(type, "items", StringComparison.OrdinalIgnoreCase))
        {
            await SpaceMoltHttpLogging.LogItemCatalogAsync(
                type,
                category,
                id,
                page,
                pageSize,
                search,
                "api",
                result.GetRawText());
        }
        return catalogue;
    }



    // =====================================================
    // GAME STATE BUILDER
    // =====================================================

    public async Task<GameState> GetGameStateAsync()
    {
        var status = await ExecuteAsync("get_status");
        EnsureCommandSucceeded("get_status", status);
        if (!ObserveTickFromPayload(status))
            _currentTick = Math.Max(1, _currentTick + 1);

        var player = RequireObjectProperty(status, "player", "get_status");
        var ship = RequireObjectProperty(status, "ship", "get_status");

        string currentSystem = RequireStringProperty(player, "current_system", "get_status.player");

        var systemResult = await ExecuteAsync("get_system");
        EnsureCommandSucceeded("get_system", systemResult);
        var systemObj = RequireObjectProperty(systemResult, "system", "get_system");
        var connections = RequireArrayProperty(systemObj, "connections", "get_system.system");

        var jumpTargets = connections
            .EnumerateArray()
            .Select(c => c.GetProperty("system_id").GetString()!)
            .ToArray();

        var currentPoiObj = RequireObjectProperty(systemResult, "poi", "get_system");

        var currentPOI = new POIInfo
        {
            Id = currentPoiObj.GetProperty("id").GetString() ?? "",
            Name = currentPoiObj.GetProperty("name").GetString() ?? "",
            Type = currentPoiObj.GetProperty("type").GetString() ?? "",
            HasBase = currentPoiObj.TryGetProperty("has_base", out var hb) && hb.GetBoolean(),
            BaseId = currentPoiObj.TryGetProperty("base_id", out var bid) ? bid.GetString() : null,
            BaseName = currentPoiObj.TryGetProperty("base_name", out var bn) ? bn.GetString() : null,
            Online = currentPoiObj.TryGetProperty("online", out var on) ? on.GetInt32() : 0
        };

        var pois = systemObj
            .GetProperty("pois")
            .EnumerateArray()
            .Where(p => p.GetProperty("id").GetString() != currentPOI.Id)
            .Select(p => new POIInfo
            {
                Id = p.GetProperty("id").GetString() ?? "",
                Name = p.GetProperty("name").GetString() ?? "",
                Type = p.GetProperty("type").GetString() ?? "",
                HasBase = p.TryGetProperty("has_base", out var hb) && hb.GetBoolean(),
                BaseId = p.TryGetProperty("base_id", out var bid) ? bid.GetString() : null,
                BaseName = p.TryGetProperty("base_name", out var bn) ? bn.GetString() : null,
                Online = p.TryGetProperty("online", out var on) ? on.GetInt32() : 0
            })
            .ToArray();

        // ---------------------------
        // Cargo
        // ---------------------------

        var cargo = new Dictionary<string, ItemStack>();

        if (ship.TryGetProperty("cargo", out var cargoArray))
        {
            foreach (var c in cargoArray.EnumerateArray())
            {
                string itemId = c.GetProperty("item_id").GetString()!;
                int quantity = c.GetProperty("quantity").GetInt32();
                cargo[itemId] = new ItemStack(itemId, quantity);
            }
        }

        // docked_at_base is a string (base id) when docked, ""/null when not
        string? dockedBaseId = null;
        bool docked =
            player.TryGetProperty("docked_at_base", out var d) &&
            d.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(d.GetString());

        if (docked)
            dockedBaseId = d.GetString();

        // ----------------------------------------------------
        // Construct base GameState
        // ----------------------------------------------------

        var state = new GameState
        {
            System = currentSystem,
            CurrentPOI = currentPOI,
            POIs = pois,
            Systems = jumpTargets,
            Cargo = cargo,

            ShipName = ship.TryGetProperty("name", out var shipNameEl) && shipNameEl.ValueKind == JsonValueKind.String
                ? shipNameEl.GetString() ?? ""
                : "",
            ShipClassId = ship.TryGetProperty("class_id", out var shipClassEl) && shipClassEl.ValueKind == JsonValueKind.String
                ? shipClassEl.GetString() ?? ""
                : "",
            Armor = ship.TryGetProperty("armor", out var armorEl) && armorEl.ValueKind == JsonValueKind.Number
                ? armorEl.GetInt32()
                : 0,
            Speed = ship.TryGetProperty("speed", out var speedEl) && speedEl.ValueKind == JsonValueKind.Number
                ? speedEl.GetInt32()
                : 0,
            CpuUsed = ship.TryGetProperty("cpu_used", out var cpuUsedEl) && cpuUsedEl.ValueKind == JsonValueKind.Number
                ? cpuUsedEl.GetInt32()
                : 0,
            CpuCapacity = ship.TryGetProperty("cpu_capacity", out var cpuCapEl) && cpuCapEl.ValueKind == JsonValueKind.Number
                ? cpuCapEl.GetInt32()
                : 0,
            PowerUsed = ship.TryGetProperty("power_used", out var powerUsedEl) && powerUsedEl.ValueKind == JsonValueKind.Number
                ? powerUsedEl.GetInt32()
                : 0,
            PowerCapacity = ship.TryGetProperty("power_capacity", out var powerCapEl) && powerCapEl.ValueKind == JsonValueKind.Number
                ? powerCapEl.GetInt32()
                : 0,
            ModuleCount = ship.TryGetProperty("modules", out var modulesEl) && modulesEl.ValueKind == JsonValueKind.Array
                ? modulesEl.GetArrayLength()
                : 0,

            Fuel = ship.GetProperty("fuel").GetInt32(),
            MaxFuel = ship.GetProperty("max_fuel").GetInt32(),
            Credits = player.GetProperty("credits").GetInt32(),
            Docked = docked,
            Hull = ship.GetProperty("hull").GetInt32(),
            MaxHull = ship.GetProperty("max_hull").GetInt32(),
            Shield = ship.GetProperty("shield").GetInt32(),
            MaxShield = ship.GetProperty("max_shield").GetInt32(),
            CargoUsed = ship.GetProperty("cargo_used").GetInt32(),
            CargoCapacity = ship.GetProperty("cargo_capacity").GetInt32(),
            Shared = new SharedGameState(),
            Notifications = Array.Empty<GameNotification>(),
            ChatMessages = Array.Empty<GameChatMessage>()
        };

        if (!(state.Docked && state.CurrentPOI.IsStation))
            _mode = GameContextKind.Space;

        state.Mode = GameContextMode.FromKind(_mode);

        // ----------------------------------------------------
        // Station Snapshot Cache (storage + market per station)
        // ----------------------------------------------------
        if (state.Docked && state.CurrentPOI.IsStation)
        {
            string stationId = state.CurrentPOI.Id;
            string storageStationId = string.IsNullOrWhiteSpace(dockedBaseId)
                ? stationId
                : dockedBaseId!;

            if (!_stationCache.TryGetValue(stationId, out var stationInfo))
            {
                stationInfo = new StationInfo
                {
                    StationId = stationId
                };
                _stationCache[stationId] = stationInfo;
            }

            var storageResult = await ExecuteAsync("view_storage", new { station_id = storageStationId });
            if (TryParseStorageSnapshot(storageResult, out int credits, out var storageItems))
            {
                stationInfo.StorageCredits = credits;
                stationInfo.StorageItems = storageItems;

                if (stationInfo.StorageCredits > 0)
                {
                    int withdrawnAmount = stationInfo.StorageCredits;
                    var withdrawResult = await ExecuteAsync(
                        "withdraw_credits",
                        new { amount = withdrawnAmount });

                    bool withdrawFailed =
                        withdrawResult.ValueKind == JsonValueKind.Object &&
                        withdrawResult.TryGetProperty("error", out var withdrawError) &&
                        withdrawError.ValueKind == JsonValueKind.Object;

                    if (!withdrawFailed)
                    {
                        stationInfo.StorageCredits = 0;
                        state.Credits += withdrawnAmount;
                    }
                }
            }

            var marketResult = await ExecuteAsync("view_market");
            if (TryParseMarketSnapshot(marketResult, stationId, out var market))
            {
                stationInfo.Market = market;
                SaveMarketCacheToDisk(stationId, market);
            }

            var analyzeMarketResult = await ExecuteAsync("analyze_market");
            await SpaceMoltHttpLogging.LogAnalyzeMarketAsync(stationId, analyzeMarketResult);

            var viewOrdersResult = await ExecuteAsync("view_orders", new { station_id = stationId });
            if (TryParseOwnOrders(viewOrdersResult, out var buyOrders, out var sellOrders))
            {
                stationInfo.BuyOrders = buyOrders;
                stationInfo.SellOrders = sellOrders;
            }

            if (state.Mode.Kind == GameContextKind.Shipyard)
            {
                var showroomResult = await ExecuteAsync("shipyard_showroom");
                if (TryParseShipyardShowroom(showroomResult, out var showroomLines))
                    stationInfo.ShipyardShowroomLines = showroomLines;

                var listingsResult = await ExecuteAsync("browse_ships");
                if (TryParseShipyardListings(listingsResult, out var listingLines))
                    stationInfo.ShipyardListingLines = listingLines;

                SaveShipyardCacheToDisk(
                    stationId,
                    stationInfo.ShipyardShowroomLines,
                    stationInfo.ShipyardListingLines);
            }

            if (state.Mode.Kind == GameContextKind.ShipCatalog)
            {
                var shipCatalogue = await GetCatalogueAsync(
                    type: "ships",
                    page: _shipCatalogPage,
                    pageSize: 12);

                state.ShipCatalogue = shipCatalogue;
            }

            if (state.Mode.Kind == GameContextKind.Hangar)
            {
                var listShipsResult = await ExecuteAsync("list_ships");
                if (TryParseOwnedShips(listShipsResult, out var ownedShips))
                    state.OwnedShips = ownedShips;
            }

            state.Shared.StorageCredits = stationInfo.StorageCredits;
            state.Shared.StorageItems = CloneItems(stationInfo.StorageItems);
            state.Shared.Market = CloneMarket(stationInfo.Market);
            state.Shared.EconomyDeals = BuildBestDealsForCurrentStation(stationId, maxDeals: 3);
            state.Shared.OwnBuyOrders = stationInfo.BuyOrders.ToArray();
            state.Shared.OwnSellOrders = stationInfo.SellOrders.ToArray();
            state.Shared.GlobalMedianBuyPrices = BuildGlobalMedianBuyPrices();
            state.Shared.GlobalMedianSellPrices = BuildGlobalMedianSellPrices();
            state.Shared.GlobalWeightedMidPrices = BuildGlobalWeightedMidPrices();
            state.ShipyardShowroomLines = stationInfo.ShipyardShowroomLines ?? Array.Empty<string>();
            state.ShipyardListingLines = stationInfo.ShipyardListingLines ?? Array.Empty<string>();
            if (state.Mode.Kind != GameContextKind.ShipCatalog)
                state.ShipCatalogue = new Catalogue();
        }
        else
        {
            state.Shared.StorageCredits = 0;
            state.Shared.StorageItems = new Dictionary<string, ItemStack>();
            state.Shared.Market = null;
            state.Shared.EconomyDeals = Array.Empty<EconomyDeal>();
            state.Shared.OwnBuyOrders = Array.Empty<OpenOrderInfo>();
            state.Shared.OwnSellOrders = Array.Empty<OpenOrderInfo>();
            state.Shared.GlobalMedianBuyPrices = BuildGlobalMedianBuyPrices();
            state.Shared.GlobalMedianSellPrices = BuildGlobalMedianSellPrices();
            state.Shared.GlobalWeightedMidPrices = BuildGlobalWeightedMidPrices();
            state.ShipyardShowroomLines = Array.Empty<string>();
            state.ShipyardListingLines = Array.Empty<string>();
            state.ShipCatalogue = new Catalogue();
        }

        if (state.Mode.Kind != GameContextKind.Hangar)
            state.OwnedShips = Array.Empty<OwnedShipInfo>();

        // ----------------------------------------------------
        // Active Missions
        // ----------------------------------------------------
        var activeMissionsResult = await ExecuteAsync("get_active_missions");

        var activeMissions = new List<MissionInfo>();

        if (activeMissionsResult.TryGetProperty("missions", out var activeArray))
        {
            foreach (var m in activeArray.EnumerateArray())
            {
                activeMissions.Add(new MissionInfo
                {
                    Id = m.GetProperty("mission_id").GetString() ?? "",
                    Title = m.GetProperty("title").GetString() ?? "",
                    Description = m.TryGetProperty("description", out var desc)
                        ? desc.GetString() ?? ""
                        : "",
                    ProgressText = m.TryGetProperty("progress_text", out var prog)
                        ? prog.GetString() ?? ""
                        : "",
                    Completed = m.TryGetProperty("completed", out var comp)
                        && comp.GetBoolean()
                });
            }
        }

        // ----------------------------------------------------
        // Available Missions (only when docked)
        // ----------------------------------------------------
        if (state.Docked && state.CurrentPOI.IsStation)
        {
            var missionsResult = await ExecuteAsync("get_missions");

            var available = new List<MissionInfo>();

            if (missionsResult.TryGetProperty("missions", out var missionArray))
            {
                foreach (var m in missionArray.EnumerateArray())
                {
                    available.Add(new MissionInfo
                    {
                        Id = m.GetProperty("template_id").GetString() ?? "",
                        Title = m.GetProperty("title").GetString() ?? "",
                        Description = m.TryGetProperty("description", out var desc)
                            ? desc.GetString() ?? ""
                            : "",
                        ProgressText = "",
                        Completed = false
                    });
                }
            }

            state.AvailableMissions = available.ToArray();
        }
        else
        {
            state.AvailableMissions = Array.Empty<MissionInfo>();
        }

        state.ActiveMissions = activeMissions.ToArray();
        state.Notifications = DrainPendingNotifications(maxCount: 10);
        state.ChatMessages = SnapshotChatMessages(maxCount: 5);

        return state;
    }

    private static bool TryParseShipyardShowroom(JsonElement response, out string[] lines)
    {
        lines = Array.Empty<string>();

        if (response.ValueKind != JsonValueKind.Object)
            return false;

        JsonElement result = response;
        if (response.TryGetProperty("result", out var wrappedResult) &&
            wrappedResult.ValueKind == JsonValueKind.Object)
        {
            result = wrappedResult;
        }

        JsonElement shipsArray = default;
        bool found =
            (result.TryGetProperty("ships", out shipsArray) && shipsArray.ValueKind == JsonValueKind.Array) ||
            (result.TryGetProperty("listings", out shipsArray) && shipsArray.ValueKind == JsonValueKind.Array);
        if (!found)
            return false;

        var entries = new List<string>();
        foreach (var ship in shipsArray.EnumerateArray())
        {
            if (ship.ValueKind != JsonValueKind.Object)
                continue;

            string name = TryGetString(ship, "name") ?? "ship";
            string classId =
                TryGetString(ship, "class_id") ??
                TryGetString(ship, "ship_class") ??
                "-";

            int? hull = TryGetInt(ship, "hull", "base_hull");
            int? shield = TryGetInt(ship, "shield", "base_shield");
            int? cargo = TryGetInt(ship, "cargo", "cargo_capacity");
            int? speed = TryGetInt(ship, "speed", "base_speed");

            decimal? price =
                (ship.TryGetProperty("showroom_price", out var p0) && p0.ValueKind == JsonValueKind.Number && p0.TryGetDecimal(out var d0)) ? d0 :
                (ship.TryGetProperty("price", out var p1) && p1.ValueKind == JsonValueKind.Number && p1.TryGetDecimal(out var d1)) ? d1 :
                null;

            var parts = new List<string> { $"`{name}` ({classId})" };
            if (hull.HasValue || shield.HasValue || cargo.HasValue || speed.HasValue)
                parts.Add($"Hull {hull?.ToString() ?? "-"} | Shield {shield?.ToString() ?? "-"} | Cargo {cargo?.ToString() ?? "-"} | Speed {speed?.ToString() ?? "-"}");
            if (price.HasValue)
                parts.Add($"@ {Math.Round(price.Value, 2):0.##}cr");

            entries.Add(string.Join(" | ", parts));
            if (entries.Count >= 12)
                break;
        }

        lines = entries.ToArray();
        return true;
    }

    private static bool TryParseShipyardListings(JsonElement response, out string[] lines)
    {
        lines = Array.Empty<string>();

        if (response.ValueKind != JsonValueKind.Object)
            return false;

        JsonElement result = response;
        if (response.TryGetProperty("result", out var wrappedResult) &&
            wrappedResult.ValueKind == JsonValueKind.Object)
        {
            result = wrappedResult;
        }

        JsonElement listingsArray = default;
        bool found =
            (result.TryGetProperty("listings", out listingsArray) && listingsArray.ValueKind == JsonValueKind.Array) ||
            (result.TryGetProperty("ships", out listingsArray) && listingsArray.ValueKind == JsonValueKind.Array);
        if (!found)
            return false;

        var entries = new List<string>();
        foreach (var listing in listingsArray.EnumerateArray())
        {
            if (listing.ValueKind != JsonValueKind.Object)
                continue;

            string listingId =
                TryGetString(listing, "listing_id") ??
                TryGetString(listing, "id") ??
                "-";
            string name = TryGetString(listing, "name") ?? "ship";
            string classId =
                TryGetString(listing, "class_id") ??
                TryGetString(listing, "ship_class") ??
                "-";

            decimal? price =
                (listing.TryGetProperty("price", out var p0) && p0.ValueKind == JsonValueKind.Number && p0.TryGetDecimal(out var d0)) ? d0 :
                (listing.TryGetProperty("price_each", out var p1) && p1.ValueKind == JsonValueKind.Number && p1.TryGetDecimal(out var d1)) ? d1 :
                null;

            var parts = new List<string> { $"`{listingId}`: `{name}` ({classId})" };
            if (price.HasValue)
                parts.Add($"@ {Math.Round(price.Value, 2):0.##}cr");

            entries.Add(string.Join(" | ", parts));
            if (entries.Count >= 12)
                break;
        }

        lines = entries.ToArray();
        return true;
    }

    private static bool TryParseOwnedShips(JsonElement response, out OwnedShipInfo[] ships)
    {
        ships = Array.Empty<OwnedShipInfo>();

        if (response.ValueKind != JsonValueKind.Object)
            return false;

        if (!response.TryGetProperty("ships", out var shipsArray) ||
            shipsArray.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parsed = new List<OwnedShipInfo>();
        foreach (var ship in shipsArray.EnumerateArray())
        {
            if (ship.ValueKind != JsonValueKind.Object)
                continue;

            string shipId = TryGetString(ship, "ship_id") ?? "";
            if (string.IsNullOrWhiteSpace(shipId))
                continue;

            parsed.Add(new OwnedShipInfo
            {
                ShipId = shipId,
                ClassId = TryGetString(ship, "class_id") ?? "",
                Location = TryGetString(ship, "location") ?? "",
                IsActive = ship.TryGetProperty("is_active", out var activeEl) && activeEl.ValueKind == JsonValueKind.True
            });
        }

        ships = parsed.ToArray();
        return true;
    }

    private static int? TryGetInt(JsonElement obj, params string[] keys)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var key in keys)
        {
            if (!obj.TryGetProperty(key, out var prop))
                continue;

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int value))
                return value;
        }

        return null;
    }

    private static void EnsureCommandSucceeded(string command, JsonElement payload)
    {
        if (TryExtractApiError(payload, out var code, out var message, out var retryAfterSeconds))
        {
            string details = string.IsNullOrWhiteSpace(code)
                ? (message ?? "Unknown API error")
                : $"{code}: {message ?? "Unknown API error"}";
            if (retryAfterSeconds.HasValue)
                details += $" (retry_after={retryAfterSeconds.Value}s)";
            throw new InvalidOperationException($"SpaceMolt API `{command}` failed: {details}");
        }
    }

    private static JsonElement RequireObjectProperty(JsonElement obj, string property, string command)
    {
        if (obj.ValueKind != JsonValueKind.Object ||
            !obj.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Unexpected `{command}` payload: missing object `{property}`. Payload: {SummarizeJson(obj)}");
        }

        return value;
    }

    private static JsonElement RequireArrayProperty(JsonElement obj, string property, string command)
    {
        if (obj.ValueKind != JsonValueKind.Object ||
            !obj.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"Unexpected `{command}` payload: missing array `{property}`. Payload: {SummarizeJson(obj)}");
        }

        return value;
    }

    private static string RequireStringProperty(JsonElement obj, string property, string command)
    {
        if (obj.ValueKind == JsonValueKind.Object &&
            obj.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? "";
        }

        throw new InvalidOperationException(
            $"Unexpected `{command}` payload: missing string `{property}`. Payload: {SummarizeJson(obj)}");
    }

    private static bool TryExtractApiError(
        JsonElement content,
        out string? code,
        out string? message,
        out int? retryAfterSeconds)
    {
        code = null;
        message = null;
        retryAfterSeconds = null;

        if (content.ValueKind != JsonValueKind.Object ||
            !content.TryGetProperty("error", out var error) ||
            error.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        if (error.ValueKind == JsonValueKind.Object)
        {
            if (error.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.String)
                code = codeEl.GetString();
            if (error.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
                message = msgEl.GetString();
        }
        else if (error.ValueKind == JsonValueKind.String)
        {
            code = error.GetString();
        }

        if (content.TryGetProperty("message", out var topMsgEl) && topMsgEl.ValueKind == JsonValueKind.String)
            message ??= topMsgEl.GetString();

        if (content.TryGetProperty("retry_after", out var retryEl) &&
            retryEl.ValueKind == JsonValueKind.Number &&
            retryEl.TryGetInt32(out var retry))
        {
            retryAfterSeconds = retry;
        }

        message ??= "Unknown API error";
        return true;
    }

    private static string SummarizeJson(JsonElement payload)
    {
        string raw = payload.GetRawText();
        const int maxLength = 240;
        if (raw.Length <= maxLength)
            return raw;
        return raw.Substring(0, maxLength) + "...";
    }


    private void EnsureSession()
    {
        if (_sessionId == null)
            throw new InvalidOperationException("Session not created.");
    }

    private async Task EnsureSuccess(HttpResponseMessage response, string raw)
    {
        var content = JsonSerializer.Deserialize<JsonElement>(raw);

        if (content.TryGetProperty("error", out var error) &&
            error.ValueKind != JsonValueKind.Null)
        {
            var code = error.GetProperty("code").GetString();
            var message = error.GetProperty("message").GetString();
            throw new Exception($"API Error: {code} - {message}");
        }

        response.EnsureSuccessStatusCode();
        await Task.CompletedTask;
    }

}
