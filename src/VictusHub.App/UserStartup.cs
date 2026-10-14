// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VictusHub.App;

internal static class UserStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Victus Gaming Hub.lnk");
    public static bool IsEnabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return File.Exists(ShortcutPath) || key?.GetValue("VictusHub") is string; }
    }
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            string exe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "VictusHub.App.exe"));
            if (!File.Exists(exe)) throw new IOException("Installed application not found.");
            Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath)!);
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            object? shortcut = null;
            try
            {
                shortcut = ((dynamic)shell).CreateShortcut(ShortcutPath);
                ((dynamic)shortcut).TargetPath = exe;
                ((dynamic)shortcut).Arguments = "--tray";
                ((dynamic)shortcut).WorkingDirectory = AppContext.BaseDirectory;
                ((dynamic)shortcut).IconLocation = exe + ",0";
                ((dynamic)shortcut).Save();
            }
            finally
            {
                if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
                Marshal.FinalReleaseComObject(shell);
            }
        }
        else File.Delete(ShortcutPath);
        // Migrate the old Run entry only after the shortcut was successfully saved.
        key.DeleteValue("VictusHub", false);
    }
}
