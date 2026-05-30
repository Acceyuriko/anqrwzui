using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace anqrwzui;

public partial class Main
{
    private sealed class AppConfigDocument
    {
        public List<object[]>? Options { get; set; }
        public DetectorConfig? Detector { get; set; }
        public AimPhysicsConfig? AimPhysics { get; set; }
        public AimTargetConfig? AimTarget { get; set; }
    }

    private sealed class DetectorConfig
    {
        public float SelfFilterAreaRatio { get; set; } = 0.018f;
    }

    private sealed class AimPhysicsConfig
    {
        public double Kp { get; set; } = DefaultAimKp;
    }

    private sealed class AimTargetConfig
    {
        public int HoldFrames { get; set; } = DefaultAimTargetHoldFrames;
        public double AssociationDistanceRatio { get; set; } = DefaultAimTargetAssociationDistanceRatio;
        public double MaxOffsetRatio { get; set; } = DefaultAimTargetMaxOffsetRatio;
    }

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

            var defaultOptions = new List<object[]>
            {
                new object[] { "default", 0.0 },
                new object[] { "m416-1", 0.6 },
                new object[] { "m416-2", 1.0 },
                new object[] { "m416-3", 1.6 },
                new object[] { "m416-4", 2.2 },
            };

            var defaultConfig = new AppConfigDocument
            {
                Options = defaultOptions,
                Detector = new DetectorConfig
                {
                    SelfFilterAreaRatio = _selfFilterAreaRatio
                },
                AimPhysics = new AimPhysicsConfig
                {
                    Kp = ClampAimKp(_aimPhysicsKp)
                },
                AimTarget = new AimTargetConfig
                {
                    HoldFrames = ClampAimTargetHoldFrames(_aimTargetHoldFrames),
                    AssociationDistanceRatio = ClampAimTargetAssociationDistanceRatio(_aimTargetAssociationDistanceRatio),
                    MaxOffsetRatio = ClampAimTargetMaxOffsetRatio(_aimTargetMaxOffsetRatio)
                }
            };

            var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
            if (TryParseConfig(json, out var parsedOptions, out var areaRatio, out var aimKp, out var aimTargetHoldFrames, out var aimTargetAssociationDistanceRatio, out var aimTargetMaxOffsetRatio))
            {
                _configOptions = parsedOptions;
                _selfFilterAreaRatio = areaRatio;
                _aimPhysicsKp = aimKp;
                _aimPhysicsKd = ComputeCriticalDampingKd(_aimPhysicsKp);
                _aimTargetHoldFrames = aimTargetHoldFrames;
                _aimTargetAssociationDistanceRatio = aimTargetAssociationDistanceRatio;
                _aimTargetMaxOffsetRatio = aimTargetMaxOffsetRatio;
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
            if (TryParseConfig(json, out var options, out var areaRatio, out var aimKp, out var aimTargetHoldFrames, out var aimTargetAssociationDistanceRatio, out var aimTargetMaxOffsetRatio))
            {
                _configOptions = options;
                _selfFilterAreaRatio = areaRatio;
                _aimPhysicsKp = aimKp;
                _aimPhysicsKd = ComputeCriticalDampingKd(_aimPhysicsKp);
                _aimTargetHoldFrames = aimTargetHoldFrames;
                _aimTargetAssociationDistanceRatio = aimTargetAssociationDistanceRatio;
                _aimTargetMaxOffsetRatio = aimTargetMaxOffsetRatio;
                Logger.Info($"配置文件加载成功, 共有 {_configOptions.Count} 个选项");
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
                    BeginInvoke(new Action(() =>
                    {
                        RefreshOptionSelections();
                        ApplySelfFilterAreaRatioToUiAndDetector();
                        ApplyAimPhysicsKpToUiAndState();
                    }));
                }
                else
                {
                    RefreshOptionSelections();
                    ApplySelfFilterAreaRatioToUiAndDetector();
                    ApplyAimPhysicsKpToUiAndState();
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

            _configSaveDebounceTimer?.Dispose();
            _configSaveDebounceTimer = null;
        }
        catch (Exception ex)
        {
            Logger.Error("释放配置文件监控资源失败", ex);
        }
    }

