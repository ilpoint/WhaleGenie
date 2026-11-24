using System;
using System.Reflection;

namespace WhaleGenie.Mcp;

/// <summary>
/// Says a tool only looks: nothing a call does changes the draft, the project or the machine, so it
/// leaves no line in the audit log. The rule the log is built on — a change is recorded, a look is
/// not — is stated here rather than repeated inside every tool body.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpReadOnlyAttribute : Attribute
{
}

/// <summary>
/// Says a call to this tool is worth a line in the audit log. A tool declares one of three things,
/// and the server refuses to offer a tool that declares none of them or two of them:
/// read-only (<see cref="McpReadOnlyAttribute"/>), recorded by the interceptor from
/// <see cref="Summary"/>, or recorded by the tool itself (<see cref="Handled"/>).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Summary"/> is a template rather than a sentence: <c>"run {name}"</c> names the macro
/// the call was about, and the interceptor fills the braces from the arguments of that one call. A
/// list or an object is written as its size, so a request that carries a whole macro cannot put one
/// in the log; a string is clipped, because a name is what a line is for and a paragraph is not.
/// </para>
/// <para>
/// <see cref="Handled"/> is for a tool that already writes its own line, either directly or through
/// the store it calls. The interceptor leaves those alone, so one call is one line.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpAuditedAttribute(string? summary = null) : Attribute
{
    /// <summary>The line this tool's calls are recorded with, <c>{argument}</c> standing for one.</summary>
    public string? Summary { get; } = summary;

    /// <summary>True when the tool writes its own line and the interceptor must not write another.</summary>
    public bool Handled { get; set; }
}

/// <summary>
/// What one tool method declares about its audit behaviour: that it only looks, that it writes its
/// own line, or that the interceptor writes one from a summary template.
/// </summary>
public sealed record McpToolDeclaration(bool ReadOnly, bool Handled, string? Summary)
{
    /// <summary>
    /// The declaration on <paramref name="method"/>. A method with neither attribute, with both, or
    /// with a declaration that cannot be acted on throws here rather than reaching a client: the
    /// server is built from this, so a tool added without saying how it is recorded fails loudly at
    /// the one moment somebody is looking.
    /// </summary>
    public static McpToolDeclaration Of(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);

        var readOnly = method.GetCustomAttribute<McpReadOnlyAttribute>() is not null;
        var audited = method.GetCustomAttribute<McpAuditedAttribute>();
        var name = Describe(method);

        if (readOnly && audited is not null)
        {
            throw new InvalidOperationException(
                $"{name} declares both [McpReadOnly] and [McpAudited]. A tool looks, or its calls are recorded, not both.");
        }

        if (!readOnly && audited is null)
        {
            throw new InvalidOperationException(
                $"{name} declares neither [McpReadOnly] nor [McpAudited]. Every tool says whether its calls leave a line.");
        }

        if (readOnly)
        {
            return new McpToolDeclaration(ReadOnly: true, Handled: false, Summary: null);
        }

        if (audited!.Handled)
        {
            if (!string.IsNullOrWhiteSpace(audited.Summary))
            {
                throw new InvalidOperationException(
                    $"{name} gives a summary template as well as Handled = true; that template would never be written.");
            }

            return new McpToolDeclaration(ReadOnly: false, Handled: true, Summary: null);
        }

        if (string.IsNullOrWhiteSpace(audited.Summary))
        {
            throw new InvalidOperationException(
                $"{name} is audited but has no summary template. Write one, such as [McpAudited(\"run {name}\")], or Handled = true if the tool writes its own line.");
        }

        return new McpToolDeclaration(ReadOnly: false, Handled: false, Summary: audited.Summary);
    }

    private static string Describe(MethodInfo method)
        => $"{method.DeclaringType?.FullName}.{method.Name}";
}
