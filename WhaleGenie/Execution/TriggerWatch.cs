using System;

namespace WhaleGenie.Execution;

/// <summary>
/// What one armed trigger is waiting for, and when the thing it waits for was last looked at. The
/// run window shows these, because "my macro did not run" is nearly always answered by which of the
/// waiting things is not true.
/// </summary>
/// <param name="Macro">The macro's name.</param>
/// <param name="Looking">What it waits for, in the words the macro was written with.</param>
/// <param name="Matched">
/// Whether what it waits for is there right now, or null where that is not a question — a timer is
/// not "there", it is simply due at a time.
/// </param>
/// <param name="Checked">When this was last looked at.</param>
/// <param name="Next">
/// When it will be looked at again, or <see cref="DateTime.MinValue"/> where nothing is on a clock
/// — a file watch is told about a change rather than going to look for one.
/// </param>
/// <param name="Told">
/// Whether the machine says when this changes instead of it being looked for on a clock. A window
/// watched this way is caught even when it is only up for a moment.
/// </param>
public sealed record TriggerWatch(
    string Macro,
    string Looking,
    bool? Matched,
    DateTime Checked,
    DateTime Next,
    bool Told);
