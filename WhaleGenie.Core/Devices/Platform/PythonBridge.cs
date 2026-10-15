namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The two pieces of Python this program writes out when a script runs: the module a script finds
/// as <c>WhaleGenie</c>, which knows how to talk to the macro over the pipe, and the little program
/// that puts it in place and then runs what the macro wrote. They are kept here as text rather than
/// shipped as files for the same reason the browser picker's script is: they have to match the
/// engine that reads the other end of the pipe, so they travel with it.
/// </summary>
internal static class PythonBridge
{
    /// <summary>
    /// What a script finds as <c>WhaleGenie</c>. Every call is one line of JSON out and one line
    /// back, written under a lock because a script may start threads of its own.
    /// </summary>
    public const string Host = """
        # The macro behind a script: what a Python script may ask WhaleGenie for while it runs.

        import json
        import os
        import threading


        class Host:
            # The object a script started by a macro finds as WhaleGenie. The pipe is a file the
            # other end is writing lines of JSON to, read and written here without buffering: a
            # named pipe cannot be sought, so the bytes are gathered by hand until a line is whole.

            def __init__(self, handle):
                self._handle = handle
                self._held = b""
                self._lock = threading.Lock()

            def _call(self, message):
                with self._lock:
                    line = json.dumps(message, ensure_ascii=False) + "\n"
                    os.write(self._handle, line.encode("utf-8"))
                    answer = self._line()
                said = json.loads(answer.decode("utf-8"))
                if not said.get("ok"):
                    raise RuntimeError(said.get("error") or "the macro refused that")
                return said.get("value")

            def _line(self):
                while b"\n" not in self._held:
                    chunk = os.read(self._handle, 65536)
                    if not chunk:
                        raise RuntimeError("the macro is no longer listening for this script")
                    self._held += chunk
                line, self._held = self._held.split(b"\n", 1)
                return line

            def GetProperty(self, name):
                # What the macro variable of that name holds, or None when it has no such name.
                return self._call({"call": "get", "name": str(name)})

            def SetProperty(self, name, value):
                # Writes a value into the macro being run.
                self._call({"call": "set", "name": str(name), "value": value})

            def StepExecute(self, id):
                # Runs one step of the macro being run, named by its four-character identity.
                return bool(self._call({"call": "step", "id": str(id)}))

            def Print(self, text):
                # Puts a line into the run log.
                self._call({"call": "print", "text": str(text)})

            def SetResult(self, value):
                # Hands back what this step should leave behind as its result.
                self._call({"call": "result", "value": value})


        def connect():
            # Connects to the macro that started this script.
            name = os.environ.get("WHALEGENIE_PIPE", "")
            if not name:
                raise RuntimeError("this script was not started by WhaleGenie")
            return Host(os.open("\\\\.\\pipe\\" + name, os.O_RDWR | os.O_BINARY))
        """;

    /// <summary>
    /// What is started instead of the script itself: it puts the macro in reach of every line the
    /// script runs, then runs it. The script is compiled under its own path so that a mistake in it
    /// is reported against the line it was written on.
    /// </summary>
    public const string Boot = """
        # Starts the script a macro wrote, with the macro itself in reach.

        import builtins
        import sys

        import whalegenie

        builtins.WhaleGenie = whalegenie.connect()

        # What the script sees as its own command line: its path first, then the arguments the step
        # wrote, the way a script started from a command line sees them.
        sys.argv = sys.argv[1:]
        path = sys.argv[0]

        with open(path, "rb") as source:
            written = source.read()

        # The copy was written as UTF-8, and a mark left on the front is not part of the script.
        if written.startswith(b"\xef\xbb\xbf"):
            written = written[3:]

        exec(compile(written.decode("utf-8"), path, "exec"),
             {"__name__": "__main__", "__file__": path, "__builtins__": builtins})
        """;
}
