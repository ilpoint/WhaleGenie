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

namespace WhaleGenie.Core.Execution;

// Keys: reading a chord, sending a run of them, waiting for one, and driving the gamepad.
public sealed partial class MacroRunner
{
    private IReadOnlyList<string> Keys(ExecutableStep step)
        => StepArguments.Chords(Args(step).Text("keys"));

    /// <summary>The shortcut a clipboard step presses, written as one combination.</summary>
    private IReadOnlyList<string> Shortcut(ExecutableStep step)
        => StepArguments.Chords(Args(step).Text("shortcut"));
}
