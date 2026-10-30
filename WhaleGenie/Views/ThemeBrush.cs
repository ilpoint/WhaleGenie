using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
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
    /// <summary>
    /// 取一支画笔的**快照**。**不随主题切换更新**——只用于"不能绑定的地方"：
    /// `Pen`、`DrawingContext`、几何填充这类需要一个 `IBrush` 对象本身的场合。
    ///
    /// **要设属性请用 <see cref="Assign"/>**（`DynamicResource`），那条路会跟着主题变。
    ///
    /// 查找必须带 `ActualThemeVariant`：令牌都在 `Tokens.axaml` 的 `ThemeDictionaries` 里，
    /// **不带 variant 的重载取不到它们**——这个函数为此静默失败过很久（返回 `null`，
    /// 而调用方的兜底把它盖住了，于是"窗有底色"看起来正常）。
    /// </summary>
    internal static IBrush? Of(string key)
        => Application.Current is { } app
            && app.TryFindResource(key, app.ActualThemeVariant, out var found)
            && found is IBrush brush
                ? brush
                : null;

    /// <summary>
    /// 把一个令牌**绑到属性上**（`DynamicResource` 语义）：和 axaml 窗走同一条路，
    /// **换主题会跟着变**。设属性的场合一律用这个，不要用 <see cref="Of"/>。
    /// </summary>
    internal static void Assign(AvaloniaObject target, AvaloniaProperty property, string key)
        => target.Bind(property, new DynamicResourceExtension(key));
}
