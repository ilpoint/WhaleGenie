using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
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

                Status = Strings.Get("Mcp.Starting");
                await _host.StartAsync(Port);
                Endpoint = _host.Endpoint;
                IsRunning = _host.IsRunning;
                Status = Strings.Get("Mcp.On");
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
            Endpoint = string.Empty;
        }
    }

    /// <summary>Short confirmation under the flyout after the client config was copied.</summary>
    public void ShowCopied() => Status = Strings.Get("Mcp.Copied");

    public override void RefreshLanguage()
    {
        base.RefreshLanguage();
        Status = IsRunning ? Strings.Get("Mcp.On") : Strings.Get("Mcp.Off");
    }
}
