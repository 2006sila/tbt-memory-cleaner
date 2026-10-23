using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using ThinkBookToolkit.PluginUi;
using TbtMemoryCleaner.Core;

namespace TbtMemoryCleaner.Ui;

/// <summary>ToolKit 内存清理页面。手动清理由本进程执行，指标每秒刷新。</summary>
public sealed class MemoryCleanerPage : IToolkitPluginPage
{
    private readonly IMemoryCleaner _cleaner;

    private IPluginPageContext? _context;
    private bool _disposed;
    private bool _busy;
    private bool _suppress = true;
    private bool _dark = true;
    private bool _chinese = true;
    private MemoryArea _mask = MemoryArea.Default;
    private bool _notify = true;
    private MemoryArea _supported = MemoryArea.All;
    private MemorySnapshot? _lastSnapshot;

    private DispatcherTimer? _saveTimer;
    private string? _pendingSetting;
    private JsonElement _pendingValue;

    private Grid _root = null!;
    private TextBlock _title = null!;
    private TextBlock _subtitle = null!;
    private Border _banner = null!;
    private TextBlock _bannerText = null!;

    private StatCard _usageCard = null!;
    private StatCard _availableCard = null!;
    private StatCard _standbyCard = null!;
    private StatCard _modifiedCard = null!;

    private Grid _compositionGrid = null!;
    private readonly ColumnDefinition[] _compositionColumns = new ColumnDefinition[4];
    private readonly Border[] _compositionSegments = new Border[4];
    private readonly TextBlock[] _compositionLegend = new TextBlock[4];

    private Button _cleanButton = null!;
    private TextBlock _status = null!;
    private TextBlock _lastResult = null!;

    private TextBlock _areasTitle = null!;
    private TextBlock _areasHint = null!;
    private readonly List<AreaRow> _areaRows = new();

    private TextBlock _autoTitle = null!;
    private CheckBox _autoCheck = null!;
    private TextBlock _autoCheckLabel = null!;
    private TextBlock _autoHint = null!;
    private TextBlock _thresholdCaption = null!;
    private Slider _threshold = null!;
    private TextBlock _thresholdText = null!;
    private TextBlock _intervalCaption = null!;
    private ComboBox _interval = null!;

    private CheckBox _notifyCheck = null!;
    private TextBlock _notifyLabel = null!;

    private TextBlock _standbyCaption = null!;
    private Slider _standbySlider = null!;
    private TextBlock _standbyText = null!;

    private TextBlock _processTitle = null!;
    private StackPanel _processPanel = null!;
    private int _processTick;

    private TextBlock _evidenceTitle = null!;
    private StackPanel _evidencePanel = null!;
    private TextBlock _evidenceHint = null!;
    private TextBlock _footnote = null!;

    public MemoryCleanerPage()
        : this(new WindowsMemoryCleaner())
    {
    }

    internal MemoryCleanerPage(IMemoryCleaner cleaner) => _cleaner = cleaner;

    public FrameworkElement CreateView(IPluginPageContext context)
    {
        _context = context;
        _dark = context.State.IsDark;
        _chinese = IsChinese(context.State.Language);

        try
        {
            _supported = _cleaner.SupportedAreas;
        }
        catch
        {
            _supported = MemoryArea.All;
        }

        _root = new Grid { Background = Brushes.Transparent };
        TextElement.SetFontFamily(_root, new FontFamily("Microsoft YaHei UI, Segoe UI"));
        BuildResources();
        BuildLayout();
        ApplyLanguage();
        LoadSettings(context.State.Request.PluginSettings);
        RefreshMetrics();
        UpdateBanner();
        _suppress = false;

        return _root;
    }

    public void Update(PluginPageState state)
    {
        if (_disposed) return;

        if (state.IsDark != _dark)
        {
            _dark = state.IsDark;
            BuildResources();
        }

        var chinese = IsChinese(state.Language);
        if (chinese != _chinese)
        {
            _chinese = chinese;
            ApplyLanguage();
        }

        if (!_busy) RefreshMetrics();
        if (++_processTick % 10 == 0) RefreshProcesses();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _saveTimer?.Stop();
        _saveTimer = null;

        if (_cleanButton is not null) _cleanButton.Click -= OnCleanClick;
        if (_autoCheck is not null)
        {
            _autoCheck.Checked -= OnAutoChanged;
            _autoCheck.Unchecked -= OnAutoChanged;
        }

        if (_threshold is not null) _threshold.ValueChanged -= OnThresholdChanged;
        if (_interval is not null) _interval.SelectionChanged -= OnIntervalChanged;
        if (_notifyCheck is not null)
        {
            _notifyCheck.Checked -= OnNotifyChanged;
            _notifyCheck.Unchecked -= OnNotifyChanged;
        }

        foreach (var row in _areaRows)
        {
            row.Box.Checked -= OnAreaChanged;
            row.Box.Unchecked -= OnAreaChanged;
        }

        _areaRows.Clear();
        _context = null;
    }

