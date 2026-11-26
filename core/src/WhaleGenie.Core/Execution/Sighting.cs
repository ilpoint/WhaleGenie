using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// One thing a detection model found: the box it drew with the score it gave it, the class number
/// the model uses, and the name of that class when the model writes names down. A sighting is a hit
/// as well — it is counted, ordered and handed over the same way — with the two extra things a
/// model knows and a picture search does not.
/// </summary>
internal sealed record Sighting(ImageMatch Match, int ClassId, string Label);
