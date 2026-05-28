using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace VRCHOTAS;

public partial class AboutWindow : Window
{
    private const string ProjectUrl = "https://github.com/K-Hideyoshi/VRCHOTAS";

    public AboutWindow()
    {
        InitializeComponent();
        VersionTextBlock.Text = $"Version {GetVersionText()}";
    }

    private static string GetVersionText()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version is null)
        {
            return "Unknown";
        }

        return version.Revision > 0 ? version.ToString() : version.ToString(3);
    }

    private void OnProjectLinkNavigate(object sender, RequestNavigateEventArgs e)
    {
        OpenUrl(e.Uri?.ToString() ?? ProjectUrl);
        e.Handled = true;
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}