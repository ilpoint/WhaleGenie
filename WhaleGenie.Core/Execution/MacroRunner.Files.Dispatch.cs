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
        => ReadText(step, "separatorText").Trim().Length > 0
            ? ReadText(step, "separatorText")
            : ReadText(step, "separator");


    /// <summary>
    /// The file actions, each answered by the method written for it. Step types of
    /// other families are not this dispatch's to answer.
    /// </summary>
    private async Task<Signal> DispatchFile(ExecutableStep step, int depth,
        CancellationToken token)
    {
        switch (step.Type)
        {
            case "file.readText":
                ReadTextFile(step, depth);
                return Signal.Normal;
            case "file.writeText":
                WriteTextFile(step, depth);
                return Signal.Normal;
            case "file.appendLog":
                AppendLog(step, depth);
                return Signal.Normal;
            case "file.zip":
                ZipFolder(step, depth);
                return Signal.Normal;
            case "file.exists":
                FileExists(step, depth);
                return Signal.Normal;
            case "file.delete":
                DeleteFile(step, depth);
                return Signal.Normal;
            case "file.copy":
                CopyFile(step, depth);
                return Signal.Normal;
            case "file.move":
                MoveFile(step, depth);
                return Signal.Normal;
            case "file.createFolder":
                CreateFolder(step, depth);
                return Signal.Normal;
            case "file.deleteFolder":
                DeleteFolder(step, depth);
                return Signal.Normal;
            case "file.path":
                PathPart(step, depth);
                return Signal.Normal;
            case "file.unzip":
                UnzipFile(step, depth);
                return Signal.Normal;
            case "file.listFiles":
                ListFiles(step, depth);
                return Signal.Normal;
            case "file.readJson":
                ReadJson(step, depth);
                return Signal.Normal;
            case "file.writeJson":
                WriteJson(step, depth);
                return Signal.Normal;
            case "file.readCsv":
                ReadCsv(step, depth);
                return Signal.Normal;
            case "file.writeCsv":
                WriteCsv(step, depth);
                return Signal.Normal;
            case "file.saveVariables":
                SaveVariables(step, depth);
                return Signal.Normal;
            case "file.loadVariables":
                LoadVariables(step, depth);
                return Signal.Normal;

            // ----------------------------------------------------------------- data
            default:
                throw new StepFailure(RunKeys.Unsupported, step.Type);
        }
    }

    /// <summary>
    /// The data actions, each answered by the method written for it. Step types of
    /// other families are not this dispatch's to answer.
    /// </summary>
    private async Task<Signal> DispatchData(ExecutableStep step, int depth,
        CancellationToken token)
    {
        // 变量与列表动作在目录里归「数据与变量」，回答它们的代码仍在控制流那一支。分类是给人
        // 看的（动作库按它分栏），不该决定哪段代码回答它：这里把 control.* 交回去。
        if (step.Type.StartsWith("control.", StringComparison.Ordinal))
        {
            return await DispatchControl(step, depth, token).ConfigureAwait(false);
        }

        switch (step.Type)
        {
            case "data.base64Encode":
                EncodeBase64(step, depth);
                return Signal.Normal;
            case "data.base64Decode":
                DecodeBase64(step, depth);
                return Signal.Normal;
            case "data.hash":
                HashText(step, depth);
                return Signal.Normal;

            // -------------------------------------------------------------- clipboard
            default:
                throw new StepFailure(RunKeys.Unsupported, step.Type);
        }
    }
}
