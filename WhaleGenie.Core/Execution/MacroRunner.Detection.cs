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

public sealed partial class MacroRunner
{
    // ------------------------------------------------------------------ detection

    /// <summary>
    /// One thing a detection model found: where it is on the screen, how sure the model was, and
    /// which kind of thing it is — the number the model answers with and the name the label file
    /// gives that number, which is empty when the file does not name it.
    /// </summary>
    private sealed record Sighting(ImageMatch Match, int ClassId, string Label);

    /// <summary>What one look found: everything there was, and the one the step asked for.</summary>
    private sealed record FoundObjects(List<Sighting> Found, Sighting Match);
}
