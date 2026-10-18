using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

public abstract class ViewModelBase : ObservableObject, ILanguageAware
{
    /// <summary>
    /// Tells every binding on this object that its source may have changed. A label computed
    /// from the text table is read again; nothing has to be stored a second time.
    /// </summary>
    public virtual void RefreshLanguage()
        => OnPropertyChanged(new PropertyChangedEventArgs(string.Empty));
}
