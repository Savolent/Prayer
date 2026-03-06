using System;
using System.Net;
using System.Text;

internal static class TradeTabRenderer
{
    public static string Build(TradeUiModel? model)
    {
        if (model == null)
            return "<section class='space-page'><div class='small'>(trade unavailable)</div></section>";

        var sb = new StringBuilder();
        sb.AppendLine("<section class='space-page'>");
        sb.AppendLine("<div class='space-header'>");
        sb.Append("<h4 class='space-title'>Trade Terminal • ").Append(E(model.StationId)).AppendLine("</h4>");
        sb.Append("<div class='space-subtitle'>Cargo ")
            .Append(model.CargoItems.Count)
            .Append(" • Storage ")
            .Append(model.StorageItems.Count)
            .Append(" • Orders ")
            .Append(model.BuyOrders.Count + model.SellOrders.Count)
            .AppendLine("</div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class='space-stats trade-stats'>");
        AppendStatCard(sb, "Credits", model.Credits.ToString());
        AppendStatCard(sb, "Station Credits", model.StationCredits.ToString());
        AppendStatCard(sb, "Fuel", model.Fuel);
        AppendStatCard(sb, "Cargo", model.Cargo);
        sb.AppendLine("</div>");

        sb.AppendLine("<div class='space-grid'>");
        AppendInventoryPanel(sb, "Cargo", model.CargoItems, "sell", "Sell", "stash", "Stash");
        AppendInventoryPanel(sb, "Storage", model.StorageItems, "retrieve", "Retrieve", null, null);
        sb.AppendLine("</div>");

        sb.AppendLine("<section class='space-panel'>");
        sb.AppendLine("<div class='space-panel-title'>Open Orders</div>");
        if (model.BuyOrders.Count == 0 && model.SellOrders.Count == 0)
        {
            sb.AppendLine("<div class='small'>(none)</div>");
        }
        else
        {
            AppendOrderList(sb, "Buy Orders", model.BuyOrders, "cancel_buy", "Cancel Buy", true);
            AppendOrderList(sb, "Sell Orders", model.SellOrders, "cancel_sell", "Cancel Sell", false);
        }
        sb.AppendLine("</section>");
        sb.AppendLine("</section>");
        return sb.ToString();
    }

    private static void AppendOrderList(
        StringBuilder sb,
        string title,
        System.Collections.Generic.IReadOnlyList<TradeUiOrder> orders,
        string cancelCommand,
        string cancelLabel,
        bool open)
    {
        sb.Append("<details class='catalog-group'");
        if (open)
            sb.Append(" open");
        sb.Append("><summary>")
            .Append(E(title))
            .Append(" (")
            .Append(orders.Count)
            .AppendLine(")</summary>");

        if (orders.Count == 0)
        {
            sb.AppendLine("<div class='small'>(none)</div>");
            sb.AppendLine("</details>");
            return;
        }

        foreach (var order in orders)
        {
            var priceText = $"{order.PriceEach:0.##}cr";
            var quantityText = $"x{order.Quantity}";
            sb.Append("<div class='mission-item trade-order-card'><div class='mission-title'>")
                .Append(E(order.ItemId))
                .AppendLine("</div>");
            sb.Append("<div class='mission-body trade-order-meta'>Limit <span class='trade-order-price'>")
                .Append(E(priceText))
                .Append("</span> <span class='trade-order-qty'>")
                .Append(E(quantityText))
                .AppendLine("</span></div>");

            if (!string.IsNullOrWhiteSpace(order.ItemId))
            {
                sb.AppendLine("<div class='mission-actions'>");
                AppendScriptChip(sb, $"{cancelCommand} {order.ItemId};", cancelLabel);
                sb.AppendLine("</div>");
            }

            sb.AppendLine("</div>");
        }

        sb.AppendLine("</details>");
    }

    private static void AppendInventoryPanel(
        StringBuilder sb,
        string title,
        System.Collections.Generic.IReadOnlyList<TradeUiItem> items,
        string primaryCommand,
        string primaryLabel,
        string? secondaryCommand,
        string? secondaryLabel)
    {
        sb.AppendLine("<section class='space-panel'>");
        sb.Append("<div class='space-panel-title'>").Append(E(title)).AppendLine("</div>");
        if (items.Count == 0)
        {
            sb.AppendLine("<div class='small'>(empty)</div>");
        }
        else
        {
            sb.AppendLine("<div class='cargo-list'>");
            foreach (var item in items)
            {
                sb.AppendLine("<div class='cargo-row'>");
                sb.Append("<div class='cargo-item-main'><div class='cargo-label'>")
                    .Append(E($"{item.ItemId} x{item.Quantity}"))
                    .AppendLine("</div>");
                if (item.MedianBuyPrice.HasValue || item.MedianSellPrice.HasValue)
                {
                    sb.Append("<div class='cargo-meta'>Median ");
                    if (item.MedianBuyPrice.HasValue)
                    {
                        sb.Append("bid <span class='trade-order-price'>")
                            .Append(E($"{item.MedianBuyPrice.Value:0.##}cr"))
                            .Append("</span>");
                    }
                    if (item.MedianBuyPrice.HasValue && item.MedianSellPrice.HasValue)
                        sb.Append(" | ");
                    if (item.MedianSellPrice.HasValue)
                    {
                        sb.Append("ask <span class='trade-order-price'>")
                            .Append(E($"{item.MedianSellPrice.Value:0.##}cr"))
                            .Append("</span>");
                    }
                    sb.AppendLine("</div>");
                }
                sb.AppendLine("</div>");
                if (!string.IsNullOrWhiteSpace(item.ItemId))
                {
                    sb.AppendLine("<div class='cargo-actions'>");
                    AppendScriptChip(sb, $"{primaryCommand} {item.ItemId};", primaryLabel);
                    if (!string.IsNullOrWhiteSpace(secondaryCommand) && !string.IsNullOrWhiteSpace(secondaryLabel))
                        AppendScriptChip(sb, $"{secondaryCommand} {item.ItemId};", secondaryLabel);
                    sb.AppendLine("</div>");
                }
                sb.AppendLine("</div>");
            }
            sb.AppendLine("</div>");
        }
        sb.AppendLine("</section>");
    }

    private static void AppendStatCard(StringBuilder sb, string label, string value)
    {
        sb.Append("<div class='space-stat'><div class='space-stat-label'>")
            .Append(E(label))
            .Append("</div><div class='space-stat-value'>")
            .Append(E(value))
            .AppendLine("</div></div>");
    }

    private static void AppendScriptChip(StringBuilder sb, string script, string label)
    {
        sb.Append("<form class='space-chip-form' hx-post='api/control-input' hx-swap='none' hx-on::after-request='window.executeIfOk(event)'>")
            .Append("<input type='hidden' name='script' value='").Append(E(script)).Append("'>")
            .Append("<button type='submit' class='space-chip'>")
            .Append(E(label))
            .AppendLine("</button></form>");
    }

    private static string E(string value) => WebUtility.HtmlEncode(value ?? "");
}
