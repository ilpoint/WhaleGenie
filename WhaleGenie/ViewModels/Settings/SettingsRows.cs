using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.ViewModels;

// Rows the storage page and the menu rules page show.
/// <summary>
/// One place something is kept: what it is, what lands there, and where it is. The folder is the one
/// a button opens, which is the library's own folder rather than the file inside it.
/// </summary>
public sealed record StorageRow(string Name, string What, string Where, string Folder);

public sealed record MenuRuleRow(
    string GroupKey,
    string Summary,
    WhaleGenie.Core.Devices.WindowRule Rule,
    bool IsDefault,
    int PageCount,
    bool IsBlocked = false)
{
    public string PageCountLabel => IsBlocked
        ? Strings.Get("Settings.MenuRuleBlocked")
        : Strings.Format("Settings.MenuRulePageCount", PageCount);
}
