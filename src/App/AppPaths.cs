using System.IO;

public static class AppPaths
{
    public const string LogDir = "log";
    public const string CacheDir = "cache";
    public const string MarketsDir = "markets";
    public const string ShipyardsDir = "shipyards";
    public const string CatalogsDir = "catalogs";

    public static readonly string LlmLogFile = Path.Combine(LogDir, "llm.log");
    public static readonly string PlannerPromptLogFile = Path.Combine(LogDir, "planner_prompts.log");
    public static readonly string OpenAiErrorsLogFile = Path.Combine(LogDir, "openai_errors.log");
    public static readonly string HttpBadRequestLogFile = Path.Combine(LogDir, "http_badrequest.log");
    public static readonly string PathfindLogFile = Path.Combine(LogDir, "pathfind.log");
    public static readonly string SpaceMoltApiLogFile = Path.Combine(LogDir, "spacemolt_api.log");
    public static readonly string AuthFlowLogFile = Path.Combine(LogDir, "auth_flow.log");
    public static readonly string AnalyzeMarketLogFile = Path.Combine(LogDir, "analyze_market.log");
    public static readonly string ItemCatalogLogFile = Path.Combine(LogDir, "item_catalog.log");
    public static readonly string CommandExecutionLogFile = Path.Combine(LogDir, "command_execution.log");
    public static readonly string ScriptGenerationExamplesFile = Path.Combine(CacheDir, "script_generation_examples.json");
    public static readonly string SavedBotsFile = Path.Combine(CacheDir, "saved_bots.json");

    public static readonly string GalaxyMapCacheFile = Path.Combine(CacheDir, "galaxy_map_cache.json");
    public static readonly string RawMapCacheFile = Path.Combine(CacheDir, "map.cache");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(LogDir);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(MarketsDir);
        Directory.CreateDirectory(ShipyardsDir);
        Directory.CreateDirectory(CatalogsDir);
    }
}