    private void BuildResources()
    {
        var palette = Palette.For(_dark);
        var resources = _root.Resources;

        resources["Accent"] = Freeze(palette.Accent);
        resources["AccentText"] = Brushes.White;
        resources["CardBg"] = Freeze(palette.Surface);
        resources["CardBd"] = Freeze(palette.Border);
        resources["ChipBg"] = Freeze(palette.SurfaceRaised);
        resources["Track"] = Freeze(palette.SurfaceRaised);
        resources["Text"] = Freeze(palette.Text);
        resources["Muted"] = Freeze(palette.Muted);
        resources["Success"] = Freeze(palette.Success);
        resources["Warning"] = Freeze(palette.Warning);
        resources["Danger"] = Freeze(palette.Danger);
        resources["WarnBg"] = Freeze(palette.Warning, 0.14);
        resources["AccentBg"] = Freeze(palette.Accent, 0.12);

        if (_cleanButton is not null)
        {
            _cleanButton.Background = (Brush)resources["Accent"];
            _cleanButton.Foreground = Brushes.White;
        }
    }

    private void BuildLayout()
    {
        // 不自带 ScrollViewer：Toolkit 会把页面放进主滚动区（_mainScroll）。
        // 嵌套的 ScrollViewer 在无限高度下 ScrollableHeight 恒为 0，却会把滚轮事件吃掉，
        // 表现为「页面滚不动」。交给宿主滚动即可。
        var content = new StackPanel
        {
            Margin = new Thickness(24, 20, 24, 32),
            MaxWidth = 1180,
        };

        _root.Children.Add(content);

        _title = MakeText(26, "Text", FontWeights.SemiBold);
        _subtitle = MakeText(12, "Muted");
        _subtitle.Margin = new Thickness(0, 4, 0, 0);

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(_title);
        header.Children.Add(_subtitle);
        content.Children.Add(header);

        _bannerText = MakeText(12, "Warning");
        _banner = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 12),
            Visibility = Visibility.Collapsed,
            Child = _bannerText,
        };
        _banner.SetResourceReference(Border.BackgroundProperty, "WarnBg");
        _banner.SetResourceReference(Border.BorderBrushProperty, "Warning");
        content.Children.Add(_banner);

        var stats = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        for (var i = 0; i < 4; i++)
            stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _usageCard = CreateStatCard(true);
        _availableCard = CreateStatCard(false);
        _standbyCard = CreateStatCard(false);
        _modifiedCard = CreateStatCard(false);

        AttachStat(stats, _usageCard, 0);
        AttachStat(stats, _availableCard, 1);
        AttachStat(stats, _standbyCard, 2);
        AttachStat(stats, _modifiedCard, 3);
        content.Children.Add(stats);

        var composition = new StackPanel();
        var barHost = new Border
        {
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(0, 0, 0, 8),
            ClipToBounds = true,
        };
        barHost.SetResourceReference(Border.BackgroundProperty, "Track");
        _compositionGrid = new Grid();
        for (var i = 0; i < 4; i++)
        {
            var column = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
            _compositionColumns[i] = column;
            _compositionGrid.ColumnDefinitions.Add(column);

            var segment = new Border();
            segment.SetResourceReference(Border.BackgroundProperty, new[] { "Accent", "Success", "Warning", "Danger" }[i]);
            Grid.SetColumn(segment, i);
            _compositionGrid.Children.Add(segment);
            _compositionSegments[i] = segment;
        }

        barHost.Child = _compositionGrid;
        composition.Children.Add(barHost);

        var legend = new UniformGrid { Rows = 1 };
        for (var i = 0; i < 4; i++)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
            var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            dot.SetResourceReference(Border.BackgroundProperty, new[] { "Accent", "Success", "Warning", "Danger" }[i]);
            var text = MakeText(11, "Muted");
            item.Children.Add(dot);
            item.Children.Add(text);
            legend.Children.Add(item);
            _compositionLegend[i] = text;
        }

        composition.Children.Add(legend);
        content.Children.Add(Card(composition, 14, 14));

        var action = new Grid { Margin = new Thickness(0, 14, 0, 14) };
        action.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        action.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _cleanButton = new Button
        {
            Style = AccentButtonStyle(),
            Padding = new Thickness(24, 10, 24, 10),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            FocusVisualStyle = null,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _cleanButton.SetResourceReference(Control.BackgroundProperty, "Accent");
        _cleanButton.SetResourceReference(Control.ForegroundProperty, "AccentText");
        _cleanButton.Click += OnCleanClick;
        Grid.SetColumn(_cleanButton, 0);
        action.Children.Add(_cleanButton);

        var actionText = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _status = MakeText(13, "Muted");
        _lastResult = MakeText(11, "Muted");
        _lastResult.Margin = new Thickness(0, 3, 0, 0);
        actionText.Children.Add(_status);
        actionText.Children.Add(_lastResult);
        Grid.SetColumn(actionText, 1);
        action.Children.Add(actionText);
        content.Children.Add(action);

        var areas = new StackPanel();
        _areasTitle = SectionTitle();
        areas.Children.Add(_areasTitle);

        foreach (var info in AreaCatalog.All)
        {
            var row = CreateAreaRow(info);
            _areaRows.Add(row);
            areas.Children.Add(row.Root);
        }

        _areasHint = MakeText(11, "Muted");
        _areasHint.Margin = new Thickness(0, 8, 0, 0);
        areas.Children.Add(_areasHint);
        content.Children.Add(Card(areas));

        var auto = new StackPanel();
        _autoTitle = SectionTitle();
        auto.Children.Add(_autoTitle);

        var autoRow = new Grid();
        autoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        autoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _autoCheck = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        _autoCheckLabel = MakeText(13, "Text");
        _autoCheck.Content = _autoCheckLabel;
        _autoCheck.Checked += OnAutoChanged;
        _autoCheck.Unchecked += OnAutoChanged;
        Grid.SetColumn(_autoCheck, 0);
        autoRow.Children.Add(_autoCheck);

        _autoHint = MakeText(11, "Muted");
        _autoHint.VerticalAlignment = VerticalAlignment.Center;
        _autoHint.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_autoHint, 1);
        autoRow.Children.Add(_autoHint);
        auto.Children.Add(autoRow);

        var thresholdRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        thresholdRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        thresholdRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        thresholdRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });

        _thresholdCaption = MakeText(12, "Muted");
        _thresholdCaption.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_thresholdCaption, 0);
        thresholdRow.Children.Add(_thresholdCaption);

        _threshold = new Slider
        {
            Minimum = 50,
            Maximum = 95,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
        };
        _threshold.ValueChanged += OnThresholdChanged;
        Grid.SetColumn(_threshold, 1);
        thresholdRow.Children.Add(_threshold);

        _thresholdText = MakeText(12, "Text");
        _thresholdText.HorizontalAlignment = HorizontalAlignment.Right;
        _thresholdText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_thresholdText, 2);
        thresholdRow.Children.Add(_thresholdText);
        auto.Children.Add(thresholdRow);

        var intervalRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        intervalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        intervalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _intervalCaption = MakeText(12, "Muted");
        _intervalCaption.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_intervalCaption, 0);
        intervalRow.Children.Add(_intervalCaption);

        _interval = new ComboBox
        {
            Width = 150,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 0, 0, 0),
        };
        _interval.SelectionChanged += OnIntervalChanged;
        Grid.SetColumn(_interval, 1);
        intervalRow.Children.Add(_interval);
        auto.Children.Add(intervalRow);

        var standbyRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        standbyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        standbyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        standbyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });

        _standbyCaption = MakeText(12, "Muted");
        _standbyCaption.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_standbyCaption, 0);
        standbyRow.Children.Add(_standbyCaption);

        _standbySlider = new Slider
        {
            Minimum = 0,
            Maximum = 32,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
        };
        _standbySlider.ValueChanged += OnStandbyChanged;
        Grid.SetColumn(_standbySlider, 1);
        standbyRow.Children.Add(_standbySlider);

        _standbyText = MakeText(12, "Text");
        _standbyText.HorizontalAlignment = HorizontalAlignment.Right;
        _standbyText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_standbyText, 2);
        standbyRow.Children.Add(_standbyText);
        auto.Children.Add(standbyRow);

        var notifyRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        _notifyCheck = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        _notifyLabel = MakeText(13, "Text");
        _notifyCheck.Content = _notifyLabel;
        _notifyCheck.Checked += OnNotifyChanged;
        _notifyCheck.Unchecked += OnNotifyChanged;
        notifyRow.Children.Add(_notifyCheck);
        auto.Children.Add(notifyRow);

        content.Children.Add(Card(auto));

        var processes = new StackPanel();
        _processTitle = SectionTitle();
        processes.Children.Add(_processTitle);
        _processPanel = new StackPanel();
        processes.Children.Add(_processPanel);
        content.Children.Add(Card(processes));
        RefreshProcesses();

        var evidence = new StackPanel();
        _evidenceTitle = SectionTitle();
        evidence.Children.Add(_evidenceTitle);

        _evidencePanel = new StackPanel();
        _evidenceHint = MakeText(11, "Muted");
        _evidenceHint.Margin = new Thickness(0, 2, 0, 2);
        _evidencePanel.Children.Add(_evidenceHint);
        evidence.Children.Add(_evidencePanel);
        content.Children.Add(Card(evidence));

        _footnote = MakeText(11, "Muted");
        _footnote.Margin = new Thickness(4, 2, 4, 0);
        content.Children.Add(_footnote);
    }

    private StatCard CreateStatCard(bool withBar)
    {
        var caption = MakeText(11, "Muted");
        var value = MakeText(22, "Text", FontWeights.SemiBold);
        value.Margin = new Thickness(0, 3, 0, 0);

        var panel = new StackPanel();
        panel.Children.Add(caption);
        panel.Children.Add(value);

        ProgressBar? bar = null;
        if (withBar)
        {
            bar = new ProgressBar
            {
                Height = 6,
                Minimum = 0,
                Maximum = 100,
                Margin = new Thickness(0, 9, 0, 0),
                BorderThickness = new Thickness(0),
            };
            bar.SetResourceReference(Control.ForegroundProperty, "Accent");
            bar.SetResourceReference(Control.BackgroundProperty, "Track");
            panel.Children.Add(bar);
        }

        var border = Card(panel, 14, 0);
        return new StatCard { Root = border, Caption = caption, Value = value, Bar = bar };
    }

    private static void AttachStat(Grid grid, StatCard card, int column)
    {
        card.Root.Margin = new Thickness(0, 0, column == 3 ? 0 : 10, 0);
        Grid.SetColumn(card.Root, column);
        grid.Children.Add(card.Root);
    }

    private AreaRow CreateAreaRow(AreaInfo info)
    {
        var root = new Grid { Margin = new Thickness(0, 7, 0, 7) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 10, 0),
            IsEnabled = (_supported & info.Area) != 0,
        };
        box.Checked += OnAreaChanged;
        box.Unchecked += OnAreaChanged;
        Grid.SetColumn(box, 0);
        root.Children.Add(box);

        var label = MakeText(13, "Text");
        var badge = MakeText(10, RiskBrush(info.Risk));
        var badgeBorder = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(8, 0, 0, 0),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = badge,
        };
        badgeBorder.SetResourceReference(Border.BackgroundProperty, "ChipBg");
        badgeBorder.SetResourceReference(Border.BorderBrushProperty, "ChipBd");

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(label);
        titleRow.Children.Add(badgeBorder);

        var note = MakeText(11, "Muted");
        note.Margin = new Thickness(0, 2, 0, 0);

        var text = new StackPanel();
        text.Children.Add(titleRow);
        text.Children.Add(note);
        Grid.SetColumn(text, 1);
        root.Children.Add(text);

        var status = MakeText(11, "Muted");
        status.HorizontalAlignment = HorizontalAlignment.Right;
        status.TextAlignment = TextAlignment.Right;
        status.MinWidth = 64;
        Grid.SetColumn(status, 2);
        root.Children.Add(status);

        return new AreaRow
        {
            Area = info.Area,
            Info = info,
            Root = root,
            Box = box,
            Label = label,
            Badge = badge,
            Note = note,
            Status = status,
        };
    }

    private void LoadSettings(IReadOnlyDictionary<string, JsonElement>? values)
    {
        var settings = MemoryCleanerSettings.From(values);
        _mask = settings.Areas & _supported;

        _suppress = true;
        try
        {
            foreach (var row in _areaRows)
            {
                var enabled = (_supported & row.Area) != 0;
                row.Box.IsEnabled = enabled;
                row.Box.IsChecked = enabled && (_mask & row.Area) != 0;
            }

            _autoCheck.IsChecked = settings.AutoEnabled;
            _notify = settings.Notify;
            _notifyCheck.IsChecked = settings.Notify;
            _threshold.Value = Math.Clamp(settings.Threshold, _threshold.Minimum, _threshold.Maximum);
            _thresholdText.Text = ((int)Math.Round(_threshold.Value)).ToString(CultureInfo.CurrentCulture) + "%";
            _standbySlider.Value = Math.Clamp(settings.StandbyThresholdGb, _standbySlider.Minimum, _standbySlider.Maximum);
            _standbyText.Text = ((int)Math.Round(_standbySlider.Value)).ToString(CultureInfo.CurrentCulture) + " GB";
            SelectInterval(settings.IntervalSeconds);
        }
        finally
        {
            _suppress = false;
        }

        UpdateAreaAvailability();
        UpdateAutoHint();
    }

    private void SelectInterval(double seconds)
    {
        ComboBoxItem? best = null;
        var bestDelta = double.MaxValue;

        foreach (var item in _interval.Items)
        {
            if (item is not ComboBoxItem entry || entry.Tag is not double candidate) continue;
            var delta = Math.Abs(candidate - seconds);
            if (delta >= bestDelta) continue;
            bestDelta = delta;
            best = entry;
        }

        if (best is not null) _interval.SelectedItem = best;
    }

    private void UpdateAreaAvailability()
    {
        foreach (var row in _areaRows)
        {
            if ((_supported & row.Area) == 0 && string.IsNullOrEmpty(row.Status.Text))
                row.Status.Text = L("不支持", "n/a");
        }
    }

    private void UpdateAutoHint()
    {
        if (_autoHint is null) return;

        var safe = _mask & MemoryArea.Safe;
        if (safe == MemoryArea.None)
        {
            _autoHint.Text = L("自动清理只使用安全区域，请至少勾选一个安全区域。",
                "Automatic cleaning only uses safe areas; select at least one.");
            return;
        }

        var threshold = (int)Math.Round(_threshold.Value);
        var standby = (int)Math.Round(_standbySlider.Value);
        var fullStandby = (_mask & MemoryArea.StandbyList) != 0;
        _autoHint.Text = standby > 0
            ? L($"每分钟检查一次；占用 ≥ {threshold}% 或待机 ≥ {standby} GB 时清理。{(fullStandby ? "" : "低优先级待机通常只占很小一部分，想真正降待机请勾选「待机列表（全清）」。")}",
                $"Checked every minute; cleans at {threshold}% usage or {standby} GB standby.{(fullStandby ? "" : " Low-priority standby is only a small slice; tick 'Standby list (full)' to actually reduce it.")}")
            : L($"每分钟检查一次；占用 ≥ {threshold}% 时清理安全区域。",
                $"Checked every minute; cleans safe areas at {threshold}% usage.");
    }

    private async void OnAreaChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress || _disposed) return;

        _mask = ComputeMask();
        UpdateAutoHint();
        await SaveSettingAsync("areas", JsonSerializer.SerializeToElement((double)(uint)_mask));
    }

    private async void OnAutoChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress || _disposed) return;

        await SaveSettingAsync("auto-enabled", JsonSerializer.SerializeToElement(_autoCheck.IsChecked == true));
        UpdateAutoHint();
    }

    private async void OnNotifyChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress || _disposed) return;

        _notify = _notifyCheck.IsChecked == true;
        await SaveSettingAsync("notify", JsonSerializer.SerializeToElement(_notify));
    }

    private async Task NotifyAsync(CleanReport report)
    {
        if (!_notify || _disposed || _context is null) return;

        try
        {
            var failed = report.Failures.Count;
            string text;

            if (failed > 0)
            {
                text = L($"内存清理完成 {report.SuccessCount}/{report.Outcomes.Count}，{failed} 个区域失败",
                    $"Cleaning finished {report.SuccessCount}/{report.Outcomes.Count}, {failed} failed");
            }
            else
            {
                text = CleanSummary.Build(report, _chinese);
            }

            var title = L("TBT 内存清理", "TBT Memory cleaner");
            if (!ShellNotifier.Show(title, text, failed > 0,
                    () => _root.Dispatcher.BeginInvoke(new Action(ActivateApp))))
                await _context.ShowToastAsync(text, failed > 0);
        }
        catch
        {
            // 气泡和宿主提示都不可用时静默忽略。
        }
    }

    private void OnThresholdChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress) return;

        if (_thresholdText is not null)
            _thresholdText.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.CurrentCulture) + "%";

        _pendingSetting = "auto-threshold";
        _pendingValue = JsonSerializer.SerializeToElement(Math.Round(e.NewValue));

        _saveTimer ??= CreateSaveTimer();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnStandbyChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress) return;

        if (_standbyText is not null)
            _standbyText.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.CurrentCulture) + " GB";

        _pendingSetting = "auto-standby-gb";
        _pendingValue = JsonSerializer.SerializeToElement(Math.Round(e.NewValue));

        _saveTimer ??= CreateSaveTimer();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private async void OnIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _disposed) return;
        if (_interval.SelectedItem is not ComboBoxItem item || item.Tag is not double seconds) return;

        await SaveSettingAsync("auto-interval", JsonSerializer.SerializeToElement(seconds));
    }

    private DispatcherTimer CreateSaveTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_pendingSetting is not { } id) return;
            var value = _pendingValue;
            _pendingSetting = null;
            _ = SaveSettingAsync(id, value);
        };
        return timer;
    }

    private async Task SaveSettingAsync(string id, JsonElement value)
    {
        if (_context is null) return;

        try
        {
            await _context.SetSettingAsync(id, value);
        }
        catch
        {
            // 宿主拒绝写入时保持界面不变，下一轮设置会重新下发。
        }
    }

    private MemoryArea ComputeMask()
    {
        var mask = MemoryArea.None;
        foreach (var row in _areaRows)
            if (row.Box.IsChecked == true)
                mask |= row.Area;

        return mask & _supported;
    }

    private async void OnCleanClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _disposed || _context is null) return;

        if (!_cleaner.IsElevated)
        {
            SetStatus(L("需要管理员权限：请以管理员身份运行 ThinkBook Toolkit。",
                "Administrator rights are required. Run ThinkBook Toolkit as administrator."), "Danger");
            return;
        }

        var mask = _mask;
        if (mask == MemoryArea.None)
        {
            SetStatus(L("请至少选择一个清理区域。", "Select at least one area first."), "Warning");
            return;
        }

        _busy = true;
        _cleanButton.IsEnabled = false;
        SetStatus(L("正在清理…", "Cleaning…"), "Accent");

        try
        {
            var token = _context.Lifetime;
            var report = await Task.Run(() => _cleaner.Clean(mask, token), token);
            if (_disposed) return;
            if (!_root.Dispatcher.CheckAccess())
            {
                _ = _root.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ShowReport(report);
                    RefreshMetrics();
                    _ = NotifyAsync(report);
                }));
                return;
            }

            ShowReport(report);
            RefreshMetrics();
            await NotifyAsync(report);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed) OnUi(() => SetStatus(ex.Message, "Danger"));
        }
        finally
        {
            _busy = false;
            OnUi(() => _cleanButton.IsEnabled = _cleaner.IsElevated);
        }
    }

    private void ShowReport(CleanReport report)
    {
        var failures = report.Failures;

        if (failures.Count == 0)
        {
            SetStatus(L($"完成 · {report.SuccessCount} 个区域 · {report.Duration.TotalSeconds:0.00} s",
                $"Done · {report.SuccessCount} areas · {report.Duration.TotalSeconds:0.00} s"), "Success");
        }
        else
        {
            var first = failures[0];
            SetStatus(L($"完成 {report.SuccessCount}/{report.Outcomes.Count}，失败 {failures.Count}（{first.StatusText}）",
                $"Done {report.SuccessCount}/{report.Outcomes.Count}, {failures.Count} failed ({first.StatusText})"), "Warning");
        }

        _lastResult.Text = L($"上次清理 {DateTime.Now:HH:mm:ss}", $"Last clean {DateTime.Now:HH:mm:ss}");

        foreach (var row in _areaRows)
        {
            var outcome = report.OutcomeOf(row.Area);
            if (outcome is not { } result) continue;

            row.Status.Text = result.Success ? "OK" : result.StatusText;
            row.Status.SetResourceReference(TextBlock.ForegroundProperty, result.Success ? "Success" : "Danger");
        }

        ShowEvidence(report);
    }

    private void ShowEvidence(CleanReport report)
    {
        _evidencePanel.Children.Clear();

        var header = new Grid { Margin = new Thickness(0, 2, 0, 6) };
        AddColumns(header);
        PutCell(header, MakeText(11, "Muted", FontWeights.SemiBold), 0, L("指标", "Metric"));
        PutCell(header, MakeText(11, "Muted", FontWeights.SemiBold), 1, L("清理前", "Before"));
        PutCell(header, MakeText(11, "Muted", FontWeights.SemiBold), 2, L("清理后", "After"));
        PutCell(header, MakeText(11, "Muted", FontWeights.SemiBold), 3, L("变化", "Change"));
        _evidencePanel.Children.Add(header);

        AddEvidenceRow(L("工作集（推算）", "Working set (derived)"), WorkingSetBytes(report.Before), WorkingSetBytes(report.After), false);
        AddEvidenceRow(L("空闲内存", "Free memory"), report.Before.FreeBytes, report.After.FreeBytes, true);
        AddEvidenceRow(L("待机缓存", "Standby cache"), report.Before.StandbyBytes, report.After.StandbyBytes, false);
        AddEvidenceRow(L("已修改页", "Modified pages"), report.Before.ModifiedBytes, report.After.ModifiedBytes, false);
        AddEvidenceRow(L("可用内存", "Available memory"), report.Before.AvailableBytes, report.After.AvailableBytes, true);

        var standbyDelta = (long)report.After.StandbyBytes - (long)report.Before.StandbyBytes;
        var modifiedDelta = (long)report.After.ModifiedBytes - (long)report.Before.ModifiedBytes;

        if ((report.Requested & MemoryArea.WorkingSet) != 0 && (standbyDelta > 64L * 1024 * 1024 || modifiedDelta > 64L * 1024 * 1024))
        {
            var note = MakeText(11, "Muted");
            note.Margin = new Thickness(0, 8, 0, 0);
            note.Text = L(
                $"工作集被裁剪：待机 {Signed(standbyDelta)}、已修改 {Signed(modifiedDelta)} 是被裁下来的页面，不是新增占用；已修改页写盘后会回落到待机。不想看到这个中转，可取消勾选「全系统工作集」。",
                $"Working-set pages were trimmed into standby ({Signed(standbyDelta)}) and modified ({Signed(modifiedDelta)}); this is transit, not new usage. Modified pages drain back to standby after being written. Uncheck 'System working sets' to avoid it.");
            _evidencePanel.Children.Add(note);
        }

        var footnote = MakeText(11, "Muted");
        footnote.Margin = new Thickness(0, 6, 0, 0);
        footnote.Text = L(
            "工作集 = 物理总量 − 空闲 − 待机 − 已修改，与资源监视器「使用中」同口径。可用内存 = 空闲 + 待机。",
            "Working set = physical total - free - standby - modified, the same basis as Resource Monitor's 'In use'. Available = free + standby.");
        _evidencePanel.Children.Add(footnote);
    }

    private void AddEvidenceRow(string label, ulong before, ulong after, bool higherIsBetter)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        AddColumns(row);

        var delta = (long)after - (long)before;
        var good = higherIsBetter ? delta >= 0 : delta <= 0;
        var brushKey = delta == 0 ? "Muted" : good ? "Success" : "Warning";

        PutCell(row, MakeText(12, "Text"), 0, label);
        PutCell(row, MakeText(12, "Muted"), 1, FormatBytes(before));
        PutCell(row, MakeText(12, "Text"), 2, FormatBytes(after));
        PutCell(row, MakeText(12, brushKey), 3, (delta >= 0 ? "+" : "-") + FormatBytes((ulong)Math.Abs(delta)));

        _evidencePanel.Children.Add(row);
    }

    private void AddColumns(Grid grid)
    {
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    }

    private static ulong WorkingSetBytes(MemorySnapshot snapshot)
    {
        var lists = snapshot.FreeBytes + snapshot.StandbyBytes + snapshot.ModifiedBytes;
        return snapshot.TotalBytes > lists ? snapshot.TotalBytes - lists : 0;
    }

    private static string Signed(long delta)
    {
        var text = FormatBytes((ulong)Math.Abs(delta));
        return (delta >= 0 ? "+" : "-") + text;
    }

    private static void PutCell(Grid grid, TextBlock block, int column, string text)
    {
        block.Text = text;
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private void RefreshProcesses()
    {
        if (_processPanel is null || _disposed) return;

        _processPanel.Children.Clear();

        IReadOnlyList<ProcessMemoryInfo> items;
        try
        {
            items = ProcessMemoryReader.Top(6);
        }
        catch
        {
            return;
        }

        if (items.Count == 0)
        {
            var empty = MakeText(11, "Muted");
            empty.Text = L("没有超过 50 MB 的进程", "No process above 50 MB");
            _processPanel.Children.Add(empty);
            return;
        }

        foreach (var item in items)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = MakeText(12, "Text");
            name.Text = item.Name + "  #" + item.ProcessId;
            Grid.SetColumn(name, 0);
            row.Children.Add(name);

            var size = MakeText(12, "Muted");
            size.Text = FormatBytes(item.WorkingSetBytes);
            size.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(size, 1);
            row.Children.Add(size);

            var button = new Button
            {
                Content = L("裁剪", "Trim"),
                Style = AccentButtonStyle(),
                Padding = new Thickness(10, 2, 10, 2),
                FontSize = 11,
                Margin = new Thickness(10, 0, 0, 0),
                Cursor = Cursors.Hand,
                FocusVisualStyle = null,
                IsEnabled = _cleaner.IsElevated,
            };
            button.SetResourceReference(Control.BackgroundProperty, "ChipBg");
            button.SetResourceReference(Control.ForegroundProperty, "Text");
            button.Click += (_, _) => TrimProcess(item.ProcessId, item.Name);
            Grid.SetColumn(button, 2);
            row.Children.Add(button);

            _processPanel.Children.Add(row);
        }
    }

    private async void TrimProcess(int processId, string name)
    {
        if (_disposed) return;

        if (!_cleaner.IsElevated)
        {
            SetStatus(L("需要管理员权限才能裁剪进程工作集。", "Administrator rights are required to trim processes."), "Danger");
            return;
        }

        var (ok, error) = await Task.Run(() =>
        {
            var success = ProcessTrimmer.Trim(processId, out var message);
            return (success, message);
        });

        if (_disposed) return;

        SetStatus(ok
                ? L($"{name} 工作集已裁剪", $"{name} working set trimmed")
                : (error ?? L("裁剪失败", "Trim failed")),
            ok ? "Success" : "Warning");

        RefreshProcesses();
    }

    private void RefreshMetrics()
    {
        MemorySnapshot snapshot;
        try
        {
            snapshot = _cleaner.Sample();
        }
        catch
        {
            return;
        }

        _lastSnapshot = snapshot;
        _usageCard.Caption.Text = L("内存占用", "Memory usage");
        _usageCard.Value.Text = snapshot.LoadPercent.ToString(CultureInfo.CurrentCulture) + "%";
        if (_usageCard.Bar is not null) _usageCard.Bar.Value = snapshot.LoadPercent;

        _availableCard.Value.Text = FormatBytes(snapshot.AvailableBytes);
        _standbyCard.Value.Text = snapshot.ListAvailable ? FormatBytes(snapshot.StandbyBytes) : "--";
        _modifiedCard.Value.Text = snapshot.ListAvailable ? FormatBytes(snapshot.ModifiedBytes) : "--";

        var workingSet = WorkingSetBytes(snapshot);
        var parts = new[] { workingSet, snapshot.FreeBytes, snapshot.StandbyBytes, snapshot.ModifiedBytes };
        for (var i = 0; i < 4; i++)
        {
            _compositionColumns[i].Width = new GridLength(parts[i] == 0 ? 0d : (double)parts[i], GridUnitType.Star);
            _compositionLegend[i].Text = i switch
            {
                0 => L($"工作集 {FormatBytes(workingSet)}", $"Working set {FormatBytes(workingSet)}"),
                1 => L($"空闲 {FormatBytes(snapshot.FreeBytes)}", $"Free {FormatBytes(snapshot.FreeBytes)}"),
                2 => L($"待机 {FormatBytes(snapshot.StandbyBytes)}", $"Standby {FormatBytes(snapshot.StandbyBytes)}"),
                _ => L($"已修改 {FormatBytes(snapshot.ModifiedBytes)}", $"Modified {FormatBytes(snapshot.ModifiedBytes)}"),
            };
        }
    }

    private void UpdateBanner()
    {
        string? message = null;

        if (!_cleaner.IsElevated)
        {
            message = L("ThinkBook Toolkit 未以管理员身份运行：内存列表清理不可用。请以管理员身份重启 Toolkit。",
                "ThinkBook Toolkit is not elevated: memory-list cleaning is unavailable. Restart it as administrator.");
        }
        else if (_cleaner.PrivilegeError is { } error)
        {
            message = error;
        }

        _banner.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        _bannerText.Text = message ?? string.Empty;
    }

    private static void ActivateApp()
    {
        try
        {
            var window = Application.Current?.MainWindow;
            if (window is null) return;
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }
        catch
        {
            // 激活失败不影响通知本身。
        }
    }

    private void OnUi(Action action)
    {
        if (_disposed) return;
        if (!_root.Dispatcher.CheckAccess())
        {
            _root.Dispatcher.BeginInvoke(action);
            return;
        }

        action();
    }

    private void SetStatus(string text, string brushKey)
    {
        _status.Text = text;
        _status.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
    }

    private void ApplyLanguage()
    {
        _title.Text = L("内存清理", "Memory cleaner");
        _subtitle.Text = L("基于 Windows 原生内存列表 API。不写盘、不联网、不修改系统设置。",
            "Built on the native Windows memory-list API. No disk writes, no network, no system changes.");

        _cleanButton.Content = L("立即清理", "Clean now");
        _usageCard.Caption.Text = L("内存占用", "Memory usage");
        _availableCard.Caption.Text = L("可用内存", "Available");
        _standbyCard.Caption.Text = L("待机缓存", "Standby cache");
        _modifiedCard.Caption.Text = L("已修改页", "Modified pages");

        _areasTitle.Text = L("清理区域", "Cleanup areas");
        _areasHint.Text = L("「高风险」区域可能让系统卡顿数秒；自动清理永远不会使用它们。",
            "High-risk areas can stall the system for seconds; automatic cleaning never uses them.");

        foreach (var row in _areaRows)
        {
            row.Label.Text = _chinese ? row.Info.Chinese : row.Info.English;
            row.Note.Text = _chinese ? row.Info.NoteChinese : row.Info.NoteEnglish;
            row.Badge.Text = RiskText(row.Info.Risk);
        }

        _processTitle.Text = L("占用最多的进程", "Top memory processes");
        _autoTitle.Text = L("自动清理", "Automatic cleaning");
        _autoCheckLabel.Text = L("超过阈值时自动清理", "Clean automatically above threshold");
        _thresholdCaption.Text = L("触发阈值", "Threshold");
        _intervalCaption.Text = L("最短间隔", "Minimum interval");
        _standbyCaption.Text = L("待机阈值", "Standby limit");
        _notifyLabel.Text = L("清理完成后显示系统通知", "Show a system notification after cleaning");

        _evidenceTitle.Text = L("清理前后（真实读数）", "Before / after (measured)");
        _evidenceHint.Text = L("点击「立即清理」后，这里显示清理前后的空闲、待机、已修改与可用内存。",
            "After a cleanup this table shows free, standby, modified and available memory before and after.");
        _footnote.Text = L("清理只是把缓存页还给空闲列表，并不等于变快；低内存下启动大型程序前使用一次最有意义。",
            "Cleaning only returns cached pages to the free list; it does not make the system faster. It helps most before launching a large app on a low-memory machine.");

        if (string.IsNullOrEmpty(_lastResult.Text)) _lastResult.Text = L("尚未清理", "Not cleaned yet");
        if (string.IsNullOrEmpty(_status.Text)) _status.Text = L("就绪", "Ready");

        RebuildIntervalItems();
        UpdateBanner();
        UpdateAutoHint();

        if (_lastSnapshot is { } snapshot) RefreshMetrics();
    }

    private void RebuildIntervalItems()
    {
        if (_interval is null) return;

        var previous = (_interval.SelectedItem as ComboBoxItem)?.Tag is double seconds ? seconds : (double?)null;

        _suppress = true;
        try
        {
            _interval.Items.Clear();
            foreach (var minutes in new[] { 1, 5, 10, 30, 60 })
            {
                _interval.Items.Add(new ComboBoxItem
                {
                    Content = _chinese ? $"{minutes} 分钟" : $"{minutes} min",
                    Tag = (double)(minutes * 60),
                });
            }

            if (previous is { } value) SelectInterval(value);
        }
        finally
        {
            _suppress = false;
        }
    }

    private TextBlock MakeText(double size, string brushKey, FontWeight? weight = null)
    {
        var block = new TextBlock
        {
            FontSize = size,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (weight is { } value) block.FontWeight = value;
        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return block;
    }

    private TextBlock SectionTitle()
    {
        var block = MakeText(14, "Text", FontWeights.SemiBold);
        block.Margin = new Thickness(0, 0, 0, 8);
        return block;
    }

    private Border Card(UIElement child, double padding = 16, double marginBottom = 12)
    {
        var border = new Border
        {
            Padding = new Thickness(padding),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, marginBottom),
            Child = child,
        };
        border.SetResourceReference(Border.BackgroundProperty, "CardBg");
        border.SetResourceReference(Border.BorderBrushProperty, "CardBd");
        return border;
    }

    private string RiskText(AreaRisk risk) => risk switch
    {
        AreaRisk.Safe => L("安全", "Safe"),
        AreaRisk.Caution => L("谨慎", "Caution"),
        _ => L("高风险", "High risk"),
    };

    private static string RiskBrush(AreaRisk risk) => risk switch
    {
        AreaRisk.Safe => "Success",
        AreaRisk.Caution => "Warning",
        _ => "Danger",
    };

    private string L(string chinese, string english) => _chinese ? chinese : english;

    private static bool IsChinese(string? language) => language is not "en-US";

    private static string FormatBytes(ulong bytes)
    {
        const double gigabyte = 1073741824d;
        const double megabyte = 1048576d;

        return bytes >= gigabyte
            ? (bytes / gigabyte).ToString("0.00", CultureInfo.CurrentCulture) + " GB"
            : (bytes / megabyte).ToString("0", CultureInfo.CurrentCulture) + " MB";
    }

    private static SolidColorBrush Freeze(Color color, double opacity = 1d)
    {
        var brush = new SolidColorBrush(opacity >= 1d ? color : Color.FromArgb((byte)Math.Clamp(opacity * 255d, 0, 255), color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static Style AccentButtonStyle()
    {
        const string xaml = """
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             TargetType="{x:Type Button}">
              <Border x:Name="Bd" CornerRadius="8" Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Bd" Property="Opacity" Value="0.90"/></Trigger>
                <Trigger Property="IsPressed" Value="True"><Setter TargetName="Bd" Property="Opacity" Value="0.72"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Bd" Property="Opacity" Value="0.40"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """;

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(xaml)));
        return style;
    }

    private sealed class StatCard
    {
        public Border Root = null!;
        public TextBlock Caption = null!;
        public TextBlock Value = null!;
        public ProgressBar? Bar;
    }

    private sealed class AreaRow
    {
        public MemoryArea Area;
        public AreaInfo Info = null!;
        public Grid Root = null!;
        public CheckBox Box = null!;
        public TextBlock Label = null!;
        public TextBlock Badge = null!;
        public TextBlock Note = null!;
        public TextBlock Status = null!;
    }
}
