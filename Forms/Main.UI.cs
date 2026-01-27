namespace anqrwzui;

public partial class Main
{
    private void InitializeCaptureComponents()
    {
        Logger.Debug("初始化截取组件");

        this.Text = "Anqrwzui";
        this.ClientSize = new Size(800, 600);
        this.StartPosition = FormStartPosition.CenterScreen;

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

        panel.Controls.AddRange(new Control[] { _toggleCaptureButton, _deviceLabel, _fpsLabel });
        InitializeConfigSelectors(panel);
        this.Controls.Add(panel);
        this.Controls.SetChildIndex(panel, 0);

        Logger.Debug("截取组件初始化完成");
    }

    private void InitializeConfigSelectors(FlowLayoutPanel panel)
    {
        _firstPrimaryCombo = CreatePrimaryComboBox();
        _firstSecondaryCombo = CreateSecondaryComboBox();
        _secondPrimaryCombo = CreatePrimaryComboBox();
        _secondSecondaryCombo = CreateSecondaryComboBox();

        panel.Controls.AddRange(new Control[]
        {
            _firstPrimaryCombo!, _firstSecondaryCombo!, _secondPrimaryCombo!, _secondSecondaryCombo!
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
}