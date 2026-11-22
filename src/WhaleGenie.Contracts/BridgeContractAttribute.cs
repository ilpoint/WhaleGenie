using System;

namespace WhaleGenie.Contracts;

/// <summary>
/// Marks one record as part of the wire contract. The TypeScript generator emits exactly these
/// types, so ports and other helpers can live in this assembly without leaking into the page.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum)]
public sealed class BridgeContractAttribute : Attribute
{
}
