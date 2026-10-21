#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AgentsHub.Models;

namespace AgentsHub.Services;

public sealed class CustomCliService
{
    public static CustomCliService Instance { get; } = new();

    private readonly ConcurrentDictionary<string, CustomCliConfig> _clis = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _fileLock = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public event Action? CustomClisChanged;

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AgentsHub",
        "custom_clis.json"
    );

    public CustomCliService()
    {
        Load();
    }

    public List<CustomCliConfig> GetCustomClis()
    {
        return [.. _clis.Values.OrderBy(c => c.Name)];
    }

    public bool TryGetCustomCli(string id, out CustomCliConfig config)
    {
        return _clis.TryGetValue(id, out config!);
    }

    public void SaveCustomCli(CustomCliConfig cli)
    {
        if (string.IsNullOrWhiteSpace(cli.Id))
        {
            cli.Id = "cli_" + Guid.NewGuid().ToString("N")[..8];
        }
        if (string.IsNullOrWhiteSpace(cli.ShortName))
        {
            cli.ShortName = cli.Name;
        }

        _clis[cli.Id] = cli;
        Persist();
        CustomClisChanged?.Invoke();
    }

    public bool DeleteCustomCli(string id)
    {
        if (_clis.TryRemove(id, out _))
        {
            Persist();
            CustomClisChanged?.Invoke();
            return true;
        }
        return false;
    }

    private void Load()
    {
        lock (_fileLock)
        {
            if (!File.Exists(ConfigPath)) return;
            try
            {
                var json = File.ReadAllText(ConfigPath);
                var list = JsonSerializer.Deserialize<List<CustomCliConfig>>(json, JsonOpts);
                if (list != null)
                {
                    _clis.Clear();
                    foreach (var c in list)
                    {
                        if (!string.IsNullOrWhiteSpace(c.Id))
                        {
                            _clis[c.Id] = c;
                        }
                    }
                }
            }
            catch { }
        }
    }

    private void Persist()
    {
        lock (_fileLock)
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(_clis.Values.ToList(), JsonOpts);
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }
    }
}
