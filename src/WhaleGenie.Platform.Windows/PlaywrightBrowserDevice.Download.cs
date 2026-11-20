using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Playwright;

namespace WhaleGenie.Platform.Windows;

// A file the page hands over: the click that starts it and the wait for it to arrive.
public sealed partial class PlaywrightBrowserDevice
{
    /// <summary>
    /// Takes the next file the page hands over.
    /// </summary>
    /// <remarks>
    /// A download that was caught before this step asked for it is handed over first: the page may
    /// have started one a step ago, and a macro that asks a moment too late should not have to wait
    /// for a second one that may never come. With a target, the click and the wait for what it
    /// starts are one call to the driver, because a file that arrives between the two would
    /// otherwise be one nobody is listening for; the file is saved where the step said, which is
    /// also what stops the driver throwing it away.
    /// </remarks>
    public DownloadResult Download(PageTarget? target, string folder, int timeoutMs)
    {
        var patience = Patience(timeoutMs);
        try
        {
            return Wait(async () =>
            {
                var page = Page();
                page.SetDefaultTimeout(patience);
                Directory.CreateDirectory(folder);

                var caught = Caught();
                if (caught is null)
                {
                    caught = target is null || target.Selectors.Count == 0
                        ? await Arrived(patience)
                        : await ClickedAndCaught(page, target, patience);
                    Taken(caught);
                }

                var name = caught.SuggestedFilename;
                var path = Path.Combine(folder, name);
                await caught.SaveAsAsync(path);
                return new DownloadResult(path, name);
            });
        }
        catch (TimeoutException)
        {
            throw new DeviceActionException(RunKeys.Timeout,
                patience.ToString(CultureInfo.InvariantCulture));
        }
        catch (PlaywrightException failure)
        {
            throw Failed(failure);
        }
    }

    /// <summary>Clicks the element a target names and answers with the file that click started.</summary>
    private async Task<IDownload> ClickedAndCaught(IPage page, PageTarget target, int patience)
    {
        var (_, element) = await Found(target, patience);
        return await page.RunAndWaitForDownloadAsync(
            () => element.ClickAsync(),
            new PageRunAndWaitForDownloadOptions { Timeout = patience });
    }

    /// <summary>
    /// The oldest file the browser has caught and nobody has taken, or nothing when there is none.
    /// </summary>
    /// <remarks>
    /// What is handed over here is written down as taken in the same breath, so the same file cannot
    /// come out twice however many steps ask for one.
    /// </remarks>
    private IDownload? Caught()
    {
        lock (_caught)
        {
            while (_caught.Count > 0)
            {
                var download = _caught.Dequeue();
                if (_taken.Add(download))
                {
                    return download;
                }
            }

            return null;
        }
    }

    /// <summary>Keeps a file the page handed over for the step that has not asked for it yet.</summary>
    private void Catch(IDownload download)
    {
        lock (_caught)
        {
            if (_taken.Contains(download))
            {
                return;
            }

            _caught.Enqueue(download);
        }

        _arrivals.Release();
    }

    /// <summary>
    /// The next file the page hands over: one already caught if there is one, and otherwise the one
    /// that arrives within the step's patience.
    /// </summary>
    private async Task<IDownload> Arrived(int patience)
    {
        var deadline = Environment.TickCount64 + patience;
        while (true)
        {
            if (Caught() is { } caught)
            {
                return caught;
            }

            var left = deadline - Environment.TickCount64;
            if (left <= 0)
            {
                throw new TimeoutException();
            }

            // Woken by the listener the moment a file arrives; the slice only bounds how long the
            // loop sleeps when the signal was already spent by an earlier, taken file.
            await _arrivals.WaitAsync(TimeSpan.FromMilliseconds(Math.Min(left, 100)))
                .ConfigureAwait(false);
        }
    }

    /// <summary>Writes a file down as taken, which the listener may not have reached yet.</summary>
    private void Taken(IDownload download)
    {
        lock (_caught)
        {
            _taken.Add(download);
        }
    }
}
