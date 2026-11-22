namespace WhaleGenie.ViewModels;

/// <summary>The settings model's icon-name check over the Lucide catalogue.</summary>
internal sealed class MenuIconCatalogAdapter : IIconCatalog
{
    public bool Knows(string? key) => MenuIconCatalog.Knows(key);
}
