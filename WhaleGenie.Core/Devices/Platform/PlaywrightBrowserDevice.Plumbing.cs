using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Playwright;

namespace WhaleGenie.Core.Devices.Platform;

// Getting onto the page from the calling thread, and running work against an element.
public sealed partial class PlaywrightBrowserDevice
{
    /// <summary>
    /// Runs one of the page actions, and puts the page into the failure if it cannot be done.
    /// </summary>
    /// <remarks>
    /// "The element was not found" says very little on its own — and it is the failure that turns up
    /// most, because a page that has changed since the element was picked no longer has it. With the
    /// page it was looked for on, it usually says the whole thing: the macro was sent to the sign-in
    /// page, or somewhere else entirely, and the selector was never the problem.
    /// </remarks>
    private void OnPage(Func<Task> work)
    {
        try
        {
            Wait(work);
        }
        catch (Exception failure) when (failure is PlaywrightException or TimeoutException)
        {
            throw Failed(failure);
        }
    }

    private T OnPage<T>(Func<Task<T>> work)
    {
        try
        {
            return Wait(work);
        }
        catch (Exception failure) when (failure is PlaywrightException or TimeoutException)
        {
            throw Failed(failure);
        }
    }

    /// <summary>
    /// Finds the element a target names and does one of the things a step does to an element,
    /// answering with the selector that turned out to name it.
    /// </summary>
    private string OnElement(PageTarget target, int timeoutMs, Func<ILocator, Task> work)
        => OnElement(target, timeoutMs, async (element, selector) =>
        {
            await work(element);
            return selector;
        });

    /// <summary>
    /// One part of one element. The three parts are three different questions about the same
    /// element, and a step names which one it is after: the writing it shows, what it holds, or one
    /// attribute of its markup.
    /// </summary>
    private static async Task<string> Part(ILocator element, PagePart part, string attribute)
        => part switch
        {
            PagePart.Value => await element.InputValueAsync(),

            // A step that reads an attribute and names none is a step that did not say what to
            // read; the element on its own is not the answer to a question about its markup.
            PagePart.Attribute => attribute.Trim().Length > 0
                ? await element.GetAttributeAsync(attribute.Trim()) ?? string.Empty
                : throw new DeviceActionException(RunKeys.BrowserNoAttribute),

            _ => await element.InnerTextAsync(),
        };

    /// <summary>
    /// A table as the page wrote it down, with the names above its columns and its rows as lists of
    /// what the cells show. The most rows a step asked for is the most that is taken.
    /// </summary>
    private static PageTable TableOf(JsonElement? written, int maxRows)
    {
        if (written is not { ValueKind: JsonValueKind.Object } table)
        {
            return new PageTable([], []);
        }

        var columns = new List<string>();
        if (table.TryGetProperty("columns", out var heads) && heads.ValueKind == JsonValueKind.Array)
        {
            columns.AddRange(heads.EnumerateArray().Select(Cell));
        }

        var rows = new List<IReadOnlyList<string>>();
        if (table.TryGetProperty("rows", out var body) && body.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in body.EnumerateArray())
            {
                if (rows.Count >= maxRows)
                {
                    break;
                }

                rows.Add(row.ValueKind == JsonValueKind.Array
                    ? [.. row.EnumerateArray().Select(Cell)]
                    : []);
            }
        }

        return new PageTable(columns, rows);
    }

    /// <summary>What one cell of a table came back as, whether or not the page wrote a string.</summary>
    private static string Cell(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => value.GetRawText(),
    };

    /// <summary>
    /// Finds the element a target names and does something with it, answering with whatever that
    /// made of the element: both the element and the selector that named it are handed over, so a
    /// step whose own answer is "which one was used" has it to answer with.
    /// </summary>
    private T OnElement<T>(PageTarget target, int timeoutMs, Func<ILocator, string, Task<T>> work)
    {
        var patience = Patience(timeoutMs);
        try
        {
            return Wait(async () =>
            {
                Page().SetDefaultTimeout(patience);
                var (selector, element) = await Found(target, patience);
                return await work(element, selector);
            });
        }
        catch (Exception failure) when (failure is PlaywrightException or TimeoutException)
        {
            throw Missing(target, failure);
        }
    }

    /// <summary>
    /// How long the browser gives its own part of a step: the step's own timeout when it has one,
    /// and the device's half minute otherwise, which is what a step that named no length of its own
    /// used to be given without being told.
    /// </summary>
    private static int Patience(int timeoutMs) => timeoutMs > 0 ? timeoutMs : DefaultWaitMs;
}
