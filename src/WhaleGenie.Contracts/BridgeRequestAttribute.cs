using System;

namespace WhaleGenie.Contracts;

/// <summary>
/// Marks a request record whose nullable fields may be left out. The generator writes them as
/// optional in TypeScript; required fields and the method shape stay checked.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class BridgeRequestAttribute : Attribute
{
}
