using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Recording;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Devices;

// Things a detection model finds on screen.
/// <summary>
/// Looking for things in a picture with a detection model the user brought along: what a step hands
/// over is a region, and what comes back is where each thing is and which kind of thing it is.
/// </summary>
/// <remarks>
/// The model is named by the path of the file it lives at rather than held by the device, because
/// which model a step uses is the step's own business: a macro that watches for one kind of thing
/// and another that watches for something else may each bring their own. Loading a model costs a
/// moment, so a device that has already loaded one keeps it until the program ends.
/// </remarks>
public interface IDetectorDevice
{
    /// <summary>
    /// Everything the model finds in <paramref name="frame"/>, surest first and at most
    /// <see cref="DetectionQuery.Limit"/> of them, with the coordinates counted from the corner of
    /// the picture that was handed over. A model the device cannot read or run is the caller's
    /// answer to give rather than a stack trace: it comes back saying which of the two happened.
    /// </summary>
    IReadOnlyList<DetectionHit> Detect(string modelPath, ImageFrame frame, DetectionQuery query);
}
