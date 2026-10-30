using Avalonia;
using Avalonia.Controls;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// `Border.Row` 的两个通道必须能**同时**显示：`.Picked` 改的是底，`.DropTarget` 改的是边。
///
/// 这条用例防的不是"今天没写对"，是"以后有人顺手给其中一个加个 `Background`"——
/// 一旦两者都改底，拖动到已选中的行上时，就只剩一个状态看得见，而**它看起来仍然正常**。
/// </summary>
public class RowChannelsTests
{
    [Fact]
    public void Picked_and_DropTarget_both_show_at_once()
    {
        Ui.Run(() =>
        {
            var host = new Window();
            var row = new Border { Classes = { "Row", "Picked", "DropTarget" } };
            host.Content = row;
            host.Show();

            var background = row.Background;
            var borderBrush = row.BorderBrush;
            var thickness = row.BorderThickness;

            // 底来自 `.Picked`，边来自 `.DropTarget`：两个都在，说明互不覆盖。
            Assert.NotNull(background);
            Assert.NotNull(borderBrush);

            // 左 3px 是 `.Picked` 的"整行亮起来"，另三边是 `.DropTarget` 的 1px 圈。
            Assert.Equal(new Thickness(3, 1, 1, 1), thickness);

            // 只有 Picked 时：底在、边回到透明。
            row.Classes.Remove("DropTarget");
            Assert.NotNull(row.Background);
            Assert.Equal(new Thickness(3, 1, 1, 1), row.BorderThickness);

            // 只有 DropTarget 时：边在、厚度回到静息的 1px。
            row.Classes.Remove("Picked");
            Assert.NotNull(row.BorderBrush);
            Assert.Equal(new Thickness(1), row.BorderThickness);

            host.Close();
        });
    }
}
