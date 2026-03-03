public partial class SpaceMoltHttpClient
{
    public GameContextKind CurrentMode => _mode;

    public void SetMode(GameContextKind mode)
    {
        _mode = mode;
        if (mode != GameContextKind.ShipCatalog)
            _shipCatalogPage = 1;
    }

    public bool IsTradeTerminalMode => _mode == GameContextKind.Trade;

    public void SetTradeTerminalMode(bool enabled)
    {
        SetMode(enabled ? GameContextKind.Trade : GameContextKind.Space);
    }

    public bool IsHangarTerminalMode => _mode == GameContextKind.Hangar;

    public void SetHangarTerminalMode(bool enabled)
    {
        SetMode(enabled ? GameContextKind.Hangar : GameContextKind.Space);
    }

    public bool IsShipyardTerminalMode => _mode == GameContextKind.Shipyard;

    public void SetShipyardTerminalMode(bool enabled)
    {
        SetMode(enabled ? GameContextKind.Shipyard : GameContextKind.Space);
    }

    public int ShipCatalogPage => _shipCatalogPage;

    public void EnterShipCatalogMode()
    {
        _shipCatalogPage = 1;
        _mode = GameContextKind.ShipCatalog;
    }

    public bool MoveShipCatalogToNextPage(int? totalPages)
    {
        if (totalPages.HasValue && totalPages.Value > 0 && _shipCatalogPage >= totalPages.Value)
            return false;

        _shipCatalogPage++;
        return true;
    }

    public bool MoveShipCatalogToLastPage()
    {
        if (_shipCatalogPage <= 1)
            return false;

        _shipCatalogPage--;
        return true;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
