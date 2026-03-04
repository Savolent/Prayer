using System;

public interface IAgentUiStateBuilder
{
    (string SpaceStateMarkdown, string? TradeStateMarkdown, string? ShipyardStateMarkdown, string? CantinaStateMarkdown)
        BuildUiState(GameState state);
}

public sealed class AgentUiStateBuilder : IAgentUiStateBuilder
{
    public (string SpaceStateMarkdown, string? TradeStateMarkdown, string? ShipyardStateMarkdown, string? CantinaStateMarkdown)
        BuildUiState(GameState state)
    {
        var spaceState = state.ToDisplayText();
        var tradeState = state.Docked
            ? BuildTradeUiState(state)
            : null;
        var shipyardState = state.Docked && state.CurrentPOI.IsStation
            ? BuildShipyardUiState(state)
            : null;
        var cantinaState = state.Docked && state.CurrentPOI.IsStation
            ? BuildCantinaUiState(state)
            : null;

        return (spaceState, tradeState, shipyardState, cantinaState);
    }

    private static string BuildTradeUiState(GameState state)
    {
        var prices = state.BuildEstimatedItemPrices();
        var cargo = GameState.StripMarkdown(GameState.FormatCargo(state.Cargo, prices));
        var storage = state.Shared.StorageItems != null && state.Shared.StorageItems.Count > 0
            ? GameState.StripMarkdown(GameState.FormatCargo(state.Shared.StorageItems, prices))
            : "";
        var economy = GameState.StripMarkdown(
            GameState.FormatEconomy(state.Shared.EconomyDeals, state.Shared.OwnBuyOrders, state.Shared.OwnSellOrders));
        var storageSection = string.IsNullOrWhiteSpace(storage)
            ? ""
            : $"\nSTORAGE\n{storage}\n";

        return
$@"CONTEXT: TRADE TERMINAL
STATION: {state.CurrentPOI.Id}
CREDITS: {state.Credits}
STATION CREDITS: {state.Shared.StorageCredits}
FUEL: {state.Fuel}/{state.MaxFuel}
CARGO: {state.CargoUsed}/{state.CargoCapacity}

CARGO ITEMS
{cargo}
{storageSection}
ECONOMY
{economy}{state.BuildNotificationsDisplaySection()}";
    }

    private static string BuildShipyardUiState(GameState state)
    {
        var prices = state.BuildEstimatedItemPrices();
        var cargo = GameState.StripMarkdown(GameState.FormatCargo(state.Cargo, prices));
        var showroom = GameState.StripMarkdown(GameState.FormatShipyardShowroomLines(state.ShipyardShowroomLines));
        var listings = GameState.StripMarkdown(GameState.FormatShipyardShowroomLines(state.ShipyardListingLines));
        int currentPage = state.ShipCatalogue.Page ?? 1;
        int totalPages = state.ShipCatalogue.TotalPages ?? 1;
        int totalItems = state.ShipCatalogue.Total ?? state.ShipCatalogue.TotalItems ?? 0;
        int entriesOnPage = state.ShipCatalogue.NormalizedEntries.Length;
        string catalogEntries = GameState.StripMarkdown(
            GameState.FormatCatalogueEntries(state.ShipCatalogue.NormalizedEntries));

        return
$@"CONTEXT: SHIPYARD
STATION: {state.CurrentPOI.Id}
CREDITS: {state.Credits}
STATION CREDITS: {state.Shared.StorageCredits}
FUEL: {state.Fuel}/{state.MaxFuel}
CARGO: {state.CargoUsed}/{state.CargoCapacity}

SHOWROOM
{showroom}

PLAYER LISTINGS
{listings}

CARGO ITEMS
{cargo}{state.BuildNotificationsDisplaySection()}

CATALOG CACHE
PAGE: {currentPage}/{totalPages}
ENTRIES ON PAGE: {entriesOnPage}
TOTAL SHIPS: {totalItems}

SHIPS
{catalogEntries}";
    }

    private static string BuildCantinaUiState(GameState state)
    {
        string activeMissions = GameState.StripMarkdown(GameState.FormatMissions(state.ActiveMissions));
        string availableMissions = GameState.StripMarkdown(GameState.FormatMissions(state.AvailableMissions));

        if (string.IsNullOrWhiteSpace(activeMissions))
            activeMissions = "- _(none)_";
        if (string.IsNullOrWhiteSpace(availableMissions))
            availableMissions = "- _(none)_";

        return
$@"CONTEXT: CANTINA
STATION: {state.CurrentPOI.Id}
CREDITS: {state.Credits}

ACTIVE MISSIONS
{activeMissions}

AVAILABLE MISSIONS
{availableMissions}{state.BuildNotificationsDisplaySection()}";
    }
}
