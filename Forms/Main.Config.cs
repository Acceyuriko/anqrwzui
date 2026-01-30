using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace anqrwzui;

public partial class Main
{
    private void InitializeConfigPath()
    {
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "config.json");
    }

    private void InitializeSelectionStatePath()
    {
        var configDir = Path.GetDirectoryName(_configPath);
        _selectionStatePath = Path.Combine(configDir ?? AppDomain.CurrentDomain.BaseDirectory, "last_selection.json");
    }

    private void EnsureConfigFileExists()
    {
        try
        {
            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(_configPath))
            {
                return;
            }

            var defaultConfig = new List<object[]>
            {
                new object[] { "default", 0.0 },
                new object[] { "m416-1", 0.6 },
                new object[] { "vss", 0.7 },
                new object[] { "m416-2", 1.0 },
                new object[] { "aks-1", 1.1 },
                new object[] { "m416-3", 1.6 },
                new object[] { "m416-4", 2.2 },
            };

            var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
            if (TryParseConfigOptions(json, out var parsedOptions))
            {
                _configOptions = parsedOptions;
            }
            Logger.Info($"未找到配置文件，已创建默认配置: {_configPath}");
        }
        catch (Exception ex)
        {
            Logger.Error("创建默认配置文件失败", ex);
        }
    }

    private bool LoadConfigOptions()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                Logger.Warning($"未找到配置文件: {_configPath}");
                return false;
            }

            var json = File.ReadAllText(_configPath);
            if (TryParseConfigOptions(json, out var options))
            {
                _configOptions = options;
                Logger.Info($"配置文件加载成功, 共有 {_configOptions.Count} 个选项");
                RefreshOptionSelections();
                return true;
            }

            Logger.Warning("配置文件解析结果为空或格式不正确");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("读取配置文件失败", ex);
            return false;
        }
    }

    private void SetupConfigWatcher()
    {
        try
        {
            var directory = Path.GetDirectoryName(_configPath);
            var fileName = Path.GetFileName(_configPath);
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
            {
                return;
            }

            _configReloadTimer = new System.Threading.Timer(_ => ReloadConfigFromWatcher(), null, Timeout.Infinite, Timeout.Infinite);

            _configWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            _configWatcher.Changed += OnConfigFileChanged;
            _configWatcher.Created += OnConfigFileChanged;
            _configWatcher.Renamed += OnConfigFileChanged;
            _configWatcher.EnableRaisingEvents = true;

            Logger.Info("配置文件监控已启动");
        }
        catch (Exception ex)
        {
            Logger.Error("配置文件监控启动失败", ex);
        }
    }

    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            _configReloadTimer?.Change(250, Timeout.Infinite);
        }
        catch (Exception ex)
        {
            Logger.Error("配置文件变更事件处理失败", ex);
        }
    }

    private void ReloadConfigFromWatcher()
    {
        try
        {
            if (LoadConfigOptions())
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(RefreshOptionSelections));
                }
                else
                {
                    RefreshOptionSelections();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("配置文件重新加载失败", ex);
        }
    }

    private void DisposeConfigWatcher()
    {
        try
        {
            if (_configWatcher != null)
            {
                _configWatcher.EnableRaisingEvents = false;
                _configWatcher.Changed -= OnConfigFileChanged;
                _configWatcher.Created -= OnConfigFileChanged;
                _configWatcher.Renamed -= OnConfigFileChanged;
                _configWatcher.Dispose();
                _configWatcher = null;
            }

            _configReloadTimer?.Dispose();
            _configReloadTimer = null;
        }
        catch (Exception ex)
        {
            Logger.Error("释放配置文件监控资源失败", ex);
        }
    }

    private bool TryParseConfigOptions(string json, out List<ConfigOption> options)
    {
        options = new List<ConfigOption>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var arr = item.EnumerateArray().ToArray();
                if (arr.Length < 2)
                {
                    continue;
                }

                var key = arr[0].GetString();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                double value;
                if (arr[1].ValueKind == JsonValueKind.Number)
                {
                    value = arr[1].GetDouble();
                }
                else if (arr[1].ValueKind == JsonValueKind.String && double.TryParse(arr[1].GetString(), out var parsed))
                {
                    value = parsed;
                }
                else
                {
                    continue;
                }

                options.Add(new ConfigOption(key, value));
            }

            options = options.OrderBy(o => o.Value).ThenBy(o => o.Key).ToList();
            return options.Count > 0;
        }
        catch (Exception ex)
        {
            Logger.Error("解析配置文件失败", ex);
            return false;
        }
    }
}