namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The little scripts the browser device runs inside a page, kept apart from the picker: none of
/// them has anything to do with pointing at an element, and the picker is long enough already.
/// </summary>
/// <remarks>
/// What belongs here is the work the page's own document has to do — reading the shape of a table
/// out of its markup, moving its scroll position. A device member can ask Playwright for an element
/// and act on it, but "which of these rows are the column names" and "how far is one screen down"
/// are questions only the document can answer.
/// </remarks>
internal static class BrowserPageScript
{
    /// <summary>
    /// The names and rows of a table, run against the table element.
    /// </summary>
    /// <remarks>
    /// The names are taken from the strip the page itself marks as names — a <c>thead</c> row, or a
    /// first row made of <c>th</c> cells — and not from the first row as it happens to come: a
    /// table that writes no such strip holds rows of data from the top, and calling its first row
    /// of data the names would put every cell of that row out of reach. A table with no names comes
    /// back with none, and the engine names the columns the way it names any unnamed ones.
    /// </remarks>
    internal const string Table = """
        (element) => {
          const rows = [];
          const heads = [];
          const text = (cell) => (cell.innerText || cell.textContent || '').trim();

          const strip = element.tHead ? element.tHead.rows : null;
          const marked = strip && strip.length > 0
            ? strip[strip.length - 1]
            : (element.rows.length > 0 && element.rows[0].cells.length > 0
              && Array.from(element.rows[0].cells).every((cell) => cell.tagName.toLowerCase() === 'th')
              ? element.rows[0]
              : null);

          if (marked) {
            for (const cell of marked.cells) {
              heads.push(text(cell));
            }
          }

          for (const row of element.rows) {
            if (row === marked || (strip && Array.from(strip).includes(row))) {
              continue;
            }

            rows.push(Array.from(row.cells).map(text));
          }

          return { columns: heads, rows: rows };
        }
        """;

    /// <summary>One screen further down the page, which is how a page that adds more as it is read is walked.</summary>
    internal const string Screen = """
        () => {
          window.scrollBy(0, window.innerHeight);
          return true;
        }
        """;

    /// <summary>
    /// To the end of what the page has, which is where what it fetches as it is read keeps arriving:
    /// a page that adds more while it is scrolled needs several of these, each starting from
    /// wherever the one before left it.
    /// </summary>
    internal const string Bottom = """
        () => {
          window.scrollTo(0, document.documentElement.scrollHeight);
          return true;
        }
        """;

    /// <summary>Back to the top of the page, from wherever it was left.</summary>
    internal const string Top = """
        () => {
          window.scrollTo(0, 0);
          return true;
        }
        """;
}
