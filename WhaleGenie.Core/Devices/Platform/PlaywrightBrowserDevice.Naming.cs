using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Playwright;

namespace WhaleGenie.Core.Devices.Platform;

// Turning a selector line into a locator: frames, chains and the conditions that narrow it.
public sealed partial class PlaywrightBrowserDevice
{
    /// <summary>
    /// The element a line of a target names: the frames it sits inside entered first, then the
    /// element itself, named whichever of the ways the line asked for, with the conditions the step
    /// added asked of it in turn.
    /// </summary>
    /// <remarks>
    /// A line that begins with none of the ways of writing a frame is Playwright's own selector,
    /// handed over as it stands so that everything a macro could write by hand — a CSS path, a
    /// chain of its own, a text or role engine — keeps working untouched. The four semantic ways
    /// are the ones Playwright has no selector spelling for, so they are asked as questions
    /// instead, with the meaning the API gives them: the words match anywhere in the name, with
    /// case and spacing set aside.
    /// <para>
    /// The conditions are Playwright's own answers and nothing of this device's own making: looking
    /// inside a container is one locator asked under another, and text, held elements and being on
    /// screen are the filters the engine offers on any locator. Like the four semantic ways, they
    /// ride along a locator afterwards, so the element found by a condition is the element the rest
    /// of the step — clicking, reading, counting — works through.
    /// </para>
    /// </remarks>
    private ILocator Named(string line, IReadOnlyList<PageCondition> conditions)
    {
        var outer = Framed(line, out var element);

        // Where the element is looked for: a condition that says which container it sits in is a
        // step further in, and a second such condition goes further in again, so the rows read the
        // way they are written.
        var place = outer;
        foreach (var condition in conditions)
        {
            if (condition.How is PageFilter.Inside)
            {
                place = place.Within(Said(condition));
            }
        }

        var found = Ask(place, element);
        foreach (var condition in conditions)
        {
            found = Narrow(found, condition, outer);
        }

        return found;
    }

    /// <summary>
    /// The page or the frame a line says to look in, and what is left of the line once its frames
    /// have been read off the front of it.
    /// </summary>
    private Place Framed(string line, out string element)
    {
        var rest = line;
        var place = new Place(Page(), null, null);
        while (true)
        {
            var cut = rest.IndexOf(Chain, StringComparison.Ordinal);
            var head = cut < 0 ? rest : rest[..cut];
            if (!head.StartsWith(FrameStep, StringComparison.Ordinal))
            {
                break;
            }

            var inside = head[FrameStep.Length..].Trim();
            if (inside.Length == 0 || cut < 0)
            {
                // A line that only says which frame to look in names no element inside it.
                throw new DeviceActionException(RunKeys.BrowserNoSelector);
            }

            place = place.Frame(inside);
            rest = rest[(cut + Chain.Length)..];
        }

        element = rest;
        return place;
    }

    /// <summary>
    /// One written line of a page turned into a locator, from wherever it is being looked for: the
    /// four ways of naming an element by the words a person reads it by, and otherwise the line the
    /// engine already understands, handed over whole.
    /// </summary>
    private static ILocator Ask(Place place, string line)
    {
        foreach (var way in Semantic)
        {
            if (line.StartsWith(way, StringComparison.Ordinal))
            {
                var text = line[way.Length..].Trim();
                if (text.Length == 0)
                {
                    throw new DeviceActionException(RunKeys.BrowserNoSelector);
                }

                return way switch
                {
                    "label=" => place.ByLabel(text),
                    "placeholder=" => place.ByPlaceholder(text),
                    "alt=" => place.ByAltText(text),
                    _ => place.ByTitle(text),
                };
            }
        }

        return place.Locator(line);
    }

    /// <summary>
    /// One condition asked of an element, as the filter the engine puts on a locator. A condition
    /// that names where the element sits was spent before the element was looked for, so it has
    /// nothing to add here.
    /// </summary>
    /// <remarks>
    /// Text, held elements and being on screen are the engine's own filters. The two that ask about
    /// an element the found one holds are asked from the page itself rather than from the place the
    /// element was found in, because the engine reads those relative to the element being filtered
    /// and a locator that is already inside something else is not a question it accepts.
    /// </remarks>
    private static ILocator Narrow(ILocator element, PageCondition condition, Place outer) =>
        condition.How switch
        {
            PageFilter.Has => element.Filter(new LocatorFilterOptions
            {
                Has = Ask(outer, Said(condition)),
            }),
            PageFilter.HasNot => element.Filter(new LocatorFilterOptions
            {
                HasNot = Ask(outer, Said(condition)),
            }),
            PageFilter.HasText => element.Filter(new LocatorFilterOptions
            {
                HasTextString = Said(condition),
            }),
            PageFilter.HasNotText => element.Filter(new LocatorFilterOptions
            {
                HasNotTextString = Said(condition),
            }),
            PageFilter.Visible => element.Filter(new LocatorFilterOptions { Visible = true }),
            _ => element,
        };

    /// <summary>
    /// What a condition asks for, refused when the step left it out: a condition that says neither
    /// what to look for nor what to leave out is not one the engine can put to the page.
    /// </summary>
    private static string Said(PageCondition condition)
    {
        var said = condition.Value.Trim();
        if (said.Length == 0 && condition.How is not PageFilter.Visible)
        {
            throw new DeviceActionException(RunKeys.BrowserBadFilter, condition.How.ToString());
        }

        return said;
    }

    /// <summary>
    /// Where a line is looked for: the page itself, or, once a line has said which frame it means,
    /// that frame — or, once a condition has said which container the element sits in, that
    /// container. All three ask their questions the same way, which is why one of these stands for
    /// any of them.
    /// </summary>
    private sealed class Place(IPage page, IFrameLocator? frame, ILocator? within)
    {
        // A frame is read off the front of a line, before any condition is applied, so this never
        // has a container to step out of.
        public Place Frame(string selector) => frame is null
            ? new Place(page, page.FrameLocator(selector), null)
            : new Place(page, frame.FrameLocator(selector), null);

        /// <summary>The same place with one container between it and the element: look inside this.</summary>
        public Place Within(string line) => new(page, frame, Ask(this, line));

        public ILocator Locator(string selector) => within is not null
            ? within.Locator(selector)
            : frame is null ? page.Locator(selector) : frame.Locator(selector);

        public ILocator ByLabel(string text) => within is not null
            ? within.GetByLabel(text)
            : frame is null ? page.GetByLabel(text) : frame.GetByLabel(text);

        public ILocator ByPlaceholder(string text) => within is not null
            ? within.GetByPlaceholder(text)
            : frame is null ? page.GetByPlaceholder(text) : frame.GetByPlaceholder(text);

        public ILocator ByAltText(string text) => within is not null
            ? within.GetByAltText(text)
            : frame is null ? page.GetByAltText(text) : frame.GetByAltText(text);

        public ILocator ByTitle(string text) => within is not null
            ? within.GetByTitle(text)
            : frame is null ? page.GetByTitle(text) : frame.GetByTitle(text);
    }
}
