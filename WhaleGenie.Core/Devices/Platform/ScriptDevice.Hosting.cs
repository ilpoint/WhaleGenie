using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using NuGet.Common;
using WhaleGenie.Core.Expressions;


namespace WhaleGenie.Core.Devices.Platform;

// Starting the process, the pipe the host answers on, and stopping it again.
public sealed partial class ScriptDevice : IScriptDevice
{
    /// <summary>
    /// Starts the program a script runs in, saying in the engine's own words what it means when
    /// Windows reports that it will not start: for Python that this machine has none, and for this
    /// program's own script host that it would not run.
    /// </summary>
    private static Process Start(ProcessStartInfo info, string program, string missing)
    {
        try
        {
            return Process.Start(info)
                ?? throw new DeviceActionException(missing, program);
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception
            or InvalidOperationException or PlatformNotSupportedException)
        {
            throw new DeviceActionException(missing, program);
        }
    }

    /// <summary>What one request from the script is answered with.</summary>
    private static JsonObject Reply(IScriptHost host, JsonObject request) =>
        request["call"]?.GetValue<string>() switch
        {
            "get" => new JsonObject
            {
                ["ok"] = true,
                ["value"] = host.Read(Text(request, "name")) is { } value
                    ? JsonValues.Node(value)
                    : null,
            },
            "set" => Written(host, request),
            "step" => new JsonObject
            {
                ["ok"] = true,
                ["value"] = host.RunStep(Text(request, "id")),
            },
            "print" => Printed(host, request),
            var call => new JsonObject { ["ok"] = false, ["error"] = call },
        };

    private static JsonObject Written(IScriptHost host, JsonObject request)
    {
        host.Write(Text(request, "name"), JsonValues.FromJson(request["value"]));
        return new JsonObject { ["ok"] = true };
    }

    private static JsonObject Printed(IScriptHost host, JsonObject request)
    {
        host.Print(Text(request, "text"));
        return new JsonObject { ["ok"] = true };
    }

    private static string Text(JsonObject request, string field)
        => request[field]?.GetValue<string>() ?? string.Empty;

    /// <summary>
    /// Hands one answer back. A pipe whose other end has gone is not a failure of its own: the
    /// script that was listening to it has already ended, and what became of it is its exit code.
    /// </summary>
    private static void Answer(NamedPipeServerStream pipe, JsonObject reply)
    {
        try
        {
            var written = Encoding.UTF8.GetBytes(reply.ToJsonString() + "\n");
            pipe.Write(written, 0, written.Length);
            pipe.Flush();
        }
        catch (Exception failure) when (failure is IOException or ObjectDisposedException
            or InvalidOperationException)
        {
            // Nothing to say to nobody.
        }
    }

    /// <summary>Reads a stream a line at a time and puts what it finds in the queue.</summary>
    private static void Pump(StreamReader reader, ConcurrentQueue<string> into)
    {
        try
        {
            while (reader.ReadLine() is { } line)
            {
                into.Enqueue(line);
            }
        }
        catch (Exception failure) when (failure is IOException or ObjectDisposedException)
        {
            // A stream that closed under the reader is the script having ended.
        }
    }

    /// <summary>Reads the requests a script makes until the pipe closes.</summary>
    private static void Listen(NamedPipeServerStream pipe, BlockingCollection<JsonObject> into,
        TaskCompletionSource<bool> reached)
    {
        try
        {
            pipe.WaitForConnection();
            reached.TrySetResult(true);
            using var reader = new StreamReader(pipe, NoMark, false, 4096, leaveOpen: true);
            while (reader.ReadLine() is { } line)
            {
                if (JsonNode.Parse(line) is JsonObject request)
                {
                    into.Add(request);
                }
            }
        }
        catch (Exception failure) when (failure is IOException or ObjectDisposedException
            or InvalidOperationException or System.Text.Json.JsonException)
        {
            // The script closed the pipe, or never opened one: either way nothing more is coming.
        }
        finally
        {
            into.CompleteAdding();
        }
    }

    /// <summary>
    /// Hands over everything a stream has said since the last look, keeping a copy of it: the lines
    /// go into the run log as they arrive, and the whole of what was said is what the step hands
    /// back afterwards, which is what a script that failed is read through.
    /// </summary>
    private static void Drain(ConcurrentQueue<string> lines, List<string> kept, Action<string> said)
    {
        while (lines.TryDequeue(out var line))
        {
            kept.Add(line);
            said(line);
        }
    }

    /// <summary>Stops a script and everything it started.</summary>
    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(StopWaitMs);
            }
        }
        catch (Exception failure) when (failure is InvalidOperationException
            or System.ComponentModel.Win32Exception or NotSupportedException
            or AggregateException)
        {
            // A program that has just gone is exactly what this was for.
        }
    }
}
