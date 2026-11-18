using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using OpenCvSharp;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using Xunit;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The arithmetic of detection: fitting a region into the square a model reads, reading what the
/// model answered, and putting the boxes it drew back where they were. None of it needs a model or
/// a screen, which is the point of keeping it apart from both.
/// </summary>
public class YoloDetectTests
{
    /// <summary>An answer shaped the way a YOLOv8 or YOLO11 export gives one: 1 x (4 + classes) x guesses.</summary>
    private static Mat Output(int classes, int guesses, out float[] values)
    {
        values = new float[(4 + classes) * guesses];
        return new Mat([1, 4 + classes, guesses], MatType.CV_32F);
    }

    /// <summary>Writes one guess: the box, and the score it gave each class.</summary>
    private static void Guess(float[] values, int guesses, int column, float centreX, float centreY,
        float width, float height, params float[] scores)
    {
        values[column] = centreX;
        values[guesses + column] = centreY;
        values[(2 * guesses) + column] = width;
        values[(3 * guesses) + column] = height;
        for (var index = 0; index < scores.Length; index++)
        {
            values[((4 + index) * guesses) + column] = scores[index];
        }
    }

    /// <summary>The answer as the device reads it: the numbers copied into the picture.</summary>
    private static Mat Given(Mat output, float[] values)
    {
        Marshal.Copy(values, 0, output.Data, values.Length);
        return output;
    }

    [Fact]
    public void A_region_is_fitted_into_a_square_without_changing_its_shape()
    {
        // A wide region is scaled until its width fills the square, and the room left over is a band
        // at the bottom; a tall one is the other way round. Neither is stretched.
        var wide = YoloDetect.Fitted(800, 600, 640);
        Assert.Equal(640, wide.Input);
        Assert.Equal(0.8, wide.Scale, 6);
        Assert.Equal(0, wide.PadX);
        Assert.Equal(80, wide.PadY);

        var tall = YoloDetect.Fitted(600, 800, 640);
        Assert.Equal(80, tall.PadX);
        Assert.Equal(0, tall.PadY);
    }

    [Fact]
    public void The_square_a_model_is_fed_is_the_size_the_step_asked_for()
    {
        var fit = YoloDetect.Fitted(100, 50, 320);
        using var region = new Mat(50, 100, MatType.CV_8UC3, new Scalar(10, 20, 30));
        using var square = YoloDetect.Padded(region, fit);

        Assert.Equal(320, square.Width);
        Assert.Equal(320, square.Height);

        // The band left over is the grey every YOLO converter pads with, not black.
        Assert.Equal(114, square.At<Vec3b>(300, 300).Item0);
    }

    [Fact]
    public void The_picture_lands_in_the_square_where_the_box_arithmetic_counts_from()
    {
        // 800×600 进 640 的方形：横着填满，竖着上下各留 80 行灰。把框放回区域时减掉的正是这 80 行，
        // 所以画面必须真的落在第 80 行上；贴着上边放、却按中间算，框会整块往上偏 80 个模型像素
        // （2560×1440 的整屏上就是 560 个屏幕像素，东西认出来了也框在别处）。
        var wide = YoloDetect.Fitted(800, 600, 640);
        using (var region = new Mat(600, 800, MatType.CV_8UC3, new Scalar(7, 9, 11)))
        using (var square = YoloDetect.Padded(region, wide))
        {
            Assert.Equal(114, square.At<Vec3b>(wide.PadY - 2, 300).Item0);
            Assert.Equal(7, square.At<Vec3b>(wide.PadY + 2, 300).Item0);
            Assert.Equal(7, square.At<Vec3b>(wide.Input - wide.PadY - 3, 300).Item0);
            Assert.Equal(114, square.At<Vec3b>(wide.Input - wide.PadY + 2, 300).Item0);

            // 宽的那两边顶到边：0.8 倍缩放下来正好填满，一行灰都不该有。
            Assert.Equal(7, square.At<Vec3b>(300, 2).Item0);
            Assert.Equal(7, square.At<Vec3b>(300, wide.Input - 3).Item0);
        }

        // 竖着的反过来：左右各留 80 列。
        var tall = YoloDetect.Fitted(600, 800, 640);
        using (var region = new Mat(800, 600, MatType.CV_8UC3, new Scalar(7, 9, 11)))
        using (var square = YoloDetect.Padded(region, tall))
        {
            Assert.Equal(114, square.At<Vec3b>(300, tall.PadX - 2).Item0);
            Assert.Equal(7, square.At<Vec3b>(300, tall.PadX + 2).Item0);
            Assert.Equal(7, square.At<Vec3b>(300, tall.Input - tall.PadX - 3).Item0);
            Assert.Equal(114, square.At<Vec3b>(300, tall.Input - tall.PadX + 2).Item0);
        }
    }

