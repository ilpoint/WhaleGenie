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
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

public sealed partial class MacroRunner
{
    // ------------------------------------------------------------------- devices

    /// <summary>
    /// The input device that delivers this step's input, chosen by the step's own settings: in
    /// front by default, posted at one window when the step names one, or through a driver.
    /// </summary>
    private IInputDevice Input(ExecutableStep step) => _devices.Inputs.For(Route(step));
}
