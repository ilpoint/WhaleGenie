namespace WhaleGenie.Contracts;

/// <summary>The two grants a client works under, the configuration block and the audit file.</summary>
[BridgeContract]
public sealed record McpState(bool AllowControl, bool AllowRun, string Config, string AuditFile);
