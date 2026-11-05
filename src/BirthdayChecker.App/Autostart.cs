using System.IO;
using Microsoft.Win32;

namespace BirthdayChecker.App;

/// <summary>Start with Windows via HKCU\...\Run — no admin rights, no installer needed. The registry is the source of truth.</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Task Manager's "Startup apps" switch: the Run value stays, a flag here says "disabled".
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "BirthDayChecker";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    /// <summary>
    /// False when started as "dotnet BirthdayChecker.dll" (or the path is unknown): writing that would
    /// start a bare dotnet.exe at every login.
    /// </summary>
    public static bool CanEnable =>
        Environment.ProcessPath is { } p && !Path.GetFileNameWithoutExtension(p).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

    private static string? Current()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string;
    }

    public static bool IsEnabled => Current() is not null && !DisabledInTaskManager;

    /// <summary>Enabled, but for another copy of the exe (moved or reinstalled elsewhere).</summary>
    public static bool PointsElsewhere => Current() is { } cmd && !string.Equals(cmd, Command, StringComparison.OrdinalIgnoreCase);

    /// <summary>Registered, but switched off in Task Manager (first byte of the flag is odd).</summary>
    public static bool DisabledInTaskManager
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return Current() is not null && key?.GetValue(ValueName) is byte[] { Length: > 0 } flag && (flag[0] & 1) == 1;
        }
    }

    /// <exception cref="UnauthorizedAccessException">Blocked by policy.</exception>
    public static void Set(bool enabled)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (enabled)
                key.SetValue(ValueName, Command);
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        // Either way the Task Manager flag is stale now: without it Windows treats the entry as enabled.
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
