using System;
using System.Text.Json;
using System.Text.Json.Serialization;

public class Catalogue
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("page")]
    public int? Page { get; set; }

    [JsonPropertyName("page_size")]
    public int? PageSize { get; set; }

    [JsonPropertyName("total_pages")]
    public int? TotalPages { get; set; }

    [JsonPropertyName("total_items")]
    public int? TotalItems { get; set; }

    [JsonPropertyName("total")]
    public int? Total { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("items")]
    public CatalogueEntry[] Items { get; set; } = Array.Empty<CatalogueEntry>();

    [JsonPropertyName("entries")]
    public CatalogueEntry[] Entries { get; set; } = Array.Empty<CatalogueEntry>();

    [JsonPropertyName("ships")]
    public CatalogueEntry[] Ships { get; set; } = Array.Empty<CatalogueEntry>();

    public CatalogueEntry[] NormalizedEntries
    {
        get
        {
            if (Items.Length > 0)
                return Items;
            if (Entries.Length > 0)
                return Entries;
            if (Ships.Length > 0)
                return Ships;
            return Array.Empty<CatalogueEntry>();
        }
    }

    public static Catalogue FromJson(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object)
            return new Catalogue();

        return JsonSerializer.Deserialize<Catalogue>(result.GetRawText())
               ?? new Catalogue();
    }
}

public class CatalogueEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("class_id")]
    public string ClassId { get; set; } = "";

    [JsonPropertyName("class")]
    public string Class { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("tier")]
    public int? Tier { get; set; }

    [JsonPropertyName("scale")]
    public int? Scale { get; set; }

    [JsonPropertyName("hull")]
    public int? Hull { get; set; }

    [JsonPropertyName("base_hull")]
    public int? BaseHull { get; set; }

    [JsonPropertyName("shield")]
    public int? Shield { get; set; }

    [JsonPropertyName("base_shield")]
    public int? BaseShield { get; set; }

    [JsonPropertyName("cargo")]
    public int? Cargo { get; set; }

    [JsonPropertyName("cargo_capacity")]
    public int? CargoCapacity { get; set; }

    [JsonPropertyName("speed")]
    public int? Speed { get; set; }

    [JsonPropertyName("base_speed")]
    public int? BaseSpeed { get; set; }

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("materials")]
    public Dictionary<string, int>? MaterialsById { get; set; }

    [JsonPropertyName("ingredients")]
    public RecipeIngredientEntry[] Ingredients { get; set; } = Array.Empty<RecipeIngredientEntry>();

    [JsonPropertyName("inputs")]
    public RecipeIngredientEntry[] Inputs { get; set; } = Array.Empty<RecipeIngredientEntry>();
}

public sealed class RecipeIngredientEntry
{
    [JsonPropertyName("item_id")]
    public string ItemId { get; set; } = "";

    [JsonPropertyName("item")]
    public string Item { get; set; } = "";

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("quantity")]
    public int? Quantity { get; set; }

    [JsonPropertyName("amount")]
    public int? Amount { get; set; }

    [JsonPropertyName("count")]
    public int? Count { get; set; }
}
