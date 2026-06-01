using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using VRCHOTAS.Logging;

namespace VRCHOTAS.Services;

public sealed class OpenVrNativeLibraryService : IDisposable
{
    private static readonly string[] SteamVrProcessNames = ["vrserver", "vrmonitor", "vrcompositor"];
    private static readonly object ResolverSync = new();

    private static bool _resolverRegistered;
    private static IntPtr _resolvedOpenVrLibraryHandle;

    private readonly IAppLogger _logger;
    private IntPtr _nativeLibraryHandle;
    private bool _disposed;

    public OpenVrNativeLibraryService(IAppLogger logger)
    {
        _logger = logger;
        EnsureDllImportResolverRegistered();
    }

    public bool TryEnsureLoaded(out string failureMessage)
    {
        failureMessage = string.Empty;
        if (_nativeLibraryHandle != IntPtr.Zero)
        {
            return true;
        }

        foreach (var candidate in EnumerateOpenVrApiCandidates())
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            if (NativeLibrary.TryLoad(candidate, out var handle))
            {
                _resolvedOpenVrLibraryHandle = handle;
                _nativeLibraryHandle = handle;
                _logger.Info(nameof(OpenVrNativeLibraryService), $"Loaded openvr_api.dll from '{candidate}'.");
                return true;
            }
        }

        failureMessage = "openvr_api.dll was not found in the application output, OpenVR SDK cache, or SteamVR installation.";
        return false;
    }

    public bool IsSteamVrRunning()
    {
        foreach (var processName in SteamVrProcessNames)
        {
            try
            {
                if (Process.GetProcessesByName(processName).Length > 0)
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_nativeLibraryHandle != IntPtr.Zero)
        {
            if (_resolvedOpenVrLibraryHandle == _nativeLibraryHandle)
            {
                _resolvedOpenVrLibraryHandle = IntPtr.Zero;
            }

            NativeLibrary.Free(_nativeLibraryHandle);
            _nativeLibraryHandle = IntPtr.Zero;
        }
    }

    private static void EnsureDllImportResolverRegistered()
    {
        lock (ResolverSync)
        {
            if (_resolverRegistered)
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(typeof(Valve.VR.OpenVR).Assembly, static (libraryName, _, _) =>
            {
                if (string.Equals(libraryName, "openvr_api", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(libraryName, "openvr_api.dll", StringComparison.OrdinalIgnoreCase))
                {
                    return _resolvedOpenVrLibraryHandle;
                }

                return IntPtr.Zero;
            });

            _resolverRegistered = true;
        }
    }

    private static IEnumerable<string> EnumerateOpenVrApiCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var steamVrPath = Environment.GetEnvironmentVariable("STEAMVR_PATH");
        if (!string.IsNullOrWhiteSpace(steamVrPath))
        {
            var candidate = Path.Combine(steamVrPath, "bin", "win64", "openvr_api.dll");
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }

        foreach (var steamPath in EnumerateRegisteredSteamPaths())
        {
            var candidate = Path.Combine(steamPath, "steamapps", "common", "SteamVR", "bin", "win64", "openvr_api.dll");
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            var candidate = Path.Combine(programFilesX86, "Steam", "steamapps", "common", "SteamVR", "bin", "win64", "openvr_api.dll");
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            var candidate = Path.Combine(programFiles, "Steam", "steamapps", "common", "SteamVR", "bin", "win64", "openvr_api.dll");
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }

        var openVrSdkPath = Environment.GetEnvironmentVariable("OPENVR_SDK_PATH");
        if (!string.IsNullOrWhiteSpace(openVrSdkPath))
        {
            var candidate = Path.Combine(openVrSdkPath, "bin", "win64", "openvr_api.dll");
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }

        foreach (var candidate in EnumerateAppLocalOpenVrApiCandidates())
        {
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<string> EnumerateAppLocalOpenVrApiCandidates()
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            yield return Path.Combine(baseDirectory, "openvr_api.dll");
        }

        var current = string.IsNullOrWhiteSpace(baseDirectory)
            ? null
            : new DirectoryInfo(baseDirectory);
        while (current is not null)
        {
            yield return Path.Combine(current.FullName, "artifacts", "release", "openvr-sdk-cache", "sdk", "bin", "win64", "openvr_api.dll");
            current = current.Parent;
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
}
