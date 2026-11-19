using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;

namespace WhaleGenie.Views;

/// <summary>
/// One press that might turn into a drag. A press and a click are the same gesture until the
/// pointer moves, so the press is kept until it either becomes a drag or is let go: without that,
/// every button that can be dragged would stop being a button that can be clicked.
/// </summary>
internal sealed class DragStart
{
    /// <summary>How far the pointer has to move before a press counts as a drag.</summary>
    private const double Threshold = 4;

    private Visual? _owner;
    private PointerPressedEventArgs? _press;
    private string? _payload;
    private Point _origin;

    /// <summary>True while a remembered press has not yet become a drag or a plain click.</summary>
    public bool IsPending => _owner is not null && _press is not null && _payload is not null;

    /// <summary>Remembers a press that may become a drag, and what it would carry.</summary>
    public void Remember(Visual owner, PointerPressedEventArgs press, string payload)
    {
        _owner = owner;
        _press = press;
        _payload = payload;
        _origin = press.GetPosition(owner);
    }

    /// <summary>
    /// Carries on with a pointer that is moving: past a few pixels the press becomes a drag, which
    /// is what the platform hands to whatever is underneath.
    /// </summary>
    public async Task Moved(PointerEventArgs e, DataTransfer transfer)
    {
        if (_owner is null || _press is not { } press)
        {
            return;
        }

        if (!e.GetCurrentPoint(_owner).Properties.IsLeftButtonPressed)
        {
            Forget();
            return;
        }

        var at = e.GetPosition(_owner);
        if (Math.Abs(at.X - _origin.X) < Threshold && Math.Abs(at.Y - _origin.Y) < Threshold)
        {
            return;
        }

        Forget();

        // A drag that the platform will not take leaves the press being what it looked like at
        // first: a click, which fills a place in. That is the same thing this button does anyway,
        // and it is a better answer to a gesture than a dialog that stops working.
        try
        {
            await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Copy);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Gives up on the press: it was a click, or the drag it became is over.</summary>
    public void Forget()
    {
        _owner = null;
        _press = null;
        _payload = null;
    }
}
