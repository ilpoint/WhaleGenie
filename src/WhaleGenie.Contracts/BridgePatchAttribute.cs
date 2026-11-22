using System;

namespace WhaleGenie.Contracts;

/// <summary>
/// Marks a request record whose fields are all optional: an absent field means "leave it alone",
/// not "set it to null". The generator writes every property as optional in TypeScript.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class BridgePatchAttribute : Attribute
{
}
