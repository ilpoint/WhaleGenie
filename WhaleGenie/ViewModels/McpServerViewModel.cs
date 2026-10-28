using System;
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
/// launches WhaleGenie with <c>--mcp</c> and talks down the pipe it owns, so there is nothing here
/// to switch on and no address to show. What a person needs instead is the block to paste into the
/// client's own configuration file, which is what this hands over — and the record of what a client
/// did, which is the question asked afterwards.
/// </summary>
public partial class McpServerViewModel : ViewModelBase
{
    private string _status = string.Empty;

    public string Header => Strings.Get("Mcp.Title");

    /// <summary>
    /// The block to paste into a client's configuration file. It names this program and the flag
    /// that makes it speak MCP; the client starts the process itself, which is why no port appears.
    /// </summary>
    public string Config => JsonSerializer.Serialize(new
    {
        mcpServers = new
        {
            whalegenie = new
            {
                command = Executable,
                args = new[] { "--mcp" },
            },
        },
    }, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>This program's own path, which is what the client has to be told to start.</summary>
    public string Executable => McpProgram.Executable;

    /// <summary>Where the block was copied, or why there is nothing to copy.</summary>
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
}
