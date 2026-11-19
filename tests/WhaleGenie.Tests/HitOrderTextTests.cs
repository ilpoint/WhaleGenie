using System;
using System.Collections.Generic;
using WhaleGenie.Localization;

namespace WhaleGenie.Tests;

/// <summary>
/// 命中排序的 ⓘ 读的就是这一段：它列着哪几档，用户就照着哪几档选。新加的档没写进去，界面等于少了
/// 一页说明；还写着已经删掉的档，就更糟。这条用例把三族各自的说明与它们现在给的档钉在一起。
/// </summary>
public class HitOrderTextTests
{
    private static string Hint(string action) => Strings.Get($"{action}.orderBy.hint");

    /// <summary>图片与检测：四向、综合评分、两种距离、随机，默认档说的是位置而不是相似度。</summary>
    [Theory]
    [InlineData("vision.findImage")]
    [InlineData("vision.waitImage")]
    [InlineData("vision.clickImage")]
    [InlineData("vision.detect")]
    [InlineData("vision.waitDetect")]
    [InlineData("vision.clickDetect")]
    public void The_picture_and_detection_hint_lists_the_orders(string action)
    {
        var hint = Hint(action);
        foreach (var word in new[] { "从左到右、从上到下", "综合评分", "离指定点最近", "离画面中心最近", "随机" })
        {
            Assert.Contains(word, hint, StringComparison.Ordinal);
        }

        // 默认档数的是位置：不把这句话说出来，读的人会以为它挑的是最像的那个。
        Assert.Contains("位置", hint, StringComparison.Ordinal);

        // 这两档已经被综合评分取代，说明里不该还留着。
        Assert.DoesNotContain("面积最大", hint, StringComparison.Ordinal);
        Assert.DoesNotContain("分数最高", hint, StringComparison.Ordinal);
    }

    /// <summary>检测的框会变，所以它的说明还得说出权重。</summary>
    [Theory]
    [InlineData("vision.detect")]
    [InlineData("vision.waitDetect")]
    [InlineData("vision.clickDetect")]
    public void The_detection_hint_says_what_the_weight_does(string action)
        => Assert.Contains("分数权重", Hint(action), StringComparison.Ordinal);

    /// <summary>文字读的是模型给的顺序，所以它只说自己有的四档。</summary>
    [Theory]
    [InlineData("ocr.findText")]
    [InlineData("ocr.clickText")]
    public void The_reading_hint_says_why_it_has_no_directions(string action)
    {
        var hint = Hint(action);
        Assert.Contains("按识别顺序", hint, StringComparison.Ordinal);
        Assert.Contains("分数最高", hint, StringComparison.Ordinal);
        Assert.Contains("面积最大", hint, StringComparison.Ordinal);
        Assert.DoesNotContain("从右到左", hint, StringComparison.Ordinal);
        Assert.DoesNotContain("综合评分", hint, StringComparison.Ordinal);
    }

    /// <summary>颜色只有一个像素的命中，说明里就是四向，不讲尺寸。</summary>
    [Theory]
    [InlineData("vision.findColor")]
    [InlineData("vision.findColors")]
    [InlineData("vision.clickColor")]
    public void The_colour_hint_lists_the_four_directions(string action)
    {
        var hint = Hint(action);
        foreach (var word in new[] { "从左到右、从上到下", "从右到左、从上到下", "从左到右、从下往上", "从右到左、从下往上" })
        {
            Assert.Contains(word, hint, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("综合评分", hint, StringComparison.Ordinal);
    }

    /// <summary>The English side says the same things, where a translated slip would go unnoticed.</summary>
    [Fact]
    public void The_english_hint_names_the_new_orders_too()
    {
        var picture = ActionEnglish.English["vision.findImage.orderBy.hint"];
        Assert.Contains("Best overall", picture, StringComparison.Ordinal);
        Assert.Contains("Closest to a point", picture, StringComparison.Ordinal);
        Assert.Contains("Closest to the middle", picture, StringComparison.Ordinal);

        var detection = ActionEnglish.English["vision.detect.orderBy.hint"];
        Assert.Contains("score weight", detection, StringComparison.Ordinal);

        var reading = ActionEnglish.English["ocr.findText.orderBy.hint"];
        Assert.Contains("As read", reading, StringComparison.Ordinal);
    }
}
