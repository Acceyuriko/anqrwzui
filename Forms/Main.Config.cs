using System.IO;
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

            var defaultConfig = new Dictionary<string, Dictionary<string, double>>
            {
                ["default"] = new() { ["1"] = 0, ["2"] = 0, ["3"] = 0, ["4"] = 0 },
                ["m416"] = new() { ["1"] = 1.0, ["2"] = 2.0, ["3"] = 3.0, ["4"] = 4.0 },
                ["aks"] = new() { ["1"] = 2.0, ["2"] = 4.0, ["3"] = 6.0, ["4"] = 8.0 },
                ["ump"] = new() { ["1"] = 0.5, ["2"] = 1.0, ["3"] = 1.5, ["4"] = 2.0 }
            };

            var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
            _configOptions = defaultConfig;
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
            var options = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double>>>(json);
            if (options != null)
            {
                _configOptions = options;
                Logger.Info($"配置文件加载成功, 共有 {_configOptions.Count} 个一级选项");
                RefreshComboSelections();
                return true;
            }

            Logger.Warning("配置文件解析结果为空");
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
                    BeginInvoke(new Action(RefreshComboSelections));
                }
                else
                {
                    RefreshComboSelections();
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
}