    [Fact]
    public void A_box_is_put_back_where_it_was_in_the_region()
    {
        // The region is 800x600 in a 640 square: everything the model says is 0.8 of a screen pixel
        // and sits 80 rows above where it really is.
        var fit = YoloDetect.Fitted(800, 600, 640);
        var box = new Box(400, 320, 160, 120, 0.9f, 0);

        var (corner, size) = YoloDetect.Placed(box, fit, 800, 600);

        Assert.Equal(new ScreenPoint(400, 225), corner);
        Assert.Equal(new ScreenSize(200, 150), size);
    }

    [Fact]
    public void A_box_that_runs_off_the_region_comes_back_inside_it()
    {
        // A 640x640 region goes into the square as it stands, one screen pixel to one model pixel,
        // which keeps the arithmetic of this check plain.
        var fit = YoloDetect.Fitted(640, 640, 640);

        var overTheCorner = YoloDetect.Placed(new Box(-40, -40, 200, 200, 0.8f, 0), fit, 640, 640);
        Assert.Equal(new ScreenPoint(0, 0), overTheCorner.Corner);
        Assert.Equal(new ScreenSize(60, 60), overTheCorner.Size);

        var overTheFarEdge = YoloDetect.Placed(new Box(660, 660, 200, 200, 0.8f, 0), fit, 640, 640);
        Assert.Equal(new ScreenPoint(560, 560), overTheFarEdge.Corner);
        Assert.Equal(new ScreenSize(80, 80), overTheFarEdge.Size);
    }

    [Fact]
    public void Every_guess_sure_enough_is_read_with_the_class_it_names()
    {
        using var output = Output(2, 3, out var values);
        Guess(values, 3, 0, 100, 100, 20, 20, 0.9f, 0.1f);
        Guess(values, 3, 1, 200, 200, 20, 20, 0.1f, 0.6f);
        Guess(values, 3, 2, 300, 300, 20, 20, 0.2f, 0.2f);
        Given(output, values);

        var found = YoloDetect.Boxes(output, 4 + 2, 3, 25, 45, null, 10);

        Assert.Equal(2, found.Count);
        Assert.Equal(0, found[0].ClassId);
        Assert.Equal(0.9f, found[0].Score, 6);
        Assert.Equal(1, found[1].ClassId);

        // Nothing is left of the guess that scored 0.2 for both classes: it is below the bar.
        Assert.DoesNotContain(found, box => box.CentreX == 300);
    }

    [Fact]
    public void Two_boxes_over_one_thing_of_the_same_class_are_kept_once()
    {
        using var output = Output(1, 2, out var values);
        Guess(values, 2, 0, 100, 100, 40, 40, 0.9f);
        Guess(values, 2, 1, 104, 104, 40, 40, 0.7f);
        Given(output, values);

        var found = YoloDetect.Boxes(output, 4 + 1, 2, 25, 45, null, 10);

        var kept = Assert.Single(found);
        Assert.Equal(0.9f, kept.Score, 6);
    }

    [Fact]
    public void Two_things_over_each_other_of_different_classes_are_both_kept()
    {
        // A person standing in front of a bicycle is two boxes on the same pixels, and both are
        // there: what is dropped is a second box of the same class, not a second thing.
        using var output = Output(2, 2, out var values);
        Guess(values, 2, 0, 100, 100, 40, 40, 0.9f, 0.1f);
        Guess(values, 2, 1, 100, 100, 40, 40, 0.1f, 0.8f);
        Given(output, values);

        var found = YoloDetect.Boxes(output, 4 + 2, 2, 25, 45, null, 10);

        Assert.Equal(2, found.Count);
        Assert.Equal([0, 1], found.Select(box => box.ClassId));
    }

