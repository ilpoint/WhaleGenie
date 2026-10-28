namespace WhaleGenie.Models;

/// <summary>
/// The names a step problem is known by. They are constants rather than literals typed at each call
/// because two places have to agree on them: the check that reports one, and the list that says
/// which of them blocks a save. A typo in a literal does not fail — it quietly produces a problem
/// nothing recognises, which is the one mistake this file exists to make impossible.
/// </summary>
/// <remarks>
/// The value is the name, so a code read in a log or in an MCP answer matches the constant here.
/// </remarks>
public static class MacroProblemCode
{
    public const string ArgumentType = nameof(ArgumentType);
    public const string BadExpression = nameof(BadExpression);
    public const string BadOption = nameof(BadOption);
    public const string BadResultName = nameof(BadResultName);
    public const string BadUserFailureKey = nameof(BadUserFailureKey);
    public const string BreakOutsideLoop = nameof(BreakOutsideLoop);
    public const string CaseOutsideSwitch = nameof(CaseOutsideSwitch);
    public const string ContinueOutsideLoop = nameof(ContinueOutsideLoop);
    public const string DuplicateArgument = nameof(DuplicateArgument);
    public const string DuplicateResultKey = nameof(DuplicateResultKey);
    public const string DuplicateResultName = nameof(DuplicateResultName);
    public const string EmptySwitch = nameof(EmptySwitch);
    public const string ExclusiveRowColumn = nameof(ExclusiveRowColumn);
    public const string GivenWithValue = nameof(GivenWithValue);
    public const string GlobalMustExist = nameof(GlobalMustExist);
    public const string HiddenStep = nameof(HiddenStep);
    public const string LoopIndexOutsideLoop = nameof(LoopIndexOutsideLoop);
    public const string LoopVariableShadowed = nameof(LoopVariableShadowed);
    public const string MacroNotFound = nameof(MacroNotFound);
    public const string MissingArgument = nameof(MissingArgument);
    public const string MissingCondition = nameof(MissingCondition);
    public const string MissingField = nameof(MissingField);
    public const string MissingRowColumn = nameof(MissingRowColumn);
    public const string NoMacro = nameof(NoMacro);
    public const string NoPlace = nameof(NoPlace);
    public const string NotARecord = nameof(NotARecord);
    public const string PlaceAndNumbers = nameof(PlaceAndNumbers);
    public const string RecordInText = nameof(RecordInText);
    public const string RecordWhereNumber = nameof(RecordWhereNumber);
    public const string ReservedVariable = nameof(ReservedVariable);
    public const string ScriptInput = nameof(ScriptInput);
    public const string SharedBrowser = nameof(SharedBrowser);
    public const string SharedInput = nameof(SharedInput);
    public const string UndeclaredActionResult = nameof(UndeclaredActionResult);
    public const string UndeclaredArgument = nameof(UndeclaredArgument);
    public const string UndeclaredResult = nameof(UndeclaredResult);
    public const string UndefinedVariable = nameof(UndefinedVariable);
    public const string UnguardedRecursion = nameof(UnguardedRecursion);
    public const string UnknownAction = nameof(UnknownAction);
    public const string UnknownField = nameof(UnknownField);
    public const string UnknownParameter = nameof(UnknownParameter);
    public const string UnknownRowColumn = nameof(UnknownRowColumn);
    public const string WaitWithoutTimeout = nameof(WaitWithoutTimeout);
    public const string WindowBounds = nameof(WindowBounds);
    public const string WrongStepKind = nameof(WrongStepKind);
}
