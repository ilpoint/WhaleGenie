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

// The two dispatches: which file step and which data step goes where.
public sealed partial class MacroRunner
{
    /// <summary>The character between two CSV cells.</summary>
    /// <summary>
    /// What the step said separates its cells: the character written beside the picker when there
    /// is one, and otherwise the one that was picked — which may be <c>auto</c>, and then the file
    /// itself decides.
    /// </summary>
    private string Separator(ExecutableStep step)
        => Args(step).Text("separatorText").Trim().Length > 0
            ? Args(step).Text("separatorText")
            : Args(step).Text("separator");


    /// <summary>
    /// The file actions, each answered by the method written for it. A step of
    /// another family is not in this table.
    /// </summary>
    private Dictionary<string, ActionHandler> FileActions
        => _fileActions ??= new(StringComparer.Ordinal)
        {
            ["file.readText"] = (step, depth, token) =>
            {
                ReadTextFile(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.writeText"] = (step, depth, token) =>
            {
                WriteTextFile(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.appendLog"] = (step, depth, token) =>
            {
                AppendLog(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.zip"] = (step, depth, token) =>
            {
                ZipFolder(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.exists"] = (step, depth, token) =>
            {
                FileExists(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.delete"] = (step, depth, token) =>
            {
                DeleteFile(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.copy"] = (step, depth, token) =>
            {
                CopyFile(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.move"] = (step, depth, token) =>
            {
                MoveFile(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.createFolder"] = (step, depth, token) =>
            {
                CreateFolder(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.deleteFolder"] = (step, depth, token) =>
            {
                DeleteFolder(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.path"] = (step, depth, token) =>
            {
                PathPart(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.unzip"] = (step, depth, token) =>
            {
                UnzipFile(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.listFiles"] = (step, depth, token) =>
            {
                ListFiles(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.readJson"] = (step, depth, token) =>
            {
                ReadJson(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.writeJson"] = (step, depth, token) =>
            {
                WriteJson(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.readCsv"] = (step, depth, token) =>
            {
                ReadCsv(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.writeCsv"] = (step, depth, token) =>
            {
                WriteCsv(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.saveVariables"] = (step, depth, token) =>
            {
                SaveVariables(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.loadVariables"] = (step, depth, token) =>
            {
                LoadVariables(step, depth);
                return Task.FromResult(Signal.Normal);
            },
        };

    private Dictionary<string, ActionHandler>? _fileActions;


    /// <summary>
    /// The data actions, each answered by the method written for it. A step of
    /// another family is not in this table.
    /// </summary>
    private Dictionary<string, ActionHandler> DataActions
        => _dataActions ??= new(StringComparer.Ordinal)
        {
            ["data.base64Encode"] = (step, depth, token) =>
            {
                EncodeBase64(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["data.base64Decode"] = (step, depth, token) =>
            {
                DecodeBase64(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["data.hash"] = (step, depth, token) =>
            {
                HashText(step, depth);
                return Task.FromResult(Signal.Normal);
            },
        };

    private Dictionary<string, ActionHandler>? _dataActions;

}