    [Fact]
    public void Only_the_classes_the_step_wants_are_read()
    {
        using var output = Output(2, 2, out var values);
        Guess(values, 2, 0, 100, 100, 20, 20, 0.9f, 0.1f);
        Guess(values, 2, 1, 200, 200, 20, 20, 0.1f, 0.8f);
        Given(output, values);

        var found = YoloDetect.Boxes(output, 4 + 2, 2, 25, 45, [1], 10);

        var kept = Assert.Single(found);
        Assert.Equal(1, kept.ClassId);
    }

    [Fact]
    public void Only_as_many_guesses_are_read_as_the_step_asked_for()
    {
        using var output = Output(1, 3, out var values);
        Guess(values, 3, 0, 100, 100, 20, 20, 0.5f);
        Guess(values, 3, 1, 200, 200, 20, 20, 0.7f);
        Guess(values, 3, 2, 300, 300, 20, 20, 0.9f);
        Given(output, values);

        var found = YoloDetect.Boxes(output, 4 + 1, 3, 25, 45, null, 2);

        Assert.Equal(2, found.Count);
        Assert.Equal(300, found[0].CentreX);
        Assert.Equal(200, found[1].CentreX);
    }

    [Fact]
    public void A_shape_this_step_cannot_read_is_turned_away()
    {
        // What this step reads: one picture, the four sides of a box plus a score per class, and a
        // column per guess.
        using var known = new Mat([1, 84, 8400], MatType.CV_32F);
        Assert.True(YoloDetect.Recognised(known, out var channels, out var candidates));
        Assert.Equal(84, channels);
        Assert.Equal(8400, candidates);
        Assert.Equal("1x84x8400", YoloDetect.Written(known));

        // What an older YOLO answers with: the guesses on the shorter side, and a score for the
        // presence of something among the channels. It is turned away rather than misread.
        using var older = new Mat([1, 25200, 85], MatType.CV_32F);
        Assert.False(YoloDetect.Recognised(older, out _, out _));
        Assert.Equal("1x25200x85", YoloDetect.Written(older));

        // And an answer that is not a picture of guesses at all.
        using var plain = new Mat(10, 10, MatType.CV_32F);
        Assert.False(YoloDetect.Recognised(plain, out _, out _));

        using var oneClass = new Mat([1, 5, 8400], MatType.CV_32F);
        Assert.True(YoloDetect.Recognised(oneClass, out _, out _));
    }

    [Fact]
    public void The_names_of_a_label_file_are_read_in_the_order_they_are_written()
    {
        var names = YoloDetect.Names("person\nbicycle\r\n\r\ncar\n");

        Assert.Equal(["person", "bicycle", "car"], names);
    }

    [Fact]
    public void A_class_named_by_the_label_file_is_put_together_with_its_number()
    {
        IReadOnlyList<string> names = ["person", "bicycle", "car"];

        Assert.Equal([2], YoloDetect.Wanted(["car"], names, out var none));
        Assert.Empty(none);

        // A name is held up against the file ignoring case and the spaces around both, and a row
        // written as a number is that class whatever the file says.
        Assert.Equal([1, 0], YoloDetect.Wanted([" BICYCLE ", "0"], names, out none));
        Assert.Empty(none);

        // No rows at all is every class, which is what a step that named none asks for.
        Assert.Null(YoloDetect.Wanted([], names, out none));
        Assert.Null(YoloDetect.Wanted(null, names, out none));

        // A name the file does not have comes back, so the step is told rather than left looking for
        // something that could never be found.
        Assert.Null(YoloDetect.Wanted(["truck"], names, out var unknown));
        Assert.Equal("truck", unknown);

        Assert.Equal("person, bicycle, car", YoloDetect.Preview(names));
        Assert.EndsWith("…", YoloDetect.Preview([.. Enumerable.Range(0, 20).Select(n => $"c{n}")]));
    }
}
