using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using VRCHOTAS.Logging;

namespace VRCHOTAS.Services;

public sealed class SteamVrDriverDeploymentService
{
    private const string DriverName = "vrchotas";
    private readonly IAppLogger _logger;
    private static readonly string DriverTargetRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "openvr",
        "drivers",
        DriverName);

    public SteamVrDriverDeploymentService(IAppLogger logger)
    {
        _logger = logger;
    }

    public void TryDeployOnStartup()
    {
        try
        {
            if (!TryResolveVrPathReg(out var vrPathRegPath))
            {
                _logger.Info(nameof(SteamVrDriverDeploymentService), "SteamVR was not detected. Skipping driver deployment.");
                return;
            }

            if (!TryResolveDriverPayload(out var payload))
            {
                _logger.Warning(nameof(SteamVrDriverDeploymentService), "Driver payload was not found. Skipping SteamVR driver deployment.");
                return;
            }

            DeployPayload(payload);
            RegisterDriver(vrPathRegPath);
            _logger.Info(nameof(SteamVrDriverDeploymentService), $"SteamVR driver deployed to '{DriverTargetRoot}'.");
        }
        catch (Exception ex)
        {
            _logger.Error(nameof(SteamVrDriverDeploymentService), "Silent SteamVR driver deployment failed.", ex);
        }
    }

    private static IEnumerable<string> EnumerateAppBaseDirectories()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }

    private bool TryResolveDriverPayload(out DriverPayload payload)
    {
        foreach (var baseDirectory in EnumerateAppBaseDirectories())
        {
            var payloadRoot = Path.Combine(baseDirectory, "DriverPayload");
            if (TryCreateBundledPayload(payloadRoot, out payload))
            {
                return true;
            }
        }

        foreach (var baseDirectory in EnumerateAppBaseDirectories())
        {
            var virtualDriverRoot = Path.Combine(baseDirectory, "VirtualDriver");
            if (TryCreateRepositoryPayload(virtualDriverRoot, out payload))
            {
                return true;
            }
        }

        payload = default;
        return false;
    }

    private static bool TryCreateBundledPayload(string payloadRoot, out DriverPayload payload)
    {
        var manifestPath = Path.Combine(payloadRoot, "driver.vrdrivermanifest");
        var dllPath = Path.Combine(payloadRoot, "bin", "win64", "driver_vrchotas.dll");
        var inputDirectory = Path.Combine(payloadRoot, "resources", "input");

        return TryCreatePayload(manifestPath, dllPath, inputDirectory, out payload);
    }

    private static bool TryCreateRepositoryPayload(string virtualDriverRoot, out DriverPayload payload)
    {
        var manifestPath = Path.Combine(virtualDriverRoot, "resources", "driver.vrchotas.vrdrivermanifest");
        var dllPath = Path.Combine(virtualDriverRoot, "build", "Release", "driver_vrchotas.dll");
        var inputDirectory = Path.Combine(virtualDriverRoot, "build", "resources", "input");

        return TryCreatePayload(manifestPath, dllPath, inputDirectory, out payload);
    }

    private static bool TryCreatePayload(string manifestPath, string dllPath, string inputDirectory, out DriverPayload payload)
    {
        if (!File.Exists(manifestPath) || !File.Exists(dllPath) || !Directory.Exists(inputDirectory))
        {
            payload = default;
            return false;
        }

        payload = new DriverPayload(manifestPath, dllPath, inputDirectory);
        return true;
    }

    private void DeployPayload(DriverPayload payload)
    {
        var targetBinDirectory = Path.Combine(DriverTargetRoot, "bin", "win64");
        var targetInputDirectory = Path.Combine(DriverTargetRoot, "resources", "input");

        Directory.CreateDirectory(DriverTargetRoot);
        Directory.CreateDirectory(targetBinDirectory);
        Directory.CreateDirectory(targetInputDirectory);

        CopyFile(payload.ManifestPath, Path.Combine(DriverTargetRoot, "driver.vrdrivermanifest"));
        CopyFile(payload.DriverDllPath, Path.Combine(targetBinDirectory, "driver_vrchotas.dll"));
        CopyDirectoryContents(payload.InputDirectoryPath, targetInputDirectory);
    }

    private static void CopyDirectoryContents(string sourceDirectory, string targetDirectory)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, true);
        }
    }

    private static void CopyFile(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, true);
    }

    private void RegisterDriver(string vrPathRegPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = vrPathRegPath,
            Arguments = $"adddriver \"{DriverTargetRoot}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Failed to start vrpathreg.exe.");
        }

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode == 0)
        {
            if (!string.IsNullOrWhiteSpace(standardOutput))
            {
                _logger.Debug(nameof(SteamVrDriverDeploymentService), standardOutput.Trim());
            }

            return;
        }

        var message = string.IsNullOrWhiteSpace(standardError) ? standardOutput.Trim() : standardError.Trim();
        throw new InvalidOperationException($"vrpathreg.exe exited with code {process.ExitCode}. {message}".Trim());
    }

    private static bool TryResolveVrPathReg(out string vrPathRegPath)
    {
        foreach (var candidate in EnumerateVrPathRegCandidates())
        {
            if (File.Exists(candidate))
            {
                vrPathRegPath = candidate;
                return true;
            }
        }

        vrPathRegPath = string.Empty;
        return false;
    }

    private static IEnumerable<string> EnumerateVrPathRegCandidates()
    {
        var steamVrPath = Environment.GetEnvironmentVariable("STEAMVR_PATH");
        if (!string.IsNullOrWhiteSpace(steamVrPath))
        {
            yield return Path.Combine(steamVrPath, "bin", "win64", "vrpathreg.exe");
        }

        foreach (var steamPath in EnumerateRegisteredSteamPaths())
        {
            yield return Path.Combine(steamPath, "steamapps", "common", "SteamVR", "bin", "win64", "vrpathreg.exe");
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "Steam", "steamapps", "common", "SteamVR", "bin", "win64", "vrpathreg.exe");
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "Steam", "steamapps", "common", "SteamVR", "bin", "win64", "vrpathreg.exe");
        }
    }

    private static IEnumerable<string> EnumerateRegisteredSteamPaths()
    {
        foreach (var registryPath in new[]
                 {
                     @"Software\Valve\Steam",
                     @"SOFTWARE\WOW6432Node\Valve\Steam",
                     @"SOFTWARE\Valve\Steam"
                 })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                string? steamPath = null;
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
                    using var subKey = baseKey.OpenSubKey(registryPath);
                    steamPath = subKey?.GetValue("SteamPath") as string;
                }
                catch
                {
                }

                if (!string.IsNullOrWhiteSpace(steamPath))
                {
                    yield return steamPath;
                }

                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var subKey = baseKey.OpenSubKey(registryPath);
                    steamPath = subKey?.GetValue("SteamPath") as string;
                }
                catch
                {
                    steamPath = null;
                }

                if (!string.IsNullOrWhiteSpace(steamPath))
                {
                    yield return steamPath;
                }
            }
        }
    }

    private readonly record struct DriverPayload(string ManifestPath, string DriverDllPath, string InputDirectoryPath);
}
