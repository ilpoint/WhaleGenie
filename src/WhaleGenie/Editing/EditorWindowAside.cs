using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using WhaleGenie.Core.Devices;
using WhaleGenie.Editing;
using WhaleGenie.Views;

namespace WhaleGenie.Editing;

/// <summary>The shell window's aside/restore behavior behind the editor's port.</summary>
internal sealed class EditorWindowAside(Func<Window?> owner) : IEditorWindowAside
{
    public async Task<IDisposable?> StepAsideAsync(IScreenDevice screen)
    {
        if (owner() is not { } window)
        {
            return null;
        }

        var aside = AppWindows.StepAside(window);
        await AppWindows.SettleAsync(screen);
        return aside;
    }
}
