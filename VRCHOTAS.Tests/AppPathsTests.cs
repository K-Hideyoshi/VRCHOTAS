using VRCHOTAS.Models;
using Xunit;

namespace VRCHOTAS.Tests;

public sealed class AppPathsTests
{
    [Theory]
    [InlineData("config", "config.json")]
    [InlineData("config.json", "config.json")]
    [InlineData("my-config", "my-config.json")]
    [InlineData("my.config.json", "my.config.json")]
    public void EnsureJsonExtension_AddsExtensionWhenMissing(string input, string expected)
    {
        Assert.Equal(expected, AppPaths.EnsureJsonExtension(input));
    }

    [Fact]
    public void GetConfigurationPath_ReturnsPathUnderConfigDirectory()
    {
        var path = AppPaths.GetConfigurationPath("test.json");
        Assert.EndsWith("test.json", path);
        Assert.Contains("configs", path);
    }

    [Fact]
    public void AppDataDirectory_ContainsVRCHOTAS()
    {
        Assert.EndsWith("VRCHOTAS", AppPaths.AppDataDirectory);
    }

    [Fact]
    public void PreferencesFilePath_IsUnderAppDataDirectory()
    {
        Assert.StartsWith(AppPaths.AppDataDirectory, AppPaths.PreferencesFilePath);
        Assert.EndsWith("preferences.json", AppPaths.PreferencesFilePath);
    }
}
