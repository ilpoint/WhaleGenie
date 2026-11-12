using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;


namespace WhaleGenie.Tests;

// Lists and records: the family a narrow list asks about, one shape for a place, and the fields a record has.
public partial class ActionContractTests
{
    /// <summary>
    /// A place or a rectangle is written one way: the record field that takes it whole, or the
    /// numbers beside it. Every action that works at a place says so with a Point and every one
    /// that works on a rectangle says Rect, and no action still carries the switch that used to
    /// choose between the two.
    /// </summary>
    /// <summary>
    /// 每一张「还要像什么样」都要说清问的是哪一族：说不清时编辑器摆不出选项，说不认识的族时
    /// 也摆不出。这两个都当场报出来，不留到运行时。
    /// </summary>
    [Fact]
    public void Every_narrow_list_says_which_family_it_asks_about()
    {
        var narrowing = ActionCatalog.Definitions
            .SelectMany(action => action.Parameters)
            .Where(parameter => parameter.Kind is ActionParameterKind.Narrow)
            .ToList();

        Assert.NotEmpty(narrowing);
        foreach (var parameter in narrowing)
        {
            Assert.False(string.IsNullOrWhiteSpace(parameter.Semantics.NarrowOn), parameter.Name);
            Assert.NotEmpty(NarrowWay.For(parameter.NarrowFamily));
        }
    }

    [Fact]
    public void A_place_or_a_rectangle_is_declared_as_one_shape()
    {
        var retired = new[] { "useRecord", "record", "startFromRecord", "startRecord",
            "endFromRecord", "endRecord" };
        foreach (var action in ActionSemanticsCatalog.All)
        {
            foreach (var name in retired)
            {
                Assert.Null(action.Parameter(name));
            }
        }

        (string Key, string Field, ActionValueShape Shape)[] places =
        [
            ("input.mouseMove", "place", ActionValueShape.Point),
            ("input.mouseClick", "place", ActionValueShape.Point),
            ("input.mouseDoubleClick", "place", ActionValueShape.Point),
            ("input.mouseDown", "place", ActionValueShape.Point),
            ("input.mouseUp", "place", ActionValueShape.Point),
            ("input.mouseScroll", "place", ActionValueShape.Point),
            ("vision.getPixel", "place", ActionValueShape.Point),
            ("vision.waitColor", "place", ActionValueShape.Point),
            ("input.mouseDrag", "startPlace", ActionValueShape.Point),
            ("input.mouseDrag", "endPlace", ActionValueShape.Point),
            ("vision.capture", "rect", ActionValueShape.Rect),
            ("window.move", "rect", ActionValueShape.Rect),
        ];

        foreach (var (key, name, shape) in places)
        {
            var field = ActionSemanticsCatalog.Find(key)!.Parameter(name);
            Assert.NotNull(field);
            Assert.Equal(shape, field.Shape);
            Assert.Equal(ActionReadMode.WholeValue, field.ReadMode);
            Assert.False(field.Required);
        }

        // The same question is not asked twice: no place or rectangle action also carries a
        // second field of the other shape.
        foreach (var action in ActionSemanticsCatalog.All)
        {
            var shapes = action.Parameters.Select(parameter => parameter.Shape).ToList();
            Assert.False(shapes.Contains(ActionValueShape.Point) && shapes.Contains(ActionValueShape.Rect));
        }
    }

    /// <summary>
    /// One beat goes by one name. A field that says the same thing under a second name is a second
    /// question for the person filling the form in, and a second answer for the engine to read.
    /// </summary>
    [Fact]
    public void No_action_carries_a_second_name_for_the_same_beat()
    {
        foreach (var action in ActionSemanticsCatalog.All)
        {
            Assert.Null(action.Parameter("pollMs"));
        }
    }

    /// <summary>
    /// A record's fields are the catalogue's, not the engine's private shape: every record a step
    /// hands back says what it contains, every field name is one of the constants the file format
    /// and the record readers share, and only a record (or a list of them) carries fields at all.
    /// </summary>
    [Fact]
    public void Every_record_says_what_its_fields_are()
    {
        var constants = typeof(FieldNames)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var action in ActionSemanticsCatalog.All)
        {
            foreach (var result in action.Returns)
            {
                var field = $"{action.Key}.{result.Key}";
                if (result.Type == ValueKind.Record)
                {
                    Assert.NotEmpty(result.Fields);
                }
                else if (result.Type != ValueKind.List)
                {
                    Assert.Empty(result.Fields);
                }

                Assert.All(result.Fields, declared =>
                {
                    Assert.Contains(declared.Name, constants);
                    Assert.NotEqual(ValueKind.Image, declared.Type);
                });
            }
        }
    }
}
