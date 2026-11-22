using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using WhaleGenie.Core.Devices;
using WhaleGenie.Editing;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Views;

/// <summary>
/// Puts a desktop overlay up for one action field and reports what it took. This is the only place
/// the picker commands know a window type; the session above it works with plain requests.
/// </summary>
internal sealed class EditorPickerHost(Func<Window?> owner, IDeviceLayer devices)
    : IEditorPickerHost, IEditorLookHost
{
    public async Task<EditorPickResult> PickAsync(EditorPickRequest request)
    {
        if (owner() is not { } window)
        {
            return EditorPickResult.Cancelled;
        }

        switch (request.Kind)
        {
            case EditorPickKind.Point:
                {
                    WindowRule? target = WindowRule.TryParse(request.WindowRuleJson, out var rule) ? rule : null;
                    var point = await PointPickerWindow.PickAsync(window, devices.Screen, devices.Windows,
                        devices.Ui, devices.Hook, target);
                    return point is { } at
                        ? Result(at.X, at.Y)
                        : EditorPickResult.Cancelled;
                }
            case EditorPickKind.Region:
            case EditorPickKind.Drag:
                {
                    RegionPickerWindow.Region? bounds = request.Width > 0 && request.Height > 0
                        ? new RegionPickerWindow.Region(request.X, request.Y, request.Width, request.Height)
                        : (RegionPickerWindow.Region?)null;
                    if (request.Kind == EditorPickKind.Drag)
                    {
                        var drag = await RegionPickerWindow.PickDragAsync(window, devices.Screen,
                            devices.Windows, bounds, devices.Hook);
                        return drag is { } span
                            ? Result(span.StartX, span.StartY, endX: span.EndX, endY: span.EndY)
                            : EditorPickResult.Cancelled;
                    }

                    var region = await RegionPickerWindow.PickAsync(window, devices.Screen,
                        devices.Windows, null, bounds, hook: devices.Hook);
                    return region is { } picked
                        ? Result(picked.X, picked.Y, picked.Width, picked.Height)
                        : EditorPickResult.Cancelled;
                }
            case EditorPickKind.WindowBounds:
                {
                    RegionPickerWindow.Region? initial = request.Width > 0 && request.Height > 0
                        ? new RegionPickerWindow.Region(request.X, request.Y, request.Width, request.Height)
                        : (RegionPickerWindow.Region?)null;
                    var picked = await RegionPickerWindow.PickAsync(window, devices.Screen,
                        devices.Windows, initial, null, request.Handle, devices.Hook);
                    return picked is { } region
                        ? Result(region.X, region.Y, region.Width, region.Height)
                        : EditorPickResult.Cancelled;
                }
            case EditorPickKind.Capture:
                {
                    RegionPickerWindow.Region? bounds = request.Width > 0 && request.Height > 0
                        ? new RegionPickerWindow.Region(request.X, request.Y, request.Width, request.Height)
                        : (RegionPickerWindow.Region?)null;
                    var path = await RegionPickerWindow.CaptureAsync(window, devices.Screen,
                        devices.Windows, request.AssetFolder, bounds, devices.Hook);
                    return path is { Length: > 0 }
                        ? Result(path: path)
                        : EditorPickResult.Cancelled;
                }
            case EditorPickKind.Color:
                {
                    var picked = await ColorPickerWindow.PickAsync(window, devices.Screen,
                        devices.Input, devices.Hook);
                    return picked is { } colour
                        ? Result(colour.At.X, colour.At.Y, colour: colour.Colour.ToHex())
                        : EditorPickResult.Cancelled;
                }
            case EditorPickKind.WindowTitle:
                {
                    var match = Enum.TryParse<WindowMatch>(request.WindowMatch, ignoreCase: true,
                        out var parsed) ? parsed : WindowMatch.Title;
                    var picked = await WindowPickerWindow.PickAsync(window, match, devices.Windows);
                    return picked is { Length: > 0 }
                        ? new EditorPickResult(true, 0, 0, 0, 0, 0, 0, string.Empty,
                            string.Empty, picked, string.Empty, string.Empty, [], [], string.Empty, false)
                        : EditorPickResult.Cancelled;
                }
            case EditorPickKind.Window:
                {
                    using var aside = AppWindows.StepAside(window);
                    await AppWindows.SettleAsync();
                    var picked = await WindowPickWindow.PickAsync(devices.Windows, devices.Screen, devices.Hook);
                    if (picked is null)
                    {
                        return EditorPickResult.Cancelled;
                    }

                    var process = string.Empty;
                    var className = string.Empty;
                    try
                    {
                        process = devices.Windows.ProcessOf(picked.Handle);
                        className = devices.Windows.ClassOf(picked.Handle);
                    }
                    catch (Exception)
                    {
                        // The title still names the window when the process or class cannot be read.
                    }

                    var rule = WindowRule.From(picked, process, className);
                    return new EditorPickResult(true, 0, 0, 0, 0, 0, 0, string.Empty, string.Empty,
                        string.Empty, picked.Title, rule.ToJson().ToJsonString(), [], [], string.Empty, false);
                }
            case EditorPickKind.Element:
                {
                    var picked = await ElementPickerWindow.PickAsync(window, devices.Screen,
                        devices.Hook, devices.Ui);
                    return picked is { } element
                        ? new EditorPickResult(true, 0, 0, 0, 0, 0, 0, string.Empty, string.Empty,
                            element.Selector, element.Window, string.Empty, [], [], string.Empty, false)
                        : EditorPickResult.Cancelled;
                }
            case EditorPickKind.Browser:
                {
                    var picked = await BrowserPicker.PickAsync(devices.Browser, request.Url, request.Profile);
                    if (picked.Selector.Length == 0)
                    {
                        return EditorPickResult.Cancelled;
                    }

                    var text = string.Empty;
                    if (request.WantsWords)
                    {
                        try
                        {
                            text = (await Task.Run(() => devices.Browser.Text(
                                PageTarget.Of(picked.Selector, 1), PagePart.Text, string.Empty, 3000))).Trim();
                        }
                        catch (Exception)
                        {
                            // A page that went away leaves the selector to be taken without its words.
                        }
                    }

                    return new EditorPickResult(true, 0, 0, 0, 0, 0, 0, string.Empty, string.Empty,
                        text.Length > 0 ? text : picked.Selector, string.Empty, string.Empty,
                        picked.Candidates, [], string.Empty, false);
                }
            case EditorPickKind.Path:
                return await PickPathAsync(window, request);
            default:
                return EditorPickResult.Cancelled;
        }
    }

    public async Task<EditorLookResult> PickLookAsync(EditorLookRequest request)
    {
        if (owner() is not { } window)
        {
            return EditorLookResult.Cancelled;
        }

        if (request.Mode == EditorLookMode.Offset)
        {
            var point = await LookPickWindow.PickOffsetAsync(window, request.Look,
                request.Area, request.Origin, request.Centre, request.Variables);
            return point is { } at
                ? new EditorLookResult(true, at.X, at.Y, [])
                : EditorLookResult.Cancelled;
        }

        var entries = await LookPickWindow.PickColoursAsync(window, request.Look,
            request.Area, request.Origin, request.Centre, request.Variables, request.Entries);
        return entries is { Count: > 0 }
            ? new EditorLookResult(true, 0, 0, entries)
            : EditorLookResult.Cancelled;
    }

    private static async Task<EditorPickResult> PickPathAsync(Window owner, EditorPickRequest request)
    {
        var storage = owner.StorageProvider;
        var filters = Filter(request.PathFilter, request.PathLabel);
        var intent = Enum.TryParse<PathIntent>(request.PathIntent, ignoreCase: true, out var parsed)
            ? parsed
            : PathIntent.Read;
        switch (intent)
        {
            case PathIntent.Write:
                {
                    var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = Strings.Format("Add.BrowseSaveTitle", request.Name),
                        SuggestedFileName = string.Empty,
                        FileTypeChoices = filters,
                    });
                    var path = file?.TryGetLocalPath();
                    return path is { Length: > 0 } ? Result(path: path) : EditorPickResult.Cancelled;
                }
            case PathIntent.Folder:
                {
                    var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
                    {
                        Title = Strings.Get("Add.BrowseFolderTitle"),
                        AllowMultiple = false,
                    });
                    var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
                    return path is { Length: > 0 } ? Result(path: path) : EditorPickResult.Cancelled;
                }
            case PathIntent.Files:
                {
                    var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = Strings.Format("Add.BrowseOpenTitle", request.Name),
                        AllowMultiple = true,
                        FileTypeFilter = filters,
                    });
                    var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>()
                        .Where(path => path.Length > 0).ToList();
                    return paths.Count > 0
                        ? Result(path: string.Join(Environment.NewLine, paths))
                        : EditorPickResult.Cancelled;
                }
            default:
                {
                    var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = Strings.Format("Add.BrowseOpenTitle", request.Name),
                        AllowMultiple = false,
                        FileTypeFilter = filters,
                    });
                    var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
                    return path is { Length: > 0 } ? Result(path: path) : EditorPickResult.Cancelled;
                }
        }
    }

    private static IReadOnlyList<FilePickerFileType> Filter(string patterns, string label)
    {
        var wanted = patterns.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return wanted.Length == 0
            ? []
            : [new FilePickerFileType(label.Length > 0 ? label : string.Join(" / ", wanted))
            {
                Patterns = [.. wanted],
            }];
    }

    private static EditorPickResult Result(int x = 0, int y = 0, int width = 0, int height = 0,
        int endX = 0, int endY = 0, string colour = "", string path = "")
        => new(true, x, y, width, height, endX, endY, colour, path, string.Empty, string.Empty,
            string.Empty, [], [], string.Empty, false);
}
