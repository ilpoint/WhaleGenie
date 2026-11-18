using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Recording;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Devices;

// Reading writing off the screen.
/// <summary>Reading text off the screen.</summary>
public interface IOcrDevice
{
    /// <summary>Every piece of text in the frame, in reading order.</summary>
    IReadOnlyList<TextSpan> Recognize(ImageFrame frame);
}
