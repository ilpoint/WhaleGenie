using System.IO;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Models;

/// <summary>
/// One project entry that points at a macro: who it belongs to, whether it is armed, and what it
/// watches. The settings live in <see cref="Spec"/> rather than on this type, so a key entry cannot
/// carry a schedule and a reader that switched on the kind gets every field it may rely on.
/// </summary>
/// <remarks>
/// Trigger settings live here rather than on the macro body, so the same macro can be reached by
/// several inputs without copying its steps. The name in <see cref="Id"/> is what the runtime
/// remembers an entry by; it survives editing and travelling into another project.
/// </remarks>
public sealed partial class TriggerEntry : ObservableObject
{
    /// <summary>The name this entry keeps across edits, saves and imports.</summary>
    public string Id { get; init; } = TriggerIds.New();

    [ObservableProperty]
    public partial string MacroName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Kind))]
    [NotifyPropertyChangedFor(nameof(KindLabel))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(IsRemovable))]
    public partial TriggerSpec Spec { get; set; } = new KeySpec();

    public TriggerKind Kind => Spec.Kind;

    public string KindLabel => TriggerKinds.Text(Kind);

    /// <summary>One line for the editor's list and the macro card.</summary>
    public string Summary => Spec.Describe();

    /// <summary>Menu entries are managed on the project's menu pages rather than removed here.</summary>
    public bool IsRemovable => !TriggerKinds.IsPlacement(Kind);

    /// <summary>
    /// Puts this entry in a menu cell. The cell is a field of the menu settings, so it moves with
    /// them; a caller that is not looking at a menu entry is asking the wrong entry.
    /// </summary>
    public void MoveMenuCell(string pageId, int row, int column)
    {
        if (Spec is MenuSpec menu)
        {
            Spec = menu with { PageId = pageId, Row = row, Column = column };
        }
    }

    /// <summary>Replaces the icon shown in this entry's menu cell.</summary>
    public void SetMenuIcon(MenuIconReference? icon)
    {
        if (Spec is MenuSpec menu)
        {
            Spec = menu with { Icon = icon };
        }
    }

    /// <summary>A copy under the same name, with the same settings.</summary>
    public TriggerEntry Copy() => new()
    {
        Id = Id,
        MacroName = MacroName,
        IsEnabled = IsEnabled,
        Spec = Spec,
    };

    public JsonObject ToJson()
    {
        var node = TriggerSpecJson.ToJson(Spec);
        node[FieldNames.Id] = Id;
        node[FieldNames.Macro] = MacroName;
        node[FieldNames.Enabled] = IsEnabled;
        return node;
    }

    public static TriggerEntry FromJson(JsonObject node)
    {
        var id = node[FieldNames.Id] is JsonValue name && name.TryGetValue<string>(out var text)
                 && text.Trim().Length > 0
            ? text.Trim()
            : throw new InvalidDataException("A trigger entry has no \"id\".");

        return new TriggerEntry
        {
            Id = id,
            MacroName = node[FieldNames.Macro] is JsonValue owner
                        && owner.TryGetValue<string>(out var macroName)
                ? macroName
                : throw new InvalidDataException($"Trigger \"{id}\" has no \"macro\"."),
            IsEnabled = node[FieldNames.Enabled] is JsonValue armed && armed.TryGetValue<bool>(out var on)
                ? on
                : throw new InvalidDataException($"Trigger \"{id}\" has no \"enabled\"."),
            Spec = TriggerSpecJson.FromJson(node, id),
        };
    }

    public override string ToString() => $"{KindLabel}: {Summary}";
}
