using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Lucide.Avalonia;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

// The lines the menu shows about the rule in play, and the wheel over it.
public sealed partial class MacroMenuWindow
{
    private string CurrentRuleTip()
        => _canUseForeground
            ? Strings.Format("Menu.AddWindowRule", _foregroundProcess)
            : Strings.Get("Menu.NoForeground");

    private string BlockTip()
        => _canUseForeground
            ? Strings.Format("Menu.BlockWindow", _foregroundProcess)
            : Strings.Get("Menu.NoForeground");

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var commonPoint = e.GetPosition(_commonGrid);
        var contextPoint = e.GetPosition(_contextGrid);
        var overCommon = commonPoint.X >= 0 && commonPoint.Y >= 0
            && commonPoint.X <= _commonGrid.Bounds.Width
            && commonPoint.Y <= _commonGrid.Bounds.Height;
        var overContext = contextPoint.X >= 0 && contextPoint.Y >= 0
            && contextPoint.X <= _contextGrid.Bounds.Width
            && contextPoint.Y <= _contextGrid.Bounds.Height;
        if (!overCommon && !overContext)
        {
            return;
        }

        if (e.Delta.Y > 0)
        {
            if (overCommon)
            {
                _commonPage = Math.Max(0, _commonPage - 1);
            }
            else
            {
                _contextPage = Math.Max(0, _contextPage - 1);
            }
        }
        else if (e.Delta.Y < 0)
        {
            if (overCommon)
            {
                _commonPage = Math.Min(_layout.CommonPages.Count - 1, _commonPage + 1);
            }
            else
            {
                _contextPage = Math.Min(_layout.ContextPages.Count - 1, _contextPage + 1);
            }
        }
        _selected = null;
        RenderLayout();
        e.Handled = true;
    }
}
