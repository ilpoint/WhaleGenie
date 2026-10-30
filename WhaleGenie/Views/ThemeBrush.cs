using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WhaleGenie.Views;

/// <summary>
/// 从主题取一支画笔。**取不到就是 <c>null</c>，没有兜底色。**
///
/// 这五个纯代码窗原来各有一份私有 `Brush(key, fallback)`：资源取不到就用一个硬编码的
/// `Color.FromRgb(…)` 顶上。那种写法比"静默失效"更坏——**资源在的时候它不生效，换主题也不跟着变**，
/// 却让人以为"这里的颜色已经处理过"。同一个兜底值（`#202020`）被复制到了五个窗里。
///
/// 所以这里只有"取"没有"兜"：资源由主题保证存在，取不到就该当场暴露（`null`），
/// 而不是让一处看起来正常的界面用着一个没人再看一眼的值。
/// </summary>
internal static class ThemeBrush
{
    internal static IBrush? Of(string key)
        => Application.Current?.TryFindResource(key, out var found) == true ? found as IBrush : null;
}
