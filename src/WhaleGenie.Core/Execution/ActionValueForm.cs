namespace WhaleGenie.Core.Execution;

/// <summary>
/// Which shapes one value field may hold. This is a fact about the field rather than about the
/// value currently in it: a cell that holds a bare name, one that holds a sentence with names in
/// it, and one that holds a formula are different questions the editor asks, even when all three
/// happen to be empty at the moment.
///
/// <para>
/// It stands apart from <see cref="ActionValueShape"/>, which says what kind of control draws the
/// field. A number and a path are different controls but both hold one whole value; a label and a
/// message are both text controls but only one of them may carry a name.
/// </para>
/// </summary>
public enum ActionValueForm
{
    /// <summary>Text only: no name may stand anywhere in it.</summary>
    Text,

    /// <summary>Text with names standing between its pieces.</summary>
    Run,

    /// <summary>One whole value: a literal, a name, a call, or an operation on them.</summary>
    Value,

    /// <summary>One bare variable name, with the identity it is bound to.</summary>
    Name,
}
