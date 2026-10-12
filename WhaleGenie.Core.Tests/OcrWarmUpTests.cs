using System;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Preloading the text recognition models: what it saves is the wait the first step that reads text
/// would otherwise pay, and what it costs is the models staying in memory. Warming up twice must not
/// load them twice, and a machine that cannot load them must not have the whole program fall over
/// something nobody asked for yet.
/// </summary>
public class OcrWarmUpTests
{
    [Fact]
    public void Warming_up_loads_the_models_once_and_swallows_what_loading_them_says()
    {
        var loads = 0;
        var reader = new PaddleOcrDevice(() =>
        {
            loads++;

            // A machine with no models to load, in the shape a reader would meet one in.
            throw new InvalidOperationException("no models here");
        });

        // Warming up is not the place to report a broken reader: the step that reads text is.
        reader.WarmUp();
        Assert.Equal(1, loads);

        // What loading said is remembered, so nothing is tried a second time.
        reader.WarmUp();
        Assert.Equal(1, loads);
    }
}
