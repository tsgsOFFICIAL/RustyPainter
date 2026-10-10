using RustyPainter.Service.Logging;
using Microsoft.Win32;
using System.IO;
using System;

namespace RustyPainter.Service;

/// <summary>
/// Knows how this copy of the app was installed (Inno Setup vs. a portable zip extract) and keeps the
/// version Windows shows under Installed apps in line with what is actually running.
/// </summary>
public static class InstallRegistration
{
    /// <summary>The Inno Setup uninstaller file name, dropped next to the exe by every Setup install.</summary>
    public const string UninstallerName = "unins000.exe";

    /// <summary>
    /// Inno Setup registers an install under "{AppName}_is1" when the script has no explicit AppId.
    /// Must match <c>MyAppName</c> in Installer/RustyPainter.iss.
    /// </summary>
    private const string UninstallRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Rusty Painter_is1";

    /// <summary>The folder the running exe lives in.</summary>
    public static string InstallDirectory => Path.GetDirectoryName(Utility.GetExePath()) ?? AppContext.BaseDirectory;

    /// <summary>
    /// Setup always drops its uninstaller next to the exe, and portable zip extracts never have one. If the
    /// uninstaller was lost, the registration pointing at this folder still identifies the install, and
    /// re-running Setup repairs it.
    /// </summary>
    public static bool IsInstallerInstall()
    {
        if (File.Exists(Path.Combine(InstallDirectory, UninstallerName)))
            return true;

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(UninstallRegistryKey);
            return key is not null && IsRegisteredForThisInstall(key);
        }
        catch (Exception ex)
        {
            AppLog.Warn("InstallRegistration", $"Could not read the installer registration: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Self-contained publishes bundle the runtime next to the exe; framework-dependent ones rely on a
    /// system-wide .NET.
    /// </summary>
    public static bool IsSelfContained() => File.Exists(Path.Combine(InstallDirectory, "System.Private.CoreLib.dll"));

    /// <summary>
    /// Keeps the version Windows shows under Installed apps in line with the running app. Installs that
    /// were updated by copying files would otherwise keep showing the version Setup wrote.
    /// </summary>
    /// <remarks>Only touches the registration that belongs to this installation folder. Never throws.</remarks>
    public static void SyncRegisteredVersion()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(UninstallRegistryKey, writable: true);
            if (key is null || !IsRegisteredForThisInstall(key))
                return;

            string version = Utility.GetDisplayVersion();
            if (version == "N/A" || string.Equals(key.GetValue("DisplayVersion") as string, version, StringComparison.Ordinal))
                return;

            key.SetValue("DisplayVersion", version);
            AppLog.Info("InstallRegistration", $"Updated the registered version to {version}.");
        }
        catch (Exception ex)
        {
            AppLog.Warn("InstallRegistration", $"Could not update the registered version: {ex.Message}");
        }
    }

    private static bool IsRegisteredForThisInstall(RegistryKey key)
    {
        string? location = key.GetValue("InstallLocation") as string ?? key.GetValue("Inno Setup: App Path") as string;
        if (string.IsNullOrWhiteSpace(location))
            return false;

        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(InstallDirectory)),
            StringComparison.OrdinalIgnoreCase);
    }
}
