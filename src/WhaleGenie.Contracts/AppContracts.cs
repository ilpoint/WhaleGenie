using System.Collections.Generic;

namespace WhaleGenie.Contracts;

/// <summary>The program's own picture: version, language and whether this is a debug build.</summary>
/// <remarks>The open project is not repeated here; <c>project.state</c> is the one shape that carries it.</remarks>
[BridgeContract]
public sealed record AppInfo(string Name, string Version, string Language, bool Debug);

/// <summary>What the About page cannot know by itself: where the project lives and its notice file.</summary>
[BridgeContract]
public sealed record AboutState(string ProjectUrl, string Notices);

/// <summary>Every word the page may draw, in the language the program is in.</summary>
[BridgeContract]
public sealed record StringsBundle(string Language, IReadOnlyDictionary<string, string> Entries);
