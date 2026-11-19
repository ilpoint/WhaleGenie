namespace WhaleGenie.Mcp;

/// <summary>Which mouse button a click or a drag presses.</summary>
public enum MouseButtonName
{
    Left,
    Right,
    Middle,
    Back,
    Forward,
}

/// <summary>Which way the wheel turns.</summary>
public enum ScrollDirection
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>Which part of a window the search text is held against.</summary>
public enum WindowPart
{
    Title,
    Process,
    Class,
    Handle,
}

/// <summary>How two pieces of text are compared.</summary>
public enum MatchMode
{
    Contains,
    StartsWith,
    EndsWith,
    Equals,
    Regex,
}

/// <summary>Which of the two workflows a client asks for.</summary>
public enum InstructionTopic
{
    Write,
    Debug,
}
