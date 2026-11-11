using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.ViewModels;

// What the field shows while nobody is typing in it: the text as it reads, with each name a box.
public partial class StepParameterViewModel : ViewModelBase
{
    /// <summary>
    /// The value as the pieces it is made of. A name is one piece, and so is every run of text
    /// between two names, which is what lets the field draw a variable as a box rather than as a
    /// word with a sign in front of it.
    /// </summary>
    public ObservableCollection<ParameterToken> Tokens { get; } = [];

    /// <summary>
    /// True while the line is open for typing. The chips are what the field shows otherwise, so a
    /// value reads the way it will look when the step runs rather than the way it is spelled.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTokens))]
    public partial bool IsEditing { get; set; }

    /// <summary>
    /// True when the field draws chips instead of a box: only for a value that names something, and
    /// only while it is not being typed in. A field holding plain text is a box like any other,
    /// because there is nothing in it that a box cannot show.
    /// </summary>
    public bool ShowsTokens => !IsEditing && Tokens.Any(token => token.IsName);

    /// <summary>Reads the line again into the pieces the field draws.</summary>
    private void RefreshTokens()
    {
        Tokens.Clear();
        foreach (var token in ParameterToken.Of(Formula.FromLine(Text)))
        {
            Tokens.Add(token);
        }

        OnPropertyChanged(nameof(ShowsTokens));
    }
}
