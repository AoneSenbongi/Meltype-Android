// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;

namespace Meltype.Config;

public sealed record InputPreferences
{
    public string Comma { get; init; } = "，";
    public string Period { get; init; } = "．";
    public bool LearningEnabled { get; init; } = true;
    public InputPreferences Validate() => this with
    {
        Comma = Comma is "、" or "，" or "," ? Comma : "，",
        Period = Period is "。" or "．" or "." ? Period : "．"
    };
}

public sealed class InputPreferenceStore(string path)
{
    private readonly object _gate = new();
    public InputPreferences Current
    {
        get
        {
            lock (_gate)
            {
                try
                {
                    if (!File.Exists(path)) return new();
                    if (new FileInfo(path).Length > 4096) return new() { LearningEnabled = false };
                    return (JsonSerializer.Deserialize<InputPreferences>(File.ReadAllText(path)) ??
                        new InputPreferences { LearningEnabled = false }).Validate();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                { return new() { LearningEnabled = false }; }
            }
        }
    }
    public void Save(InputPreferences value)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(value.Validate()));
                File.Move(temp, path, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
