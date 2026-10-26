#nullable enable
using System.IO;
using System.Runtime.InteropServices;
using AgentsHub.Core.Legacy.ConPTY;
using AgentsHub.Services.Sidecar;

namespace AgentsHub.Services.Core;

public static class EngineFactory
{
    /// <summary>
    /// Creates the appropriate engine adapter based on requested mode and platform availability.
    /// Gracefully falls back to LegacyConPTY only on Windows if requested.
    /// </summary>
    public static IEngineAdapter CreateEngine(EngineMode preferredMode = EngineMode.Sidecar)
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        if (preferredMode == EngineMode.LegacyConPTY)
        {
            if (isWindows)
            {
#pragma warning disable CS0618 // Type or member is obsolete
                return new LegacyConPtyAdapter();
#pragma warning restore CS0618
            }
            throw new PlatformNotSupportedException("Legacy ConPTY mode is only supported on Windows.");
        }

        // Default: Sidecar engine
        string sidecarPath = SidecarManager.ResolveBinaryPath();
        if (File.Exists(sidecarPath))
        {
            return new SidecarEngineAdapter();
        }

        // If sidecar binary is missing on Windows, fallback to legacy ConPTY with warning
        if (isWindows)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return new LegacyConPtyAdapter();
#pragma warning restore CS0618
        }

        // On Linux / macOS, sidecar is strictly required
        throw new FileNotFoundException(
            $"Sidecar engine binary not found at '{sidecarPath}'. Cross-compile using 'bun run build.ts' before starting AgentsHub on non-Windows platforms.");
    }
}
