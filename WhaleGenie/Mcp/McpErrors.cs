using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Mcp;

/// <summary>
/// The one way a tool says "no". Every refusal carries a stable code, one sentence a person can
/// read, and a hint saying what to do next, because a model that is told only "that failed" spends
/// its next call guessing. The codes are written into a document and never reworded for a message:
/// a client branches on the code, a person reads the sentence.
/// </summary>
internal static class McpErrors
{
    public const string BadArgument = "BadArgument";
    public const string BadJson = "BadJson";
    public const string BadPatch = "BadPatch";
    public const string BadPlacement = "BadPlacement";
    public const string DraftNotFound = "DraftNotFound";
    public const string NoSuchMacro = "NoSuchMacro";
    public const string NoSuchRun = "NoSuchRun";
    public const string UnknownAction = "UnknownAction";
    public const string AlreadyApplied = "AlreadyApplied";
    public const string RevisionConflict = "RevisionConflict";
    public const string NoPackagePath = "NoPackagePath";
    public const string DraftHasBlockingProblems = "DraftHasBlockingProblems";
    public const string DirectControlOff = "DirectControlOff";
    public const string DeviceUnavailable = "DeviceUnavailable";
    public const string DeviceFailed = "DeviceFailed";
    public const string Internal = "Internal";

    private static readonly JsonSerializerOptions Json = new()
    {
        // Nulls stay in: "the pointer could not be read" is a different answer from "the answer has
        // no pointer field", and a shape that keeps its keys is one a client can read without
        // guessing.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

        // A macro name, a parameter name and a failure message are Chinese on a Chinese machine.
        // Escaping every one of them into an escape sequence made answers twice the size and
        // unreadable in a log; that escape only ever mattered inside HTML, and this is JSON over
        // loopback HTTP.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>A result the client can read: one JSON document, no envelope of its own.</summary>
    public static CallToolResult Ok(object value) => new()
    {
        Content = [Text(JsonSerializer.Serialize(value, Json))],
    };

    public static CallToolResult Fail(string code, string message, string? hint = null,
        bool retryable = false, object? details = null) => new()
        {
            IsError = true,
            Content =
        [
            Text(JsonSerializer.Serialize(
                new { error = new Failure(code, message, hint, retryable, details) }, Json)),
        ],
        };

    /// <summary>Runs a tool body, turning whatever stopped it into a coded refusal.</summary>
    public static CallToolResult Run(Func<CallToolResult> body)
    {
        try
        {
            return body();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return Translate(error);
        }
    }

    public static async Task<CallToolResult> RunAsync(Func<Task<CallToolResult>> body)
    {
        try
        {
            return await body();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return Translate(error);
        }
    }

    /// <summary>
    /// Says a device's own refusal the way a client can act on it: which capability is missing, or
    /// the failure key a run would have reported, rather than a stack trace.
    /// </summary>
    public static CallToolResult Translate(Exception error) => error switch
    {
        McpFailure failure => failure.ToResult(),
        DeviceUnavailableException missing => Fail(DeviceUnavailable,
            $"This machine has no {missing.Capability}.",
            "Ask the person to use a machine that has it, or use a step that does not need it."),
        DeviceActionException refused => Fail(DeviceFailed,
            refused.Detail.Length > 0 ? $"{refused.Key}: {refused.Detail}" : refused.Key,
            "The device refused the call; check the window or the input it was aimed at."),
        ArgumentException argument => Fail(BadArgument, argument.Message),
        JsonException json => Fail(BadJson, $"The value is not valid JSON: {json.Message}",
            "Send the macro as a JSON object, not as text inside a string."),
        _ => Fail(Internal, $"{error.GetType().Name}: {error.Message}"),
    };

    private static TextContentBlock Text(string text) => new() { Text = text };

    private sealed record Failure(
        string Code, string Message, string? Hint, bool Retryable, object? Details);
}

/// <summary>
/// A refusal thrown from deep inside a call, where returning a result is not possible. It carries
/// the same facts a result would, so the tool's own <see cref="McpErrors.Run"/> can hand them over
/// unchanged instead of flattening them into one sentence.
/// </summary>
internal sealed class McpFailure(
    string code, string message, string? hint = null, bool retryable = false, object? details = null)
    : Exception(message)
{
    public string Code { get; } = code;

    public CallToolResult ToResult()
        => McpErrors.Fail(Code, Message, hint, retryable, details);
}
