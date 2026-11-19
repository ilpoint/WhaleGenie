using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

// What every family uses: waiting for a value, writing a log line, and the failure a step carries.
public sealed partial class MacroRunner
{
    /// <summary>The same, for something that turns up rather than becomes true.</summary>
    private static async Task<T> WaitForValueAsync<T>(Func<T?> probe, int interval,
        CancellationToken token)
        where T : class
    {
        while (true)
        {
            if (probe() is { } found)
            {
                return found;
            }

            await Pacing.Pause(interval, token);
        }
    }

    private void Log(LogLevel level, int depth, string step, string key, params object[] arguments)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, depth, step, key, arguments);

        // Kept beside being handed to the host: a run that ends on a failure writes all of this to
        // a file afterwards, and a run nobody is watching has no host that keeps it.
        _run.Log.Add(entry);
        _host.Log(entry);
    }
}
