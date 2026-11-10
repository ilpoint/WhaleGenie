using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// The editor is a projection of the engine's action catalogue. These checks fail when the
/// display side grows a second copy of an engine fact, or when one of the two sides loses an
/// action, parameter or result the other still has.
/// </summary>
public partial class ActionContractTests
{
}
