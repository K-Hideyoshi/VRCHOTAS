namespace VRCHOTAS.ViewModels;

/// <summary>One configuration file row for Load / Set Default menus with checkmark prefixes.</summary>
public sealed class ConfigurationMenuItem
{
    public ConfigurationMenuItem(string fileName, bool isCurrent, bool isDefault)
    {
        FileName = fileName;
        MenuHeader = BuildHeader(fileName, isCurrent, isDefault);
    }

    public string FileName { get; }
    public string MenuHeader { get; }

    private static string BuildHeader(string fileName, bool isCurrent, bool isDefault)
    {
        var prefix = string.Empty;
        if (isCurrent)
        {
            prefix += "√ ";
        }

        if (isDefault)
        {
            prefix += "√ ";
        }

        return prefix + fileName;
    }
}
