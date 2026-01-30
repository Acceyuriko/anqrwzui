using System.IO;
using System.Linq;
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

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.LightGray,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(10, 8, 10, 8)
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70f));

        var rightLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.LightGray
        };
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var leftLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 0)
        };

        var rightTopRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0)
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

        rightTopRow.Controls.AddRange(new Control[] { _toggleCaptureButton, _deviceLabel, _fpsLabel });
        leftLayout.Controls.Add(_activeComboLabel);

        rightLayout.Controls.Add(rightTopRow, 0, 0);
        rightLayout.Controls.Add(_pictureBox, 0, 1);

        mainLayout.Controls.Add(leftLayout, 0, 0);
        mainLayout.Controls.Add(rightLayout, 1, 0);

        InitializeConfigSelectors(leftLayout);
        this.Controls.Add(mainLayout);
        this.Controls.SetChildIndex(mainLayout, 0);

        LoadSelectionState();

        Logger.Debug("截取组件初始化完成");
    }

    private void InitializeConfigSelectors(FlowLayoutPanel panel)
    {
        _firstComboGroupPanel = CreateComboGroupPanel();
        _secondComboGroupPanel = CreateComboGroupPanel();
        _firstOptionGroupPanel = CreateOptionGroupPanel(1);
        _secondOptionGroupPanel = CreateOptionGroupPanel(2);

        _firstComboGroupPanel.Controls.Add(_firstOptionGroupPanel);
        _secondComboGroupPanel.Controls.Add(_secondOptionGroupPanel);

        panel.Controls.AddRange(new Control[]
        {
            _firstComboGroupPanel, _secondComboGroupPanel
        });

        PopulateOptionGroup(_firstOptionGroupPanel, 1, null);
        PopulateOptionGroup(_secondOptionGroupPanel, 2, null);

        SetActiveComboGroup(1);
    }

    private FlowLayoutPanel CreateComboGroupPanel()
    {
        return new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.TopDown,
            Margin = new Padding(0, 8, 0, 8)
        };
    }

    private FlowLayoutPanel CreateOptionGroupPanel(int groupIndex)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            Tag = groupIndex,
            Margin = new Padding(0, 0, 0, 0),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        return panel;
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
    private void PopulateOptionGroup(FlowLayoutPanel? groupPanel, int groupIndex, string? preferredKey)
    {
        if (groupPanel == null)
        {
            return;
        }

        groupPanel.SuspendLayout();
        groupPanel.Controls.Clear();
        foreach (var option in _configOptions)
        {
            var radio = new RadioButton
            {
                AutoSize = true,
                Text = string.Empty,
                Tag = option,
                Margin = new Padding(0, 2, 4, 2)
            };
            radio.CheckedChanged += OptionRadio_CheckedChanged;

            var label = new Label
            {
                AutoSize = true,
                Text = option.Key,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 4, 8, 2)
            };

            var rowPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 0)
            };
            rowPanel.Controls.Add(radio);
            rowPanel.Controls.Add(label);

            if (preferredKey != null && string.Equals(preferredKey, option.Key, StringComparison.OrdinalIgnoreCase))
            {
                radio.Checked = true;
            }

            groupPanel.Controls.Add(rowPanel);
        }

        if (groupPanel.Controls.Count > 0 && !GetGroupRadios(groupPanel).Any(r => r.Checked))
        {
            GetGroupRadios(groupPanel).First().Checked = true;
        }

        groupPanel.ResumeLayout();
    }

    private void OptionRadio_CheckedChanged(object? sender, EventArgs e)
    {
        if (sender is not RadioButton radio || !radio.Checked)
        {
            return;
        }

        if (radio.Tag is not ConfigOption option)
        {
            return;
        }

        var groupPanel = radio.Parent?.Parent as FlowLayoutPanel;
        if (groupPanel != null)
        {
            foreach (var other in GetGroupRadios(groupPanel))
            {
                if (!ReferenceEquals(other, radio) && other.Checked)
                {
                    other.Checked = false;
                }
            }
        }

        var groupIndex = groupPanel?.Tag as int? ?? 0;
        if (groupIndex == _activeComboGroup)
        {
            SetDownMovePixels(option.Value);
        }
    }

    private void RefreshOptionSelections()
    {
        var firstSelected = GetSelectedOptionKey(_firstOptionGroupPanel);
        var secondSelected = GetSelectedOptionKey(_secondOptionGroupPanel);

        PopulateOptionGroup(_firstOptionGroupPanel, 1, firstSelected);
        PopulateOptionGroup(_secondOptionGroupPanel, 2, secondSelected);
    }

    private string? GetSelectedOptionKey(FlowLayoutPanel? groupPanel)
    {
        var radio = GetGroupRadios(groupPanel).FirstOrDefault(r => r.Checked);
        return radio?.Tag is ConfigOption option ? option.Key : null;
    }

    private double? GetSelectedOptionValue(int groupIndex)
    {
        var panel = groupIndex switch
        {
            1 => _firstOptionGroupPanel,
            2 => _secondOptionGroupPanel,
            _ => null
        };

        var radio = GetGroupRadios(panel).FirstOrDefault(r => r.Checked);
        if (radio?.Tag is ConfigOption option)
        {
            return option.Value;
        }

        return null;
    }

    private void MoveSelectionInGroup(int groupIndex, int delta)
    {
        var panel = groupIndex switch
        {
            1 => _firstOptionGroupPanel,
            2 => _secondOptionGroupPanel,
            _ => null
        };

        if (panel == null)
        {
            return;
        }

        var radios = GetGroupRadios(panel).ToList();
        if (radios.Count == 0)
        {
            return;
        }

        var currentIndex = radios.FindIndex(r => r.Checked);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var nextIndex = (currentIndex + delta) % radios.Count;
        if (nextIndex < 0)
        {
            nextIndex += radios.Count;
        }
        if (nextIndex != currentIndex)
        {
            radios[nextIndex].Checked = true;
        }
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

            ApplyOptionSelection(_firstOptionGroupPanel, state.FirstOption);
            ApplyOptionSelection(_secondOptionGroupPanel, state.SecondOption);
            SetActiveComboGroup(state.ActiveGroup);
            Logger.Info("已恢复上次的选项");
        }
        catch (Exception ex)
        {
            Logger.Warning($"恢复上次选择失败: {ex.Message}");
        }
    }

    private void ApplyOptionSelection(FlowLayoutPanel? groupPanel, string? optionKey)
    {
        if (groupPanel == null || string.IsNullOrWhiteSpace(optionKey))
        {
            return;
        }

        foreach (var radio in GetGroupRadios(groupPanel))
        {
            if (radio.Tag is ConfigOption option && string.Equals(option.Key, optionKey, StringComparison.OrdinalIgnoreCase))
            {
                radio.Checked = true;
                return;
            }
        }
    }

    private IEnumerable<RadioButton> GetGroupRadios(FlowLayoutPanel? groupPanel)
    {
        if (groupPanel == null)
        {
            return Enumerable.Empty<RadioButton>();
        }

        return groupPanel.Controls
            .OfType<FlowLayoutPanel>()
            .SelectMany(p => p.Controls.OfType<RadioButton>());
    }

    private void SaveSelectionState()
    {
        try
        {
            var state = new SelectionState
            {
                FirstOption = GetSelectedOptionKey(_firstOptionGroupPanel),
                SecondOption = GetSelectedOptionKey(_secondOptionGroupPanel),
                ActiveGroup = _activeComboGroup
            };

            var dir = Path.GetDirectoryName(_selectionStatePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_selectionStatePath, json);
            Logger.Info("已保存当前选项");
        }
        catch (Exception ex)
        {
            Logger.Warning($"保存下拉选项失败: {ex.Message}");
        }
    }

    private class SelectionState
    {
        public string? FirstOption { get; set; }
        public string? SecondOption { get; set; }
        public int ActiveGroup { get; set; } = 1;
    }
}