    private bool TryParseConfig(string json, out List<ConfigOption> options, out float selfFilterAreaRatio, out double aimKp, out int aimTargetHoldFrames, out double aimTargetAssociationDistanceRatio, out double aimTargetMaxOffsetRatio)
    {
        options = new List<ConfigOption>();
        selfFilterAreaRatio = _selfFilterAreaRatio;
        aimKp = _aimPhysicsKp;
        aimTargetHoldFrames = _aimTargetHoldFrames;
        aimTargetAssociationDistanceRatio = _aimTargetAssociationDistanceRatio;
        aimTargetMaxOffsetRatio = _aimTargetMaxOffsetRatio;
        try
        {
            using var doc = JsonDocument.Parse(json);

            JsonElement optionsElement;
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                // 兼容旧格式：根节点就是选项数组
                optionsElement = doc.RootElement;
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (!doc.RootElement.TryGetProperty("Options", out optionsElement) || optionsElement.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                if (doc.RootElement.TryGetProperty("Detector", out var detectorElement) && detectorElement.ValueKind == JsonValueKind.Object)
                {
                    if (detectorElement.TryGetProperty("SelfFilterAreaRatio", out var ratioElement))
                    {
                        float ratio = selfFilterAreaRatio;
                        if (ratioElement.ValueKind == JsonValueKind.Number)
                        {
                            ratio = (float)ratioElement.GetDouble();
                        }
                        else if (ratioElement.ValueKind == JsonValueKind.String && float.TryParse(ratioElement.GetString(), out var parsedRatio))
                        {
                            ratio = parsedRatio;
                        }

                        selfFilterAreaRatio = ClampSelfFilterAreaRatio(ratio);
                    }
                }

                if (doc.RootElement.TryGetProperty("AimPhysics", out var aimPhysicsElement) && aimPhysicsElement.ValueKind == JsonValueKind.Object)
                {
                    if (aimPhysicsElement.TryGetProperty("Kp", out var kpElement))
                    {
                        double kp = aimKp;
                        if (kpElement.ValueKind == JsonValueKind.Number)
                        {
                            kp = kpElement.GetDouble();
                        }
                        else if (kpElement.ValueKind == JsonValueKind.String && double.TryParse(kpElement.GetString(), out var parsedKp))
                        {
                            kp = parsedKp;
                        }

                        aimKp = ClampAimKp(kp);
                    }
                }

                if (doc.RootElement.TryGetProperty("AimTarget", out var aimTargetElement) && aimTargetElement.ValueKind == JsonValueKind.Object)
                {
                    if (aimTargetElement.TryGetProperty("HoldFrames", out var holdFramesElement))
                    {
                        int holdFrames = aimTargetHoldFrames;
                        if (holdFramesElement.ValueKind == JsonValueKind.Number)
                        {
                            holdFrames = holdFramesElement.GetInt32();
                        }
                        else if (holdFramesElement.ValueKind == JsonValueKind.String && int.TryParse(holdFramesElement.GetString(), out var parsedHoldFrames))
                        {
                            holdFrames = parsedHoldFrames;
                        }

                        aimTargetHoldFrames = ClampAimTargetHoldFrames(holdFrames);
                    }

                    if (aimTargetElement.TryGetProperty("AssociationDistanceRatio", out var associationDistanceElement))
                    {
                        double associationDistanceRatio = aimTargetAssociationDistanceRatio;
                        if (associationDistanceElement.ValueKind == JsonValueKind.Number)
                        {
                            associationDistanceRatio = associationDistanceElement.GetDouble();
                        }
                        else if (associationDistanceElement.ValueKind == JsonValueKind.String && double.TryParse(associationDistanceElement.GetString(), out var parsedAssociationDistanceRatio))
                        {
                            associationDistanceRatio = parsedAssociationDistanceRatio;
                        }

                        aimTargetAssociationDistanceRatio = ClampAimTargetAssociationDistanceRatio(associationDistanceRatio);
                    }

                    if (aimTargetElement.TryGetProperty("MaxOffsetRatio", out var maxOffsetElement))
                    {
                        double maxOffsetRatio = aimTargetMaxOffsetRatio;
                        if (maxOffsetElement.ValueKind == JsonValueKind.Number)
                        {
                            maxOffsetRatio = maxOffsetElement.GetDouble();
                        }
                        else if (maxOffsetElement.ValueKind == JsonValueKind.String && double.TryParse(maxOffsetElement.GetString(), out var parsedMaxOffsetRatio))
                        {
                            maxOffsetRatio = parsedMaxOffsetRatio;
                        }

                        aimTargetMaxOffsetRatio = ClampAimTargetMaxOffsetRatio(maxOffsetRatio);
                    }
                }
            }
            else
            {
                return false;
            }

            foreach (var item in optionsElement.EnumerateArray())
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

    private void QueueSaveConfigDebounced(int dueTimeMs = 350)
    {
        _configSaveDebounceTimer ??= new System.Threading.Timer(_ => SaveConfigDocument(), null, Timeout.Infinite, Timeout.Infinite);
        _configSaveDebounceTimer.Change(dueTimeMs, Timeout.Infinite);
    }

    private void SaveConfigDocument()
    {
        try
        {
            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var config = new AppConfigDocument
            {
                Options = _configOptions.Select(o => new object[] { o.Key, o.Value }).ToList(),
                Detector = new DetectorConfig
                {
                    SelfFilterAreaRatio = ClampSelfFilterAreaRatio(_selfFilterAreaRatio)
                },
                AimPhysics = new AimPhysicsConfig
                {
                    Kp = ClampAimKp(_aimPhysicsKp)
                },
                AimTarget = new AimTargetConfig
                {
                    HoldFrames = ClampAimTargetHoldFrames(_aimTargetHoldFrames),
                    AssociationDistanceRatio = ClampAimTargetAssociationDistanceRatio(_aimTargetAssociationDistanceRatio),
                    MaxOffsetRatio = ClampAimTargetMaxOffsetRatio(_aimTargetMaxOffsetRatio)
                }
            };

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            Logger.Error("保存配置文件失败", ex);
        }
    }

    private static float ClampSelfFilterAreaRatio(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 0.018f;
        }

        return Math.Clamp(value, SelfFilterSliderMin / (float)SelfFilterSliderScale, SelfFilterSliderMax / (float)SelfFilterSliderScale);
    }
}