using System;
using System.Collections.Generic;
using WhaleGenie.Contracts;
using WhaleGenie.Editing;
using WhaleGenie.Localization;

namespace WhaleGenie.Bridge;

/// <summary>
/// A macro's callable children: making an empty one, copying one in, and removing one the macro no
/// longer calls. The command names the child, so the contract's two answers are "that macro does
/// not exist" and "that child is still called"; the first is the same code opening a macro uses.
/// </summary>
internal sealed partial class EditorSessionBridge
{
    private object? ApplySubMacro(EditorSessionCommand command)
    {
        var session = Required();
        switch (command)
        {
            case EditorSessionSubMacroCreateCommand create:
                return SubMacroAnswer(session,
                    session.CreateSubMacro(OptionalSubMacroName(create.Name)));
            case EditorSessionSubMacroCopyCommand copy:
                {
                    var source = RequiredSubMacroText(copy.From);
                    try
                    {
                        return SubMacroAnswer(session,
                            session.CopySubMacro(source, OptionalSubMacroName(copy.Name)));
                    }
                    catch (KeyNotFoundException)
                    {
                        throw new BridgeFailureException(BridgeErrors.NoSuchMacro,
                            Strings.Format("Bridge.NoSuchMacro", source));
                    }
                }

            case EditorSessionSubMacroDeleteCommand delete:
                {
                    var name = RequiredSubMacroText(delete.Name);
                    try
                    {
                        return SubMacroAnswer(session, session.DeleteSubMacro(name));
                    }
                    catch (KeyNotFoundException)
                    {
                        throw new BridgeFailureException(BridgeErrors.NoSuchMacro,
                            Strings.Format("Bridge.NoSuchMacro", name));
                    }
                    catch (InvalidOperationException)
                    {
                        throw new BridgeFailureException(BridgeErrors.BadArgument,
                            Strings.Format("Bridge.SubMacroInUse", name));
                    }
                }

            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsMacro"));
        }
    }

    /// <summary>
    /// The answer the page reads for a session command: the snapshot the command changed, beside
    /// the tabs and the action being written, which the command itself did not touch.
    /// </summary>
    private object? SubMacroAnswer(HeadlessEditorSession document, EditorSnapshot snapshot)
    {
        // A child's command is answered by the macro that owns the children. The page is looking at
        // the child's document, so the strip moves to the macro the answer is about: otherwise the
        // root's steps would arrive under the child's tab.
        var root = document.RootDocument;
        _documents.Activate(root);
        return Envelope(snapshot, root.ActionFormState());
    }

    /// <summary>A name the command may leave out, which asks the program to pick the next free one.</summary>
    private static string? OptionalSubMacroName(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Trim() is { Length: > 0 } name
            ? name
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
    }

    /// <summary>A name or source the command has to carry before anything can be found by it.</summary>
    private static string RequiredSubMacroText(string? value)
        => OptionalSubMacroName(value)
            ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
}
