using System;
using System.Collections.Generic;

public class SharedGameState
{
    public int StorageCredits { get; set; }
    public Dictionary<string, ItemStack> StorageItems { get; set; } = new();
    public MarketState? Market { get; set; }
    public EconomyDeal[] EconomyDeals { get; set; } = Array.Empty<EconomyDeal>();
    public OpenOrderInfo[] OwnBuyOrders { get; set; } = Array.Empty<OpenOrderInfo>();
    public OpenOrderInfo[] OwnSellOrders { get; set; } = Array.Empty<OpenOrderInfo>();
    public Dictionary<string, decimal> GlobalMedianBuyPrices { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, decimal> GlobalMedianSellPrices { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, decimal> GlobalWeightedMidPrices { get; set; } = new(StringComparer.Ordinal);
}
