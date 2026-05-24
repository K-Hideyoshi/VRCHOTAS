namespace VRCHOTAS.ViewModels;

/// <summary>One configuration file row for configuration menus.</summary>
public sealed class ConfigurationMenuItem
{
    public ConfigurationMenuItem(string fileName, bool isChecked)
    {
        FileName = fileName;
        IsChecked = isChecked;
    }

    public string FileName { get; }
    public string DisplayFileName => FileName.Replace("_", "__", StringComparison.Ordinal);
    public bool IsChecked { get; }
}
