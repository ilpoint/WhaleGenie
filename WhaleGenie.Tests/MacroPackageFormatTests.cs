using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.Tests;

/// <summary>
/// A package is the macro file, so a package written with the old field names is refused rather
/// than read as if its parameters still meant something. There is no migration branch: the version
/// has to be the one this build writes.
/// </summary>
public partial class MacroPackageFormatTests
{
}
