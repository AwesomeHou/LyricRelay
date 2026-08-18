using Microsoft.Win32;
using System.Security;

namespace LyricRelay.Windows;

public sealed class StartupManager
{
    private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string ValueName = "LyricRelay";

    public void Apply(bool enabled)
    {
        RegistryKey? key;
        try
        {
            key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        catch (SecurityException)
        {
            return;
        }

        using (key)
        {
            if (key is null) return;
            if (enabled)
            {
                var executable = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(executable))
                {
                    key.SetValue(ValueName, $"\"{executable}\" --background");
                }
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
    }
}
