using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Localization;
using WhaleGenie.Mcp;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.ViewModels;

/// <summary>
/// The MCP page in the main window. This program no longer serves MCP from inside itself: a client
/// launches WhaleGenie with <c>--mcp</c> and talks down the pipe it owns, so there is no server here
/// to switch on. The two switches decide what the client may do, and the block underneath is what a
/// person pastes into that client's configuration once — it does not carry the switches, because a
/// served run reads them from the settings file before every call.
/// </summary>
public partial class McpServerViewModel : ViewModelBase
{
    private readonly bool _loaded;
    private bool _allowControl;
    private bool _allowRun;
    private string _status = string.Empty;

    public McpServerViewModel()
    {
        var grants = LocalSettings.LoadMcpGrants();
        _allowControl = grants.Control;
        _allowRun = grants.Run;
        _loaded = true;
    }

    public string Header => Strings.Get("Mcp.Title");

    /// <summary>
    /// Whether the client may send keys, clicks and window commands. Stored in the settings file,
    /// which is where a running server reads it from before every call.
    /// </summary>
    public bool AllowControl
    {
        get => _allowControl;
        set => Grant(nameof(AllowControl), ref _allowControl, value);
    }

    /// <summary>
    /// Whether the client may run one macro at a time and read what every step worked out. A second
    /// switch rather than part of the first: clicking once and running a macro that writes files are
    /// not the same favour.
    /// </summary>
    public bool AllowRun
    {
        get => _allowRun;
        set => Grant(nameof(AllowRun), ref _allowRun, value);
    }

    /// <summary>
    /// The block to paste into a client's configuration file: this program and the flag that makes
    /// it speak MCP. Nothing about permissions is in it — those are the switches above, which the
    /// served run reads while it is running.
    /// </summary>
    public string Config => JsonSerializer.Serialize(new
    {
        mcpServers = new
        {
            whalegenie = new
            {
                command = Executable,
                args = Arguments(),
            },
        },
    }, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>This program's own path, which is what the client has to be told to start.</summary>
    public string Executable => McpProgram.Executable;

    /// <summary>Where the block was copied, or why there was nothing to copy.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Opens the file the calls are recorded in. The person asking "what did it do" is at the
    /// machine, not in a terminal, so the answer is a file opened for them — and the folder when
    /// nothing has been written yet, which is a different answer from an empty file.
    /// </summary>
    [RelayCommand]
    private void OpenAudit()
    {
        var file = McpAudit.File;
        var target = File.Exists(file) ? file : AppPaths.Logs;
        Directory.CreateDirectory(AppPaths.Logs);
        Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
    }

    /// <summary>Short confirmation under the page after the client config was copied.</summary>
    public void ShowCopied() => Status = Strings.Get("Mcp.Copied");

    /// <summary>Why there was nothing to copy: a build running out of a folder with no program in it.</summary>
    public void ShowNoProgram() => Status = Strings.Get("Mcp.NoProgram");

    public override void RefreshLanguage()
    {
        base.RefreshLanguage();
        OnPropertyChanged(nameof(Header));
    }

    /// <summary>
    /// The arguments the block asks for. MCP itself and nothing else: the permissions a run works
    /// under are the switches above, which it reads as it goes, so they are not baked into a command
    /// line a client would have to be reconfigured to change.
    /// </summary>
    private static IReadOnlyList<string> Arguments() => ["--mcp"];

    /// <summary>
    /// Flips one switch and remembers it. The block does not change, and neither does the clipboard —
    /// the client already running is the one this is meant to reach, and it reads the new answer
    /// before its next call.
    /// </summary>
    private void Grant(string name, ref bool field, bool value)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(name);
        if (_loaded)
        {
            LocalSettings.StoreMcpGrants(_allowControl, _allowRun);
        }
    }
}
