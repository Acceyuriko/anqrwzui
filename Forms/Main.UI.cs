using System.IO;
using System.Text.Json;

namespace anqrwzui;

public partial class Main
{
    private void InitializeCaptureComponents()
    {
        Logger.Debug("初始化截取组件");

        this.Text = "Anqrwzui";
        this.ClientSize = new Size(800, 600);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.TopMost = true;

        _pictureBox = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Fill,
            BackColor = Color.Black
        };
        this.Controls.Add(_pictureBox);

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 52,
            BackColor = Color.LightGray,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(10, 8, 10, 8)
        };

        _toggleCaptureButton = new Button
        {
            Text = "开始",
            Size = new Size(96, 28),
            Margin = new Padding(0, 0, 12, 0)
        };
        _toggleCaptureButton.Click += ToggleCapture_Click;

        _deviceLabel = new Label
        {
            Text = "推理设备: 未知",
            AutoSize = true,
            ForeColor = Color.DarkBlue,
            Margin = new Padding(0, 6, 18, 0)
        };
        _fpsLabel = new Label
        {
            Text = "检测FPS: -",
            AutoSize = true,
            ForeColor = Color.Black,
            Margin = new Padding(0, 6, 0, 0)
        };

        _activeComboLabel = new Label
        {
            Text = "当前: 1",
            AutoSize = true,
            ForeColor = Color.DarkGreen,
            Margin = new Padding(10, 6, 12, 0)
        };

        panel.Controls.AddRange(new Control[] { _toggleCaptureButton, _deviceLabel, _fpsLabel, _activeComboLabel });
        InitializeConfigSelectors(panel);
        this.Controls.Add(panel);
        this.Controls.SetChildIndex(panel, 0);

        LoadSelectionState();

        Logger.Debug("截取组件初始化完成");
    }

    private void InitializeConfigSelectors(FlowLayoutPanel panel)
    {
        _firstPrimaryCombo = CreatePrimaryComboBox();
        _firstSecondaryCombo = CreateSecondaryComboBox();
        _secondPrimaryCombo = CreatePrimaryComboBox();
        _secondSecondaryCombo = CreateSecondaryComboBox();

        _firstComboGroupPanel = CreateComboGroupPanel();
        _secondComboGroupPanel = CreateComboGroupPanel();

        AttachHotkeySuppress(_firstPrimaryCombo);
        AttachHotkeySuppress(_firstSecondaryCombo);
        AttachHotkeySuppress(_secondPrimaryCombo);
        AttachHotkeySuppress(_secondSecondaryCombo);

        _firstComboGroupPanel.Controls.AddRange(new Control[] { _firstPrimaryCombo!, _firstSecondaryCombo! });
        _secondComboGroupPanel.Controls.AddRange(new Control[] { _secondPrimaryCombo!, _secondSecondaryCombo! });

        panel.Controls.AddRange(new Control[]
        {
            _firstComboGroupPanel, _secondComboGroupPanel
        });

        PopulatePrimaryOptions(_firstPrimaryCombo);
        PopulatePrimaryOptions(_secondPrimaryCombo);

        _firstPrimaryCombo.SelectedIndexChanged += (_, _) => UpdateSecondaryOptions(_firstPrimaryCombo, _firstSecondaryCombo);
        _secondPrimaryCombo.SelectedIndexChanged += (_, _) => UpdateSecondaryOptions(_secondPrimaryCombo, _secondSecondaryCombo);

        if (_firstPrimaryCombo.Items.Count > 0)
        {
            _firstPrimaryCombo.SelectedIndex = 0;
            UpdateSecondaryOptions(_firstPrimaryCombo, _firstSecondaryCombo);
        }

        if (_secondPrimaryCombo.Items.Count > 0)
        {
            _secondPrimaryCombo.SelectedIndex = 0;
            UpdateSecondaryOptions(_secondPrimaryCombo, _secondSecondaryCombo);
        }

        SetActiveComboGroup(1);
    }

    private ComboBox CreatePrimaryComboBox()
    {
        return new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 120,
            Margin = new Padding(8, 2, 0, 0)
        };
    }

    private ComboBox CreateSecondaryComboBox()
    {
        return new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 90,
            Margin = new Padding(6, 2, 0, 0)
        };
    }

    private FlowLayoutPanel CreateComboGroupPanel()
    {
        return new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 12, 0)
        };
    }

    private void PopulatePrimaryOptions(ComboBox? primaryCombo)
    {
        if (primaryCombo == null) return;

        primaryCombo.BeginUpdate();
        primaryCombo.Items.Clear();
        foreach (var key in _configOptions.Keys.OrderBy(k => k))
        {
            primaryCombo.Items.Add(key);
        }
        primaryCombo.EndUpdate();
    }

    private void AttachHotkeySuppress(ComboBox? combo)
    {
        if (combo == null)
        {
            return;
        }

        combo.KeyDown += SuppressHotkeyOnCombo;
    }

    private void SuppressHotkeyOnCombo(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is Keys.D1 or Keys.NumPad1 or Keys.D2 or Keys.NumPad2 or Keys.D3 or Keys.NumPad3)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void SetActiveComboGroup(int groupIndex)
    {
        _activeComboGroup = groupIndex switch
        {
            2 => 2,
            1 => 1,
            _ => 0
        };

        if (_firstComboGroupPanel != null)
        {
            _firstComboGroupPanel.Visible = _activeComboGroup == 1;
        }

        if (_secondComboGroupPanel != null)
        {
            _secondComboGroupPanel.Visible = _activeComboGroup == 2;
        }

        if (_activeComboLabel != null)
        {
            if (_activeComboGroup == 0)
            {
                _activeComboLabel.Visible = false;
            }
            else
            {
                _activeComboLabel.Visible = true;
                _activeComboLabel.Text = $"当前: {_activeComboGroup}";
            }
        }
    }

    private void UpdateSecondaryOptions(ComboBox? primaryCombo, ComboBox? secondaryCombo)
    {
        UpdateSecondaryOptions(primaryCombo, secondaryCombo, null);
    }

    private void UpdateSecondaryOptions(ComboBox? primaryCombo, ComboBox? secondaryCombo, string? preferredSecondary)
    {
        if (primaryCombo?.SelectedItem is not string primaryKey || secondaryCombo == null)
        {
            return;
        }

        secondaryCombo.BeginUpdate();
        secondaryCombo.Items.Clear();

        if (_configOptions.TryGetValue(primaryKey, out var secondaryOptions))
        {
            foreach (var key in secondaryOptions.Keys.OrderBy(k => k))
            {
                secondaryCombo.Items.Add(key);
            }

            if (secondaryCombo.Items.Count > 0)
            {
                if (preferredSecondary != null && secondaryCombo.Items.Contains(preferredSecondary))
                {
                    secondaryCombo.SelectedItem = preferredSecondary;
                }
                else
                {
                    secondaryCombo.SelectedIndex = 0;
                }
            }
        }

        secondaryCombo.EndUpdate();
    }

    private void RefreshComboSelections()
    {
        RefreshComboPair(_firstPrimaryCombo, _firstSecondaryCombo);
        RefreshComboPair(_secondPrimaryCombo, _secondSecondaryCombo);
    }

    private void RefreshComboPair(ComboBox? primaryCombo, ComboBox? secondaryCombo)
    {
        if (primaryCombo == null || secondaryCombo == null)
        {
            return;
        }

        var previousPrimary = primaryCombo.SelectedItem as string;
        var previousSecondary = secondaryCombo.SelectedItem as string;

        PopulatePrimaryOptions(primaryCombo);

        if (previousPrimary != null && primaryCombo.Items.Contains(previousPrimary))
        {
            primaryCombo.SelectedItem = previousPrimary;
        }
        else if (primaryCombo.Items.Count > 0)
        {
            primaryCombo.SelectedIndex = 0;
        }

        UpdateSecondaryOptions(primaryCombo, secondaryCombo, previousSecondary);
    }

    private void LoadSelectionState()
    {
        try
        {
            if (!File.Exists(_selectionStatePath))
            {
                return;
            }

            var json = File.ReadAllText(_selectionStatePath);
            var state = JsonSerializer.Deserialize<SelectionState>(json);
            if (state == null)
            {
                return;
            }

            ApplySelection(_firstPrimaryCombo, _firstSecondaryCombo, state.FirstPrimary, state.FirstSecondary);
            ApplySelection(_secondPrimaryCombo, _secondSecondaryCombo, state.SecondPrimary, state.SecondSecondary);
            Logger.Info("已恢复上次的下拉选项");
        }
        catch (Exception ex)
        {
            Logger.Warning($"恢复上次选择失败: {ex.Message}");
        }
    }

    private void ApplySelection(ComboBox? primaryCombo, ComboBox? secondaryCombo, string? primaryValue, string? secondaryValue)
    {
        if (primaryCombo == null || secondaryCombo == null)
        {
            return;
        }

        if (primaryValue != null && primaryCombo.Items.Contains(primaryValue))
        {
            primaryCombo.SelectedItem = primaryValue;
        }

        UpdateSecondaryOptions(primaryCombo, secondaryCombo, secondaryValue);
    }

    private void SaveSelectionState()
    {
        try
        {
            var state = new SelectionState
            {
                FirstPrimary = _firstPrimaryCombo?.SelectedItem as string,
                FirstSecondary = _firstSecondaryCombo?.SelectedItem as string,
                SecondPrimary = _secondPrimaryCombo?.SelectedItem as string,
                SecondSecondary = _secondSecondaryCombo?.SelectedItem as string
            };

            var dir = Path.GetDirectoryName(_selectionStatePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_selectionStatePath, json);
            Logger.Info("已保存当前下拉选项");
        }
        catch (Exception ex)
        {
            Logger.Warning($"保存下拉选项失败: {ex.Message}");
        }
    }

    private class SelectionState
    {
        public string? FirstPrimary { get; set; }
        public string? FirstSecondary { get; set; }
        public string? SecondPrimary { get; set; }
        public string? SecondSecondary { get; set; }
    }
}