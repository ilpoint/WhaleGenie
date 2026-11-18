namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The piece of AutoHotkey this program writes out when a script of that language runs: the five
/// names a script reaches the macro by, and the little program behind them that carries each call
/// to a bridge process. It is kept here as text rather than shipped as a file for the same reason
/// the Python bridge is: it has to match the engine on the other side of it, so it travels with it.
/// </summary>
/// <remarks>
/// AutoHotkey cannot write to a named pipe with its own file functions, so it does not try: the
/// calls go through a second copy of this program, which is what knows how to talk to the macro.
/// Everything a call carries travels in two files — what was asked and what was answered — and the
/// one byte on the bridge's output says the answer is there. Whatever the macro holds arrives as the
/// text of one line: a list or a record as its JSON, which the script may read or hand straight back.
/// </remarks>
internal static class AutoHotkeyBridge
{
    /// <summary>
    /// What a script of this language finds: the five names, and the bridge behind them. It is
    /// written beside the script, so everything it names is a full path filled in by the engine.
    /// </summary>
    public const string Prelude = """
        ; The macro behind a script: what an AutoHotkey script may ask WhaleGenie for while it runs.
        ; This part is written by the program; a script only ever calls the five names below.

        ; A mistake in a script is a line on the error output rather than a dialog: a dialog is a
        ; window the macro would wait on for someone to close it, and the person who wrote the
        ; script is not sitting in front of the running macro.
        OnError(WhaleGenieFailed)

        WhaleGenieFailed(error, mode) {
            said := error.Message
            if (error.HasProp("Line") && error.Line != "") {
                if (error.File = "__SCRIPT__") {
                    said .= "`n  (line " . (error.Line - __OFFSET__) . ")"
                } else {
                    said .= "`n  at " . error.File . " (" . error.Line . ")"
                }
            }

            FileAppend(said . "`n", "**")
            ExitApp(2)
        }

        GetProperty(name) {
            return WhaleGenieAsk("get", name)
        }

        SetProperty(name, value) {
            WhaleGenieAsk("set", name, value)
        }

        StepExecute(id) {
            return WhaleGenieAsk("step", id) = "1"
        }

        Print(text) {
            WhaleGenieAsk("print", "", text)
        }

        SetResult(value) {
            WhaleGenieAsk("result", "", value)
        }

        WhaleGenieAsk(verb, name, value := "") {
            ; One bridge per script rather than one per call: starting a copy of the program is
            ; what a call would otherwise cost most of its time on.
            static bridge := ""
            if (!IsObject(bridge)) {
                bridge := ComObject("WScript.Shell").Exec(
                    '"__EXE__" --script-bridge "__ASKED__" "__ANSWERED__"')
            }

            if FileExist("__ASKED__") {
                FileDelete("__ASKED__")
            }

            kind := IsNumber(value) ? "n" : "t"
            FileAppend(verb . "`n" . name . "`n" . kind . "`n" . value, "__ASKED__", "UTF-8")

            bridge.StdIn.WriteLine("go")
            answer := bridge.StdOut.ReadLine()
            if (answer != "done") {
                throw Error("WhaleGenie: the script bridge did not answer.")
            }

            said := StrSplit(FileRead("__ANSWERED__", "UTF-8"), "`n", "`r")
            if (said.Length = 0) {
                throw Error("WhaleGenie: the script bridge left nothing behind.")
            }

            if (said[1] = "no") {
                throw Error("WhaleGenie: " . (said.Length > 1 ? said[2] : "the macro refused that."))
            }

            return said.Length > 1 ? said[2] : ""
        }
        """;

    /// <summary>
    /// What the engine puts in front of a script's own first line: syntax errors into the error
    /// output rather than into a dialog, then the line that brings the five names into reach. Two
    /// lines rather than the whole piece of bridge, so a mistake in a script is still reported
    /// against the line it was written on, brought back by <see cref="Ahead"/>.
    /// </summary>
    public static string Top(string prelude)
        => "#ErrorStdOut \"UTF-8\"" + Environment.NewLine
            + $"#Include \"{prelude}\"" + Environment.NewLine;

    /// <summary>
    /// How many lines stand in front of a script's own first line. An error in a script of this
    /// language is reported against the running file, so the line it names is that many too far
    /// down — and what the writer of the script has in front of them is the script they wrote.
    /// </summary>
    public static int Ahead(string prelude) => Top(prelude).Count(letter => letter == '\n');
}
