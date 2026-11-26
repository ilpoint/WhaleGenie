using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Execution;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The machine family on its own: the fake machine answers what it is asked and records what it was
/// told, so each step's reading and writing can be checked without touching this computer.
/// </summary>
public class SystemActionTests
{
    [Fact]
    public async Task A_machine_fact_is_read_into_the_result()
    {
        var context = new FakeRunContext();
        context.Machine.System.Values["machineName"] = "the fake machine";

        await Run(context, "system.info", ("field", "machineName"));

        Assert.Equal("the fake machine", context.Written["value"].AsText());
    }

    [Fact]
    public async Task Listing_screens_hands_over_each_display_with_its_middle()
    {
        var context = new FakeRunContext();

        await Run(context, "screen.list");

        var displays = context.Written["displays"].Items;
        var only = Assert.Single(displays);
        Assert.Equal(50, only.Fields.First(field => field.Name == "x").Value.AsNumber());
        Assert.Equal(25, only.Fields.First(field => field.Name == "y").Value.AsNumber());
        Assert.Equal("0,0,100,50",
            only.Fields.First(field => field.Name == "box").Value.AsText());
    }

    [Fact]
    public async Task Setting_the_volume_leaves_the_level_it_ended_at()
    {
        var context = new FakeRunContext();

        await Run(context, "system.volume", ("volumeAction", "set"), ("levelPercent", "30"));

        Assert.Equal(30, context.Machine.System.SpeakerVolume);
        Assert.Equal(30, context.Written["volume"].AsNumber());
    }

    private static Task<Signal> Run(FakeRunContext context, string type,
        params (string Name, string Text)[] fields)
        => SystemActions.Map[type](context, Step(type, fields), 0, CancellationToken.None);

    private static ExecutableStep Step(string type, params (string Name, string Text)[] fields)
        => new()
        {
            Type = type,
            Parameters = [.. fields.Select(field => Fields.Text(field.Name, field.Text))],
        };
}
