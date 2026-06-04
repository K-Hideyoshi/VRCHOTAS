using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Valve.VR;
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
        var runningProcesses = new List<string>();
        foreach (var processName in SteamVrProcessNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                if (processes.Length > 0)
                {
                    runningProcesses.Add($"{processName}(x{processes.Length})");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(nameof(OpenVrNativeLibraryService), 
                    $"Failed to check process '{processName}': {ex.Message}");
            }
        }

        var isRunning = runningProcesses.Count > 0;
        _logger.Info(nameof(OpenVrNativeLibraryService), 
            isRunning 
                ? $"SteamVR processes detected: {string.Join(", ", runningProcesses)}" 
                : "No SteamVR processes detected.");

        return isRunning;
    }

    /// <summary>
    /// Checks if SteamVR's IPC infrastructure is ready for OpenVR.Init() calls.
    /// Uses OpenVR's own APIs to test runtime availability instead of process heuristics.
    /// </summary>
    public bool IsSteamVrIpcReady()
    {
        _logger.Info(nameof(OpenVrNativeLibraryService), "=== IPC Readiness Check Started ===");

        // First check: SteamVR processes must be running
        if (!IsSteamVrRunning())
        {
            _logger.Info(nameof(OpenVrNativeLibraryService), "IPC Check Result: SteamVR not running");
            return false;
        }

        // Second check: Use OpenVR's native APIs to test if runtime is truly ready
        try
        {
            // Ensure openvr_api.dll is loaded first
            if (!TryEnsureLoaded(out var loadFailure))
            {
                _logger.Warning(nameof(OpenVrNativeLibraryService), 
                    $"IPC Check Result: OpenVR DLL not loaded - {loadFailure}");
                return false;
            }

            // Test 1: Check if runtime is installed and configured
            bool runtimeInstalled = OpenVR.IsRuntimeInstalled();
            _logger.Info(nameof(OpenVrNativeLibraryService), 
                $"OpenVR.IsRuntimeInstalled() = {runtimeInstalled}");

            if (!runtimeInstalled)
            {
                _logger.Warning(nameof(OpenVrNativeLibraryService), 
                    "IPC Check Result: OpenVR runtime not installed or not configured");
                return false;
            }

            // Test 2: Check if HMD is present (indicates SteamVR is actively managing a device session)
            bool hmdPresent = OpenVR.IsHmdPresent();
            _logger.Info(nameof(OpenVrNativeLibraryService), 
                $"OpenVR.IsHmdPresent() = {hmdPresent}");

            if (!hmdPresent)
            {
                _logger.Warning(nameof(OpenVrNativeLibraryService), 
                    "IPC Check Result: No HMD present - SteamVR may be running but not in active VR session");
                return false;
            }

            // Both checks passed - IPC should be ready
            _logger.Info(nameof(OpenVrNativeLibraryService), 
                "IPC Check Result: ✓ OpenVR runtime ready (installed + HMD present)");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning(nameof(OpenVrNativeLibraryService), 
                $"IPC Check Result: Exception during OpenVR API check: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
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

        // ALWAYS prefer the app-local or SDK version of openvr_api.dll over the SteamVR installation version.
        // Loading the implementation directly from the SteamVR installation folder can cause IPC State 310 errors
        // and hangs during VR_Init because the internal DLL expects to be hosted by SteamVR itself.
        foreach (var candidate in EnumerateAppLocalOpenVrApiCandidates())
        {
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
