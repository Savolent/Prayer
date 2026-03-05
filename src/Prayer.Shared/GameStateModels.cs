using System;
using System.Collections.Generic;

public class GameState
{
    public string System { get; set; } = "";
    public POIInfo CurrentPOI { get; set; } = new();

    public POIInfo[] POIs { get; set; } = Array.Empty<POIInfo>();
    public string[] Systems { get; set; } = Array.Empty<string>();
    public GalaxyState Galaxy { get; set; } = new();
    public int StorageCredits { get; set; }
    public Dictionary<string, ItemStack> StorageItems { get; set; } = new();
    public EconomyDeal[] EconomyDeals { get; set; } = Array.Empty<EconomyDeal>();
    public OpenOrderInfo[] OwnBuyOrders { get; set; } = Array.Empty<OpenOrderInfo>();
    public OpenOrderInfo[] OwnSellOrders { get; set; } = Array.Empty<OpenOrderInfo>();
    public Dictionary<string, ItemStack> Cargo { get; set; } = new();

    public string ShipName { get; set; } = "";
    public string ShipClassId { get; set; } = "";
    public int Armor { get; set; }
    public int Speed { get; set; }
    public int CpuUsed { get; set; }
    public int CpuCapacity { get; set; }
    public int PowerUsed { get; set; }
    public int PowerCapacity { get; set; }
    public int ModuleCount { get; set; }

    public int Fuel { get; set; }
    public int MaxFuel { get; set; }
    public int Credits { get; set; }
    public bool Docked { get; set; }
    public string[] ShipyardShowroomLines { get; set; } = Array.Empty<string>();
    public string[] ShipyardListingLines { get; set; } = Array.Empty<string>();
    public Catalogue ShipCatalogue { get; set; } = new();
    public OwnedShipInfo[] OwnedShips { get; set; } = Array.Empty<OwnedShipInfo>();

    public int Hull { get; set; }
    public int MaxHull { get; set; }
    public int Shield { get; set; }
    public int MaxShield { get; set; }

    public MissionInfo[] ActiveMissions { get; set; } = Array.Empty<MissionInfo>();
    public MissionInfo[] AvailableMissions { get; set; } = Array.Empty<MissionInfo>();

    public int CargoUsed { get; set; }
    public int CargoCapacity { get; set; }
    public GameNotification[] Notifications { get; set; } = Array.Empty<GameNotification>();
    public GameChatMessage[] ChatMessages { get; set; } = Array.Empty<GameChatMessage>();
}

public class GalaxyState
{
    public GalaxyMapSnapshot Map { get; set; } = new();
    public GalaxyMarket Market { get; set; } = new();
    public GalaxyCatalog Catalog { get; set; } = new();
    public GalaxyResources Resources { get; set; } = new();
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class GalaxyResources
{
    public Dictionary<string, string[]> SystemsByResource { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string[]> PoisByResource { get; set; } = new(StringComparer.Ordinal);
}

public class GalaxyMarket
{
    public Dictionary<string, MarketState> MarketsByStation { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, decimal> GlobalMedianBuyPrices { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, decimal> GlobalMedianSellPrices { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, decimal> GlobalWeightedMidPrices { get; set; } = new(StringComparer.Ordinal);
}

public class GalaxyCatalog
{
    public Dictionary<string, CatalogueEntry> ItemsById { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, CatalogueEntry> ShipsById { get; set; } = new(StringComparer.Ordinal);
}

public class GalaxyMapSnapshot
{
    public List<GalaxySystemInfo> Systems { get; set; } = new();
    public List<GalaxyKnownPoiInfo> KnownPois { get; set; } = new();
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class GalaxySystemInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";
    public List<string> Connections { get; set; } = new();
    public List<GalaxyPoiInfo> Pois { get; set; } = new();
}

public class GalaxyPoiInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
}

public class GalaxyKnownPoiInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string SystemId { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class MarketState
{
    public string StationId { get; set; } = "";
    public string BaseName { get; set; } = "";
    public DateTime LastUpdateUtc { get; set; }
    public Dictionary<string, List<MarketOrder>> BuyOrders { get; set; }
        = new(StringComparer.Ordinal);
    public Dictionary<string, List<MarketOrder>> SellOrders { get; set; }
        = new(StringComparer.Ordinal);
}

public class MarketOrder
{
    public decimal PriceEach { get; set; }
    public int Quantity { get; set; }
}

public class EconomyDeal
{
    public string ItemId { get; set; } = "";
    public string BuyStationId { get; set; } = "";
    public decimal BuyPrice { get; set; }
    public string SellStationId { get; set; } = "";
    public decimal SellPrice { get; set; }
    public decimal ProfitPerUnit { get; set; }
}

public class OpenOrderInfo
{
    public string OrderId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public decimal PriceEach { get; set; }
    public int Quantity { get; set; }
}

public class OwnedShipInfo
{
    public string ShipId { get; set; } = "";
    public string ClassId { get; set; } = "";
    public string Location { get; set; } = "";
    public bool IsActive { get; set; }
}

public class ItemStack
{
    public string ItemId { get; set; } = "";
    public int Quantity { get; set; }
}

public class POIInfo
{
    public string Id { get; set; } = "";
    public string SystemId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Hidden { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public bool HasBase { get; set; }
    public string? BaseId { get; set; }
    public string? BaseName { get; set; }
    public int Online { get; set; }
    public PoiResourceInfo[] Resources { get; set; } = Array.Empty<PoiResourceInfo>();
}

public class PoiResourceInfo
{
    public string ResourceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string RichnessText { get; set; } = "";
    public int? Richness { get; set; }
    public int? Remaining { get; set; }
    public string RemainingDisplay { get; set; } = "";
}

public class MissionInfo
{
    public string Id { get; set; } = "";
    public string MissionId { get; set; } = "";
    public string TemplateId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public string ProgressText { get; set; } = "";
    public bool Completed { get; set; }
    public int? Difficulty { get; set; }
    public int? ExpiresInTicks { get; set; }
    public string AcceptedAt { get; set; } = "";
    public string IssuingBase { get; set; } = "";
    public string IssuingBaseId { get; set; } = "";
    public string GiverName { get; set; } = "";
    public string GiverTitle { get; set; } = "";
    public bool? Repeatable { get; set; }
    public string FactionId { get; set; } = "";
    public string FactionName { get; set; } = "";
    public string ChainNext { get; set; } = "";
    public string ObjectivesSummary { get; set; } = "";
    public string ProgressSummary { get; set; } = "";
    public string RequirementsSummary { get; set; } = "";
    public string RewardsSummary { get; set; } = "";
}

public class GameNotification
{
    public string Type { get; set; } = "";
    public string Summary { get; set; } = "";
    public string PayloadJson { get; set; } = "";
}

public class GameChatMessage
{
    public string MessageId { get; set; } = "";
    public string Channel { get; set; } = "";
    public string Sender { get; set; } = "";
    public string Content { get; set; } = "";
    public int SeenTick { get; set; }
}
