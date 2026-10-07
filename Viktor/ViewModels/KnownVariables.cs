using System;
using System.Collections.Generic;
using System.Linq;
using Viktor.Core.Expressions;

namespace Viktor.ViewModels;

/// <summary>
/// Answers for every name the macro knows with a plain value, so an expression can be
/// checked while the macro is still being written and nothing has run yet.
/// </summary>
internal sealed class KnownVariables(IReadOnlyList<string> names) : IVariableResolver
{
    public bool TryGet(string name, out Value value)
    {
        if (names.Any(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)))
        {
            value = Value.FromNumber(0);
            return true;
        }

        value = Value.Null;
        return false;
    }
}
