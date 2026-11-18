using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The pretend detection model: what it finds, and every look the engine asked for.
/// </summary>
internal sealed class FakeDetector(FakeDesk desk) : IDetectorDevice
{
    /// <summary>What the detection model in the fake reports, in the order it reports them.</summary>
    public List<DetectionHit> Detections { get; } = [];

    /// <summary>How many looks happen before the model starts finding anything.</summary>
    public int DetectAfter { get; set; } = 1;

    /// <summary>How many times the model was asked to look.</summary>
    public int Looks { get; private set; }

    /// <summary>The model files the engine asked for, in the order it asked.</summary>
    public List<string> Models { get; } = [];

    /// <summary>What the engine asked the model to find, in the order it asked.</summary>
    public List<DetectionQuery> DetectionQueries { get; } = [];

    /// <summary>
    /// What a look hands back, for a check whose answer depends on the query — a model that finds
    /// something only once the engine has been told to look for it.
    /// </summary>
    public Func<ImageFrame, DetectionQuery, IReadOnlyList<DetectionHit>>? DetectionAnswers
    { get; set; }

    public IReadOnlyList<DetectionHit> Detect(string modelPath, ImageFrame frame, DetectionQuery query)
    {
        Looks++;
        Models.Add(modelPath);
        DetectionQueries.Add(query);
        desk.Note($"detect {Path.GetFileName(modelPath)} {query.ConfidencePercent:0} {query.Limit}");

        var hits = DetectionAnswers?.Invoke(frame, query) ?? Detections;
        return Looks >= DetectAfter ? [.. hits.Take(query.Limit)] : [];
    }
}
