// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Text.Json;
using VictusHub.Core.Hardware;

namespace VictusHub.App;
internal sealed class DesktopEcoManager(string directory)
{
    private string PathName => Path.Combine(directory, "display-recovery.json");
    public void Enter()
    {
        if (File.Exists(PathName)) return;
        var current = DisplayRefresh.Read();
        if (current.Frequency <= 60) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(PathName + ".tmp", JsonSerializer.Serialize(current));
        File.Move(PathName + ".tmp", PathName, true);
        DisplayRefresh.Set(60);
    }
    public void Restore()
    {
        if (!File.Exists(PathName)) return;
        var original = JsonSerializer.Deserialize<DisplayReading>(File.ReadAllText(PathName)) ?? throw new InvalidOperationException("Ekran geri yükleme kaydı okunamadı.");
        var current = DisplayRefresh.Read();
        if (current.Device == original.Device && current.Width == original.Width && current.Height == original.Height && current.Frequency == 60)
            DisplayRefresh.Set(original.Frequency);
        File.Delete(PathName);
    }
}
