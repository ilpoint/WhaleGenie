using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Playwright;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Platform.Windows;

// The dialog a page raises, the answer kept for it, and running a script in the page.
public sealed partial class PlaywrightBrowserDevice
{
    public void ArmDialog(bool accept, string text)
    {
        lock (_dialogs)
        {
            _dialogAccepted = accept;
            _dialogText = text;
            _dialogMessage = null;
            _dialogArmed = true;
        }
    }

    public string? TakeDialog()
    {
        lock (_dialogs)
        {
            _dialogArmed = false;
            _dialogText = string.Empty;
            var message = _dialogMessage;
            _dialogMessage = null;
            return message;
        }
    }

    /// <summary>Puts the listener that answers dialogs on one tab of the browser.</summary>
    private void Listen(IPage tab) => tab.Dialog += (_, dialog) => Answered(dialog);

    /// <summary>
    /// Answers a dialog the page put up, the way the running step asked for.
    /// </summary>
    /// <remarks>
    /// The answer is sent from another thread rather than from here: this runs on the driver's own
    /// reader, and a command sent from there would be waiting for an answer that only that reader
    /// can deliver. With nothing armed the dialog is dismissed, which is what the driver does when
    /// nobody is listening at all, so a step that never mentions dialogs behaves as it always did.
    /// </remarks>
    private void Answered(IDialog dialog)
    {
        var accept = false;
        var text = string.Empty;
        lock (_dialogs)
        {
            if (_dialogArmed)
            {
                accept = _dialogAccepted;
                text = _dialogText;
                _dialogMessage = dialog.Message;
                _dialogArmed = false;
            }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (accept)
                {
                    await dialog.AcceptAsync(text.Length > 0 ? text : null);
                }
                else
                {
                    await dialog.DismissAsync();
                }
            }
            catch (PlaywrightException)
            {
                // The dialog was answered or taken away by somebody else in the meantime, which is
                // nothing this side has to report.
            }
        });
    }

    /// <summary>
    /// Runs a script of the step's own inside the page and answers with what it returned as JSON.
    /// </summary>
    public string Evaluate(string script, int timeoutMs) => OnPage(async () =>
    {
        var page = Page();
        page.SetDefaultTimeout(Patience(timeoutMs));
        var result = await page.EvaluateAsync<JsonElement?>(script);
        return Json(result);
    });

    /// <summary>
    /// One JSON value written out the way a person would have written it.
    /// </summary>
    /// <remarks>
    /// What a page answers with arrives with everything outside plain ASCII escaped, so writing it
    /// out as it came would leave a macro reading \u62A5\u544A where the page said 报告. It is
    /// written again here, which for everything else in JSON is the same text either way.
    /// </remarks>
    private static string Json(JsonElement? value)
    {
        if (value is not { } written || written.ValueKind == JsonValueKind.Undefined)
        {
            return string.Empty;
        }

        // A string is handed over as the string itself, without the quotes JSON writes around it.
        // A script that answers with an address or a name is the common case, and the quotes used
        // to travel into whatever read the value next: a browser.goTo given result navigated to
        // ""https://..."", which the browser refused as an address. Objects, lists, numbers and
        // flags stay JSON, because there the text is the only way to carry them.
        return written.ValueKind == JsonValueKind.String
            ? written.GetString() ?? string.Empty
            : JsonSerializer.Serialize(written, ReadableJson);
    }

    /// <summary>How JSON handed to a macro is written out: as it reads, rather than escaped.</summary>
    private static readonly JsonSerializerOptions ReadableJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
