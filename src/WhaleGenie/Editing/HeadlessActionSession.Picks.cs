using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Editing;

// Preparing a desktop pick from what the field already says, and writing its answer back into the
// same StepParameterViewModel the form draws. No window type is named here.
internal sealed partial class HeadlessActionSession
{
    public EditorPickRequest? PreparePick(string name, int index, string mode)
    {
        var field = Field(name);
        var rule = _form.NeedsWindowCoordinates ? _form.CoordinateWindow() : null;
        switch (mode)
        {
            case "point":
                if (_form.NeedsWindowCoordinates && rule is null)
                {
                    return null;
                }

                return Request(EditorPickKind.Point, name, index, mode, field,
                    windowRule: rule?.ToJson().ToJsonString() ?? string.Empty);
            case "region":
            case "drag":
                var area = _form.NeedsWindowCoordinates ? _form.CoordinateWindowBounds() : null;
                if (_form.NeedsWindowCoordinates && area is null)
                {
                    return null;
                }

                return Request(mode == "drag" ? EditorPickKind.Drag : EditorPickKind.Region,
                    name, index, mode, field, bounds: area);
            case "windowTitle":
                return Request(EditorPickKind.WindowTitle, name, index, mode, field,
                    windowMatch: _form.WindowMatchFor(field).ToString());
            case "windowBounds":
                {
                    if (_form.CurrentWindow() is not { } window)
                    {
                        return null;
                    }

                    return Request(EditorPickKind.WindowBounds, name, index, mode, field,
                        bounds: (window.Location.X, window.Location.Y, window.Size.Width, window.Size.Height),
                        handle: window.Handle);
                }
            case "capture":
                {
                    var captureArea = _form.NeedsWindowCoordinates ? _form.CoordinateWindowBounds() : null;
                    if (_form.NeedsWindowCoordinates && captureArea is null)
                    {
                        return null;
                    }

                    return Request(EditorPickKind.Capture, name, index, mode, field, bounds: captureArea,
                        assetFolder: _form.AssetFolder);
                }
            case "color":
                return Request(EditorPickKind.Color, name, index, mode, field);
            case "element":
                return Request(EditorPickKind.Element, name, index, mode, field);
            case "browser":
            case "narrow":
                {
                    var url = ParameterText("url");
                    var profile = ParameterText("profile");
                    var wantsWords = false;
                    if (mode == "narrow" && index >= 0 && index < field.NarrowRows.Count)
                    {
                        wantsWords = field.NarrowRows[index].Way.WantsWords;
                    }

                    return Request(EditorPickKind.Browser, name, index, mode, field,
                        url: url, profile: profile, wantsWords: wantsWords);
                }
            case "path":
                return Request(EditorPickKind.Path, name, index, mode, field,
                    pathIntent: field.Definition.PathIntent.ToString(),
                    pathFilter: field.Definition.PathFilter,
                    pathLabel: field.Definition.LocalPathFilterLabel,
                    multiple: field.Definition.PathIntent is PathIntent.Files);
            default:
                throw new InvalidOperationException("unknown picker " + mode);
        }
    }

    public EditorActionFormSnapshot ApplyPick(EditorPickRequest request, EditorPickResult result)
    {
        if (!result.Chosen)
        {
            return Snapshot();
        }

        var field = Field(request.Name);
        switch (request.Kind)
        {
            case EditorPickKind.Point:
                _form.ApplyCursorPosition(result.X, result.Y);
                break;
            case EditorPickKind.Region:
                if (field.IsRegion && request.Index >= 0 && request.Index < field.Regions.Count)
                {
                    field.Regions[request.Index].Put(result.X, result.Y, result.Width, result.Height);
                }
                else if (!_form.ApplyRegion(result.X, result.Y, result.Width, result.Height))
                {
                    field.WriteValue(Formula.Nothing);
                }

                break;
            case EditorPickKind.WindowBounds:
                if (!_form.ApplyRegion(result.X, result.Y, result.Width, result.Height))
                {
                    field.WriteValue(Formula.Nothing);
                }

                break;
            case EditorPickKind.Drag:
                _form.ApplyDrag(result.X, result.Y, result.EndX, result.EndY);
                break;
            case EditorPickKind.Capture:
                WritePicture(field, request.Index, result.Path);
                break;
            case EditorPickKind.Color:
                if (PixelColor.TryParse(result.Colour, out var colour))
                {
                    field.TakeColour(colour);
                    _form.ApplyCursorPosition(result.X, result.Y);
                }

                break;
            case EditorPickKind.WindowTitle:
                field.WriteValue(Formula.Text(result.Selector));
                break;
            case EditorPickKind.Window:
                if (WindowRule.TryParse(result.WindowRuleJson, out var windowRule))
                {
                    field.ApplyWindowRule(windowRule);
                }

                break;
            case EditorPickKind.Element:
                field.WriteValue(Formula.Text(result.Selector));
                if (result.WindowTitle.Length > 0)
                {
                    _form.FillWindowFromPick("window", result.WindowTitle);
                }

                break;
            case EditorPickKind.Browser:
                if (string.Equals(request.Mode, "narrow", StringComparison.Ordinal)
                    && field.IsNarrow && request.Index >= 0
                    && request.Index < field.NarrowRows.Count)
                {
                    field.NarrowRows[request.Index].Value = result.Selector;
                }
                else
                {
                    field.WriteValue(Formula.Text(result.Selector));
                }

                if (result.Candidates.Count > 0)
                {
                    field.OfferCandidates(result.Candidates);
                }

                break;
            case EditorPickKind.Path:
                WritePicture(field, request.Index, result.Path);
                break;
        }

        return Snapshot();
    }

    private static void WritePicture(StepParameterViewModel field, int index, string path)
    {
        if (!field.IsImageList)
        {
            field.WriteValue(Formula.Text(path));
            return;
        }

        if (index < 0 || index >= field.Pictures.Count)
        {
            field.AddPicture();
            index = field.Pictures.Count - 1;
        }

        field.Pictures[index].Text = path;
    }

    private EditorPickRequest Request(EditorPickKind kind, string name, int index, string mode,
        StepParameterViewModel field, (int X, int Y, int Width, int Height)? bounds = null,
        long handle = 0, string windowRule = "", string windowMatch = "", string assetFolder = "", string url = "",
        string profile = "", string pathIntent = "", string pathFilter = "", string pathLabel = "",
        bool multiple = false, bool wantsWords = false)
        => new(kind, name, index, mode,
            bounds?.X ?? 0, bounds?.Y ?? 0, bounds?.Width ?? 0, bounds?.Height ?? 0,
            handle, windowRule, windowMatch, assetFolder, url, profile, pathIntent, pathFilter,
            pathLabel, multiple, wantsWords, []);

    private string ParameterText(string name)
        => _form.Parameters.FirstOrDefault(parameter =>
               string.Equals(parameter.Definition.Name, name, StringComparison.Ordinal))
           ?.CurrentText ?? string.Empty;
}
