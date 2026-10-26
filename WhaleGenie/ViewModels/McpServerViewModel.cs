using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Mcp;

namespace WhaleGenie.ViewModels;

/// <summary>
/// The MCP switch in the main window. It owns the local server's lifetime and the address shown in
/// the flyout; the tools themselves live behind <see cref="McpServerHost"/>.
/// </summary>
public partial class McpServerViewModel : ViewModelBase
{
    private readonly McpServerHost _host;
    private bool _running;
    private bool _busy;
    private bool _allowControl;

    private bool _allowRun;
    private string _status = Strings.Get("Mcp.Off");
    private string _endpoint = string.Empty;
    private int _port;

    public McpServerViewModel(MainViewModel project)
    {
        _host = new McpServerHost(project);
        _port = Math.Clamp(LocalSettings.LoadMcpPort(), 1024, 65535);
    }

    /// <summary>The port the next start asks for. It is disabled while the server is running.</summary>
    public int Port
    {
        get => _port;
        set
        {
            var port = Math.Clamp(value, 1024, 65535);
            if (_port == port)
            {
                return;
            }

            _port = port;
            LocalSettings.StoreMcpPort(port);
            OnPropertyChanged();
        }
    }

    public bool IsRunning
    {
        get => _running;
        private set
        {
            if (_running == value)
            {
                return;
            }

            _running = value;
            OnPropertyChanged();
        }
    }

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (_busy == value)
            {
                return;
            }

            _busy = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Whether an MCP client may send keys, clicks and window commands to this machine. It is the
    /// flyout's own switch, beside the server's: looking at the screen needs nothing, while driving
    /// the machine is the person's to allow. It goes back off when the server stops, so a client
    /// left connected cannot keep driving after the person has walked away.
    /// </summary>
    public bool AllowControl
    {
        get => _allowControl;
        set
        {
            if (_allowControl == value)
            {
                return;
            }

            _allowControl = value;
            McpControl.Allowed = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Whether an MCP client may run one macro at a time. A second switch rather than part of the
    /// first: clicking once and running a macro that writes files are not the same favour, and it
    /// goes back off when the server stops for the same reason the first one does.
    /// </summary>
    public bool AllowRun
    {
        get => _allowRun;
        set
        {
            if (_allowRun == value)
            {
                return;
            }

            _allowRun = value;
            McpControl.AllowsRun = value;
            OnPropertyChanged();
        }
    }

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

    public string Endpoint
    {
        get => _endpoint;
        private set
        {
            if (_endpoint == value)
            {
                return;
            }

            _endpoint = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Asks the window to give the project a package path before MCP starts. Null in checks and in
    /// any host without a window; the switch then starts without the question.
    /// </summary>
    internal Func<Task<bool>>? EnsureSaved { get; set; }

    [RelayCommand]
    private async Task ToggleAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (IsRunning)
            {
                Status = Strings.Get("Mcp.Stopping");
                await _host.StopAsync();
                IsRunning = false;
                AllowControl = false;
                AllowRun = false;
                Endpoint = string.Empty;
                Status = Strings.Get("Mcp.Off");
            }
            else
            {
                if (EnsureSaved is { } ensure && !await ensure())
                {
                    Status = Strings.Get("Mcp.NeedSave");
                    return;
                }

                await StartHeadlessAsync(Port);
            }
        }
        catch (Exception error)
        {
            Status = Strings.Format("Mcp.StartFailed", error.Message);
            try
            {
                await _host.StopAsync();
            }
            catch
            {
                // The status already carries the reason the start failed.
            }

            IsRunning = false;
            AllowControl = false;
            AllowRun = false;
            Endpoint = string.Empty;
        }
        finally
        {
            IsBusy = false;

            // A switch the user clicked has already moved itself; tell the one-way binding to read
            // the truth again, including when the save question was cancelled.
            OnPropertyChanged(nameof(IsRunning));
        }
    }

    /// <summary>
    /// Starts the server for a run that has no window. There is nobody to ask about saving and no
    /// switch to flip, so the port is the one that was handed in and the settings file is left
    /// alone: a check must not change what the person chose.
    /// </summary>
    internal async Task StartHeadlessAsync(int port)
    {
        Status = Strings.Get("Mcp.Starting");
        await _host.StartAsync(port);
        Endpoint = _host.Endpoint;
        IsRunning = _host.IsRunning;
        Status = Strings.Get("Mcp.On");
    }

    /// <summary>Stops the server when the program is leaving, without touching the switch's text.</summary>
    public async Task StopAsync()
    {
        try
        {
            await _host.StopAsync();
        }
        finally
        {
            IsRunning = false;
            AllowControl = false;
            AllowRun = false;
            Endpoint = string.Empty;
        }
    }

    /// <summary>Flips the direct-control switch from the flyout, the way the server's own one flips.</summary>
    [RelayCommand]
    private void ToggleControl() => AllowControl = !AllowControl;

    /// <summary>The same for running macros, which is the other thing a client can be let do.</summary>
    [RelayCommand]
    private void ToggleRun() => AllowRun = !AllowRun;

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

    /// <summary>Short confirmation under the flyout after the client config was copied.</summary>
    public void ShowCopied() => Status = Strings.Get("Mcp.Copied");

    public override void RefreshLanguage()
    {
        base.RefreshLanguage();
        Status = IsRunning ? Strings.Get("Mcp.On") : Strings.Get("Mcp.Off");
    }
}
