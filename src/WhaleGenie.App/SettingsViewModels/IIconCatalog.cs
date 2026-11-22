namespace WhaleGenie.ViewModels;

/// <summary>Whether a saved menu icon name exists in this build; the UI owns the icon library.</summary>
public interface IIconCatalog
{
    bool Knows(string? key);
}
