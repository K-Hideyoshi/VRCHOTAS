using CommunityToolkit.Mvvm.ComponentModel;
using VRCHOTAS.Logging;

namespace VRCHOTAS.ViewModels;

public sealed class LogLevelFilterItem : ObservableObject
{
    private bool _isSelected;

    public LogLevelFilterItem(AppLogLevel level)
    {
        Level = level;
        DisplayName = level.ToString();
    }

    public AppLogLevel Level { get; }
    public string DisplayName { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
