using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Stem.Windows;

public partial class MainWindow : Window
{
    private abstract class PaneNode;

    private sealed class PaneLeaf(TerminalPane pane) : PaneNode
    {
        public TerminalPane Pane { get; } = pane;
    }

    private sealed class PaneBranch(bool sideBySide, PaneNode first, PaneNode second) : PaneNode
    {
        public bool SideBySide { get; } = sideBySide;
        public PaneNode First { get; set; } = first;
        public PaneNode Second { get; set; } = second;
    }

    private sealed class TerminalPane : IDisposable
    {
        public required int Id { get; init; }
        public required TerminalView Terminal { get; init; }
        public required Border Surface { get; init; }
        public required TextBlock Badge { get; init; }
        public required StemProfile Profile { get; init; }
        public ConPtySession? Session { get; set; }
        public string DisplayName { get; set; } = "PowerShell";
        public string WindowTitle { get; set; } = "PowerShell";
        public string StatusText { get; set; } = "STARTING";
        public Color StatusColor { get; set; } = Color.FromRgb(228, 184, 86);
        public bool Started { get; set; }
        public bool Exited { get; set; }

        public void Dispose()
        {
            Session?.Dispose();
            Session = null;
        }
    }

    private sealed class SessionTab : IDisposable
    {
        public required int Id { get; init; }
        public required Border TabElement { get; init; }
        public required TextBlock TabTitle { get; init; }
        public PaneNode Root { get; set; } = null!;
        public FrameworkElement LayoutVisual { get; set; } = null!;
        public List<TerminalPane> Panes { get; } = [];
        public TerminalPane ActivePane { get; set; } = null!;
        public int NextPaneId { get; set; } = 1;
        public TerminalPane? ZoomedPane { get; set; }

        public void Dispose()
        {
            foreach (var pane in Panes)
            {
                pane.Dispose();
            }
            Panes.Clear();
        }
    }

    private StemSettings _settings;
    private readonly List<SessionTab> _tabs = [];
    private IReadOnlyList<StemProfile> _profiles = [];
    private SessionTab? _activeTab;
    private DateTime _configWriteTimeUtc;
    private int _nextTabId = 1;
    private bool _started;
    private bool _closingConfirmed;

    private TerminalPane? ActivePane => _activeTab?.ActivePane;
    private TerminalView? ActiveTerminal => ActivePane?.Terminal;

    public MainWindow()
    {
        InitializeComponent();
        DarkWindowTheme.Apply(this);
        _settings = StemSettings.Load();
        _profiles = StemProfileCatalog.Discover(_settings);
        var initialTab = CreateTab(startSession: false);
        ActivateTab(initialTab);
        ApplySettings(_settings, initial: true);
        _configWriteTimeUtc = ConfigWriteTimeUtc();

        Loaded += OnLoaded;
        Activated += OnActivated;
        Closing += OnClosing;
        PreviewKeyDown += OnWindowPreviewKeyDown;
        UpdateWindowChrome();
    }

    private StemProfile DefaultProfile() => StemProfileCatalog.Default(_settings, _profiles);

    private SessionTab CreateTab(bool startSession, StemProfile? profile = null)
    {
        var id = _nextTabId++;
        var title = new TextBlock
        {
            Text = $"{id}  POWERSHELL",
            Margin = new Thickness(8, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 8.5,
            FontWeight = FontWeights.SemiBold
        };
        var select = new Button
        {
            Tag = id,
            Content = title,
            Height = 24,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FocusVisualStyle = null
        };
        select.Click += OnTabSelectClick;

        var close = new Button
        {
            Tag = id,
            Content = "x",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(126, 137, 155)),
            FontSize = 10,
            ToolTip = "Close tab"
        };
        close.Click += OnTabCloseClick;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(select);
        row.Children.Add(close);
        var chip = new Border
        {
            Height = 26,
            Margin = new Thickness(2, 0, 0, 0),
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            Child = row
        };

        var tab = new SessionTab
        {
            Id = id,
            TabElement = chip,
            TabTitle = title
        };
        _tabs.Add(tab);
        TabStrip.Children.Add(chip);
        chip.ContextMenu = BuildTabContextMenu(tab);

        var pane = CreatePane(tab, startSession: false, profile ?? DefaultProfile());
        tab.Root = new PaneLeaf(pane);
        tab.ActivePane = pane;
        RebuildTabLayout(tab);
        UpdateTabTitle(tab);
        UpdateTabStyles();

        if (startSession && IsLoaded)
        {
            StartPaneSession(tab, pane);
        }
        return tab;
    }

    private ContextMenu BuildTabContextMenu(SessionTab tab)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuAction("New tab", "Ctrl+Shift+T", OnNewTabClick));
        menu.Items.Add(MenuAction("Split right", "Alt+Shift+Plus", OnSplitRightClick));
        menu.Items.Add(MenuAction("Split down", "Alt+Shift+Minus", OnSplitDownClick));
        menu.Items.Add(new Separator());
        var close = MenuAction("Close tab", "Ctrl+Shift+W", (_, _) => CloseTab(tab.Id));
        menu.Items.Add(close);
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("Settings...", string.Empty, OnSettingsClick));
        menu.Opened += (_, _) =>
        {
            ActivateTab(tab);
            close.IsEnabled = _tabs.Count > 1 || tab.Panes.Count > 0;
        };
        return menu;
    }

    private TerminalPane CreatePane(SessionTab tab, bool startSession, StemProfile profile)
    {
        var paneId = tab.NextPaneId++;
        var terminal = new TerminalView
        {
            Background = new SolidColorBrush(Color.FromRgb(5, 7, 12)),
            Foreground = new SolidColorBrush(Color.FromRgb(216, 218, 212))
        };
        terminal.ApplySettings(_settings);

        var badgeText = new TextBlock
        {
            Text = $"P{paneId}",
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(196, 167, 255))
        };
        var badge = new Border
        {
            Margin = new Thickness(0, 6, 8, 0),
            Padding = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(78, 58, 121)),
            Background = new SolidColorBrush(Color.FromRgb(24, 18, 42)),
            IsHitTestVisible = false,
            Child = badgeText
        };
        var content = new Grid();
        content.Children.Add(terminal);
        content.Children.Add(badge);
        var surface = new Border
        {
            Margin = new Thickness(1),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(32, 43, 61)),
            Background = new SolidColorBrush(Color.FromRgb(5, 7, 12)),
            ClipToBounds = true,
            Child = content
        };

        var pane = new TerminalPane
        {
            Id = paneId,
            Terminal = terminal,
            Surface = surface,
            Badge = badgeText,
            Profile = profile
        };
        tab.Panes.Add(pane);
        HookTerminal(tab, pane);
        terminal.ContextMenu = BuildTerminalContextMenu(tab, pane);

        if (startSession && IsLoaded)
        {
            StartPaneSession(tab, pane);
        }
        return pane;
    }

    private ContextMenu BuildTerminalContextMenu(SessionTab tab, TerminalPane pane)
    {
        var menu = new ContextMenu();
        var copy = MenuAction("Copy", "Ctrl+Shift+C", (_, _) => pane.Terminal.CopySelection());
        menu.Items.Add(copy);
        menu.Items.Add(MenuAction("Paste", "Ctrl+Shift+V", (_, _) => pane.Terminal.PasteClipboard()));
        menu.Items.Add(MenuAction("Select visible terminal", "Ctrl+Shift+A", (_, _) => pane.Terminal.SelectViewport()));
        menu.Items.Add(MenuAction("Find", "Ctrl+Shift+F", OnFindClick));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("Split right", "Alt+Shift+Plus", OnSplitRightClick));
        menu.Items.Add(MenuAction("Split down", "Alt+Shift+Minus", OnSplitDownClick));
        menu.Items.Add(MenuAction("Close pane", "Ctrl+Shift+W", OnClosePaneClick));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("Settings...", string.Empty, OnSettingsClick));
        menu.Opened += (_, _) =>
        {
            ActivatePane(tab, pane, focus: false);
            copy.IsEnabled = pane.Terminal.HasSelection;
        };
        menu.Closed += (_, _) => pane.Terminal.Focus();
        return menu;
    }

    private void HookTerminal(SessionTab tab, TerminalPane pane)
    {
        pane.Terminal.InputReady += text =>
        {
            var session = pane.Session;
            if (session is not null)
            {
                _ = session.WriteAsync(Encoding.UTF8.GetBytes(text));
            }
        };
        pane.Terminal.TerminalSizeChanged += (columns, rows) =>
        {
            pane.Session?.Resize(columns, rows);
            if (pane == ActivePane)
            {
                TerminalDimensions.Text = $"{columns} × {rows}";
            }
        };
        pane.Terminal.TitleChanged += title => OnTerminalTitleChanged(tab, pane, title);
        pane.Terminal.ScrollStateChanged += (offset, count) =>
        {
            if (pane == ActivePane)
            {
                UpdateScrollState(offset, count);
            }
        };
        pane.Terminal.SearchRequested += () =>
        {
            ActivatePane(tab, pane, focus: false);
            ShowSearch();
        };
        pane.Terminal.GotKeyboardFocus += (_, _) => ActivatePane(tab, pane, focus: false);
        pane.Surface.MouseEnter += (_, _) =>
        {
            if (_settings.FocusFollowsMouse && pane != ActivePane)
            {
                ActivatePane(tab, pane);
            }
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        foreach (var tab in _tabs)
        {
            foreach (var pane in tab.Panes.Where(pane => !pane.Started))
            {
                StartPaneSession(tab, pane);
            }
        }
        ActiveTerminal?.Focus();
    }

    private void StartPaneSession(SessionTab tab, TerminalPane pane)
    {
        if (pane.Started)
        {
            return;
        }
        pane.Started = true;

        var commandLine = pane.Profile.CommandLine;
        pane.DisplayName = pane.Profile.Name;
        pane.WindowTitle = pane.DisplayName;
        SetPaneStatus(tab, pane, "STARTING", Color.FromRgb(228, 184, 86));
        UpdateTabTitle(tab);

        try
        {
            pane.Session = ConPtySession.Start(
                commandLine,
                pane.Terminal.Columns,
                pane.Terminal.Rows,
                pane.Profile.WorkingDirectory,
                _settings.Term);
            pane.Session.OutputReceived += bytes =>
                _ = Dispatcher.InvokeAsync(() => pane.Terminal.Write(bytes));
            pane.Session.Exited += () => OnSessionExited(tab, pane);
            pane.Session.StartReading();
            SetPaneStatus(tab, pane, "CONNECTED", Color.FromRgb(139, 92, 246));
        }
        catch (Exception ex)
        {
            pane.Exited = true;
            SetPaneStatus(tab, pane, "OFFLINE", Color.FromRgb(241, 76, 76));
            pane.Terminal.WriteText($"stem could not start ConPTY:\r\n{ex.Message}\r\n");
        }

        UpdateTabStyles();
        ApplyPaneStyles(tab);
        if (pane == ActivePane)
        {
            RefreshActiveChrome();
            pane.Terminal.Focus();
        }
    }

    private void OnSessionExited(SessionTab tab, TerminalPane pane)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            pane.Exited = true;
            SetPaneStatus(tab, pane, "SESSION ENDED", Color.FromRgb(126, 137, 155));
            pane.Terminal.WriteText("\r\n\u001b[90m[process exited]\u001b[0m\r\n");
            UpdateTabStyles();
            ApplyPaneStyles(tab);
        });
    }

    private void SetPaneStatus(SessionTab tab, TerminalPane pane, string text, Color color)
    {
        pane.StatusText = text;
        pane.StatusColor = color;
        if (tab == _activeTab && pane == tab.ActivePane)
        {
            SetConnectionState(text, color);
        }
    }

    private void ActivateTab(SessionTab tab)
    {
        if (_activeTab == tab)
        {
            return;
        }

        if (SearchPanel.Visibility == Visibility.Visible)
        {
            CloseSearch();
        }

        _activeTab = tab;
        TerminalHost.Content = tab.ZoomedPane?.Surface ?? tab.LayoutVisual;
        UpdateTabStyles();
        ApplyPaneStyles(tab);
        RefreshActiveChrome();
        tab.ActivePane.Terminal.Focus();
    }

    private void ActivatePane(SessionTab tab, TerminalPane pane, bool focus = true)
    {
        if (_activeTab != tab)
        {
            ActivateTab(tab);
        }
        if (tab.ActivePane == pane)
        {
            if (focus)
            {
                pane.Terminal.Focus();
            }
            return;
        }

        tab.ActivePane = pane;
        UpdateTabTitle(tab);
        ApplyPaneStyles(tab);
        RefreshActiveChrome();
        if (focus)
        {
            pane.Terminal.Focus();
        }
    }

    private void RefreshActiveChrome()
    {
        var tab = _activeTab;
        var pane = tab?.ActivePane;
        if (tab is null || pane is null)
        {
            return;
        }

        ShellBadge.Text = pane.DisplayName.ToUpperInvariant();
        WindowSessionTitle.Text = pane.WindowTitle;
        Title = $"{pane.WindowTitle} \u2014 {_settings.WindowTitle}";
        SetConnectionState(pane.StatusText, pane.StatusColor);
        TerminalDimensions.Text = $"{pane.Terminal.Columns} × {pane.Terminal.Rows}";
        UpdateScrollState(pane.Terminal.ScrollOffset, pane.Terminal.Buffer.ScrollbackCount);
    }

    private void UpdateTabTitle(SessionTab tab)
    {
        var pane = tab.ActivePane;
        var paneCount = tab.Panes.Count > 1 ? $"  \u2022  {tab.Panes.Count} PANES" : string.Empty;
        tab.TabTitle.Text = $"{tab.Id}  {pane.DisplayName.ToUpperInvariant()}{paneCount}";
    }

    private void UpdateTabStyles()
    {
        foreach (var tab in _tabs)
        {
            var active = tab == _activeTab;
            tab.TabElement.Background = new SolidColorBrush(active
                ? Color.FromRgb(43, 29, 77)
                : Color.FromRgb(13, 20, 32));
            tab.TabElement.BorderBrush = new SolidColorBrush(active
                ? Color.FromRgb(139, 92, 246)
                : Color.FromRgb(42, 55, 74));
            tab.TabTitle.Foreground = new SolidColorBrush(
                active ? Color.FromRgb(196, 167, 255) : Color.FromRgb(151, 162, 179));
        }
    }

    private void ApplyPaneStyles(SessionTab tab)
    {
        foreach (var pane in tab.Panes)
        {
            var active = pane == tab.ActivePane;
            pane.Surface.Opacity = active ? 1 : _settings.UnfocusedPaneOpacity;
            pane.Surface.BorderBrush = new SolidColorBrush(active
                ? _settings.SplitDividerColor.ToMediaColor()
                : Color.FromRgb(32, 43, 61));
            pane.Surface.BorderThickness = new Thickness(active ? 1.5 : 1);
            pane.Badge.Text = active ? $"P{pane.Id}  ACTIVE" : $"P{pane.Id}";
        }
    }

    private void RebuildTabLayout(SessionTab tab)
    {
        var activeInHost = tab == _activeTab;
        if (activeInHost)
        {
            TerminalHost.Content = null;
        }

        foreach (var pane in tab.Panes)
        {
            DetachElement(pane.Surface);
        }

        tab.LayoutVisual = BuildPaneVisual(tab.Root);
        if (activeInHost)
        {
            TerminalHost.Content = tab.ZoomedPane?.Surface ?? tab.LayoutVisual;
        }
        ApplyPaneStyles(tab);
    }

    private FrameworkElement BuildPaneVisual(PaneNode node)
    {
        if (node is PaneLeaf leaf)
        {
            return leaf.Pane.Surface;
        }

        var branch = (PaneBranch)node;
        var grid = new Grid { Background = new SolidColorBrush(Color.FromRgb(5, 7, 12)) };
        var first = BuildPaneVisual(branch.First);
        var second = BuildPaneVisual(branch.Second);
        var divider = new GridSplitter
        {
            Background = new SolidColorBrush(_settings.SplitDividerColor.ToMediaColor()),
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ResizeDirection = branch.SideBySide ? GridResizeDirection.Columns : GridResizeDirection.Rows,
            ShowsPreview = true,
            Focusable = false
        };

        if (branch.SideBySide)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
            Grid.SetColumn(first, 0);
            Grid.SetColumn(divider, 1);
            Grid.SetColumn(second, 2);
            divider.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 80 });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 80 });
            Grid.SetRow(first, 0);
            Grid.SetRow(divider, 1);
            Grid.SetRow(second, 2);
            divider.VerticalAlignment = VerticalAlignment.Stretch;
        }

        grid.Children.Add(first);
        grid.Children.Add(divider);
        grid.Children.Add(second);
        return grid;
    }

    private static void DetachElement(FrameworkElement element)
    {
        switch (VisualTreeHelper.GetParent(element))
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case ContentControl content when ReferenceEquals(content.Content, element):
                content.Content = null;
                break;
            case Border border when ReferenceEquals(border.Child, element):
                border.Child = null;
                break;
        }
    }

    private static PaneNode ReplaceLeaf(
        PaneNode node,
        TerminalPane target,
        PaneNode replacement)
    {
        if (node is PaneLeaf leaf)
        {
            return leaf.Pane == target ? replacement : node;
        }

        var branch = (PaneBranch)node;
        branch.First = ReplaceLeaf(branch.First, target, replacement);
        branch.Second = ReplaceLeaf(branch.Second, target, replacement);
        return branch;
    }

    private static PaneNode? RemoveLeaf(PaneNode node, TerminalPane target)
    {
        if (node is PaneLeaf leaf)
        {
            return leaf.Pane == target ? null : node;
        }

        var branch = (PaneBranch)node;
        var first = RemoveLeaf(branch.First, target);
        var second = RemoveLeaf(branch.Second, target);
        if (first is null)
        {
            return second;
        }
        if (second is null)
        {
            return first;
        }

        branch.First = first;
        branch.Second = second;
        return branch;
    }

    private void SplitActivePane(bool sideBySide)
    {
        var tab = _activeTab;
        var active = tab?.ActivePane;
        if (tab is null || active is null)
        {
            return;
        }

        tab.ZoomedPane = null;
        var pane = CreatePane(tab, startSession: false, active.Profile);
        var branch = new PaneBranch(sideBySide, new PaneLeaf(active), new PaneLeaf(pane));
        tab.Root = ReplaceLeaf(tab.Root, active, branch);
        tab.ActivePane = pane;
        RebuildTabLayout(tab);
        UpdateTabTitle(tab);
        StartPaneSession(tab, pane);
        ActivatePane(tab, pane);
    }

    private void CloseActivePane()
    {
        var tab = _activeTab;
        var pane = tab?.ActivePane;
        if (tab is null || pane is null)
        {
            return;
        }

        if (tab.Panes.Count == 1)
        {
            CloseTab(tab.Id);
            return;
        }

        tab.ZoomedPane = null;
        tab.Root = RemoveLeaf(tab.Root, pane)!;
        pane.Dispose();
        tab.Panes.Remove(pane);
        tab.ActivePane = tab.Panes[Math.Max(0, tab.Panes.Count - 1)];
        RebuildTabLayout(tab);
        UpdateTabTitle(tab);
        ActivatePane(tab, tab.ActivePane);
    }

    private void TogglePaneZoom()
    {
        var tab = _activeTab;
        var pane = tab?.ActivePane;
        if (tab is null || pane is null || tab.Panes.Count < 2)
        {
            return;
        }

        TerminalHost.Content = null;
        if (tab.ZoomedPane is null)
        {
            DetachElement(pane.Surface);
            tab.ZoomedPane = pane;
            TerminalHost.Content = pane.Surface;
        }
        else
        {
            tab.ZoomedPane = null;
            RebuildTabLayout(tab);
        }
        ApplyPaneStyles(tab);
        pane.Terminal.Focus();
    }

    private void FocusPane(Key direction)
    {
        var tab = _activeTab;
        var active = tab?.ActivePane;
        if (tab is null || active is null || tab.Panes.Count < 2)
        {
            return;
        }

        var activeCenter = PaneCenter(active);
        TerminalPane? best = null;
        var bestScore = double.MaxValue;
        foreach (var candidate in tab.Panes)
        {
            if (candidate == active)
            {
                continue;
            }

            var center = PaneCenter(candidate);
            var dx = center.X - activeCenter.X;
            var dy = center.Y - activeCenter.Y;
            var valid = direction switch
            {
                Key.Left => dx < -1,
                Key.Right => dx > 1,
                Key.Up => dy < -1,
                Key.Down => dy > 1,
                _ => false
            };
            if (!valid)
            {
                continue;
            }

            var primary = direction is Key.Left or Key.Right ? Math.Abs(dx) : Math.Abs(dy);
            var secondary = direction is Key.Left or Key.Right ? Math.Abs(dy) : Math.Abs(dx);
            var score = primary + secondary * 2;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        if (best is not null)
        {
            ActivatePane(tab, best);
        }
    }

    private Point PaneCenter(TerminalPane pane)
    {
        try
        {
            return pane.Surface.TranslatePoint(
                new Point(pane.Surface.ActualWidth / 2, pane.Surface.ActualHeight / 2),
                TerminalHost);
        }
        catch (InvalidOperationException)
        {
            var index = _activeTab?.Panes.IndexOf(pane) ?? 0;
            return new Point(index, index);
        }
    }

    private void OnTabSelectClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int id } &&
            _tabs.FirstOrDefault(tab => tab.Id == id) is { } tab)
        {
            ActivateTab(tab);
        }
    }

    private void OnTabCloseClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int id })
        {
            CloseTab(id);
        }
        e.Handled = true;
    }

    private void OnNewTabClick(object sender, RoutedEventArgs e) =>
        ActivateTab(CreateTab(startSession: true));

    private void OnFindClick(object sender, RoutedEventArgs e) => ShowSearch();

    private void OnTogglePaneZoomClick(object sender, RoutedEventArgs e) => TogglePaneZoom();

    private void OnSessionMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            PopulateSessionMenu(menu);
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void PopulateSessionMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        var defaultProfile = DefaultProfile();
        var newTab = new MenuItem { Header = "New tab" };
        foreach (var profile in _profiles)
        {
            var prefix = profile.Kind switch
            {
                StemProfileKind.Wsl => "WSL  ",
                StemProfileKind.Ssh => "SSH  ",
                StemProfileKind.Custom => "APP  ",
                _ => "SHELL  "
            };
            var item = new MenuItem
            {
                Header = prefix + profile.Name +
                    (profile.Id.Equals(defaultProfile.Id, StringComparison.OrdinalIgnoreCase)
                        ? "  (default)"
                        : string.Empty),
                Tag = profile.Id
            };
            item.Click += OnNewProfileTabClick;
            newTab.Items.Add(item);
        }
        menu.Items.Add(newTab);
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("Split right", "Alt+Shift+Plus", OnSplitRightClick));
        menu.Items.Add(MenuAction("Split down", "Alt+Shift+Minus", OnSplitDownClick));
        menu.Items.Add(MenuAction("Zoom / restore pane", "Alt+Shift+Enter", OnTogglePaneZoomClick));
        menu.Items.Add(MenuAction("Close pane", "Ctrl+Shift+W", OnClosePaneClick));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("Find", "Ctrl+Shift+F", OnFindClick));
        menu.Items.Add(MenuAction("Settings...", string.Empty, OnSettingsClick));
    }

    private static MenuItem MenuAction(
        string header,
        string shortcut,
        RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header, InputGestureText = shortcut };
        item.Click += handler;
        return item;
    }

    private void OnNewProfileTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string id })
        {
            return;
        }

        var profile = _profiles.FirstOrDefault(candidate =>
            candidate.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (profile is not null)
        {
            ActivateTab(CreateTab(startSession: true, profile));
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _settings = StemSettings.Load(StemSettings.ConfigPath);
            _profiles = StemProfileCatalog.Discover(_settings);
            _configWriteTimeUtc = ConfigWriteTimeUtc();
            ApplySettings(_settings, initial: false);
            RefreshActiveChrome();
            ActiveTerminal?.Focus();
        }
    }

    private void OnSplitRightClick(object sender, RoutedEventArgs e) => SplitActivePane(sideBySide: true);

    private void OnSplitDownClick(object sender, RoutedEventArgs e) => SplitActivePane(sideBySide: false);

    private void OnClosePaneClick(object sender, RoutedEventArgs e) => CloseActivePane();

    private void CloseTab(int id)
    {
        var index = _tabs.FindIndex(tab => tab.Id == id);
        if (index < 0)
        {
            return;
        }

        var tab = _tabs[index];
        var wasActive = tab == _activeTab;
        tab.Dispose();
        TabStrip.Children.Remove(tab.TabElement);
        _tabs.RemoveAt(index);

        if (_tabs.Count == 0)
        {
            _activeTab = null;
            ActivateTab(CreateTab(startSession: true));
            return;
        }

        if (wasActive)
        {
            _activeTab = null;
            ActivateTab(_tabs[Math.Min(index, _tabs.Count - 1)]);
        }
        UpdateTabStyles();
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var control = (modifiers & ModifierKeys.Control) != 0;
        var shift = (modifiers & ModifierKeys.Shift) != 0;
        var alt = (modifiers & ModifierKeys.Alt) != 0;

        if (control && shift && e.Key == Key.T)
        {
            ActivateTab(CreateTab(startSession: true));
            e.Handled = true;
            return;
        }

        if (control && shift && e.Key == Key.W)
        {
            CloseActivePane();
            e.Handled = true;
            return;
        }

        if (control && e.Key == Key.Tab && _tabs.Count > 1)
        {
            var current = Math.Max(0, _tabs.IndexOf(_activeTab!));
            var next = shift
                ? (current - 1 + _tabs.Count) % _tabs.Count
                : (current + 1) % _tabs.Count;
            ActivateTab(_tabs[next]);
            e.Handled = true;
            return;
        }

        if (alt && shift && e.Key is Key.OemPlus or Key.Add)
        {
            SplitActivePane(sideBySide: true);
            e.Handled = true;
            return;
        }

        if (alt && shift && e.Key is Key.OemMinus or Key.Subtract)
        {
            SplitActivePane(sideBySide: false);
            e.Handled = true;
            return;
        }

        if (alt && shift && e.Key == Key.Enter)
        {
            TogglePaneZoom();
            e.Handled = true;
            return;
        }

        if (alt && !shift && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            FocusPane(e.Key);
            e.Handled = true;
        }
    }

    private void OnTerminalTitleChanged(SessionTab tab, TerminalPane pane, string title)
    {
        pane.WindowTitle = string.IsNullOrWhiteSpace(title)
            ? pane.DisplayName
            : title;
        UpdateTabTitle(tab);
        if (tab == _activeTab && pane == tab.ActivePane)
        {
            RefreshActiveChrome();
        }
    }

    private void UpdateScrollState(int offset, int count)
    {
        ScrollbackStatus.Text = offset == 0
            ? count == 0 ? "LIVE" : $"LIVE  \u2022  {count:N0} LINES"
            : $"HISTORY  -{offset:N0} / {count:N0}";
    }

    private void ShowSearch()
    {
        var terminal = ActiveTerminal;
        if (terminal is null)
        {
            return;
        }

        SearchPanel.Visibility = Visibility.Visible;
        SearchBox.Focus();
        SearchBox.SelectAll();
        UpdateSearchCount(terminal.StartSearch(SearchBox.Text));
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchPanel.Visibility == Visibility.Visible && ActiveTerminal is { } terminal)
        {
            UpdateSearchCount(terminal.StartSearch(SearchBox.Text));
        }
    }

    private void OnSearchBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseSearch();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            NavigateSearch((Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            e.Handled = true;
        }
    }

    private void OnSearchPreviousClick(object sender, RoutedEventArgs e) => NavigateSearch(previous: true);

    private void OnSearchNextClick(object sender, RoutedEventArgs e) => NavigateSearch(previous: false);

    private void OnSearchCloseClick(object sender, RoutedEventArgs e) => CloseSearch();

    private void NavigateSearch(bool previous)
    {
        if (ActiveTerminal is not { } terminal)
        {
            return;
        }
        UpdateSearchCount(terminal.FindNextSearch(previous));
        SearchBox.Focus();
    }

    private void UpdateSearchCount(TerminalSearchResult result)
    {
        SearchCount.Text = result.Total == 0
            ? "0 / 0"
            : $"{result.Current:N0} / {result.Total:N0}";
        SearchCount.Foreground = new SolidColorBrush(
            result.Total == 0 ? Color.FromRgb(126, 137, 155) : Color.FromRgb(196, 167, 255));
    }

    private void CloseSearch()
    {
        SearchPanel.Visibility = Visibility.Collapsed;
        ActiveTerminal?.ClearSearch();
        ActiveTerminal?.Focus();
    }

    private void SetConnectionState(string text, Color color)
    {
        ConnectionStatus.Text = text;
        ConnectionDot.Fill = new SolidColorBrush(color);
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => UpdateWindowChrome();

    private void UpdateWindowChrome()
    {
        var maximized = WindowState == WindowState.Maximized;
        RootFrame.CornerRadius = new CornerRadius(maximized ? 0 : 12);
        RootFrame.Margin = new Thickness(maximized ? 7 : 1);
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        var writeTime = ConfigWriteTimeUtc();
        if (writeTime <= _configWriteTimeUtc)
        {
            return;
        }

        _configWriteTimeUtc = writeTime;
        _settings = StemSettings.Load(StemSettings.ConfigPath);
        _profiles = StemProfileCatalog.Discover(_settings);
        ApplySettings(_settings, initial: false);
    }

    private void ApplySettings(StemSettings settings, bool initial)
    {
        foreach (var tab in _tabs)
        {
            foreach (var pane in tab.Panes)
            {
                pane.Terminal.ApplySettings(settings);
            }
            RebuildTabLayout(tab);
        }
        Opacity = settings.Opacity;
        Title = settings.WindowTitle;
        if (!initial || ActiveTerminal is not { } terminal)
        {
            return;
        }

        var grid = terminal.EstimatedGridSize(settings.Columns, settings.Rows);
        var workArea = SystemParameters.WorkArea;
        Width = Math.Clamp(grid.Width + 38, MinWidth, Math.Max(MinWidth, workArea.Width * 0.94));
        Height = Math.Clamp(grid.Height + 134, MinHeight, Math.Max(MinHeight, workArea.Height * 0.94));
    }

    private static DateTime ConfigWriteTimeUtc()
    {
        try
        {
            return File.Exists(StemSettings.ConfigPath)
                ? File.GetLastWriteTimeUtc(StemSettings.ConfigPath)
                : DateTime.MinValue;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var paneCount = _tabs.Sum(tab => tab.Panes.Count);
        if (_settings.ConfirmClose &&
            !_closingConfirmed &&
            _tabs.SelectMany(tab => tab.Panes).Any(pane => pane.Session is not null && !pane.Exited))
        {
            var result = MessageBox.Show(
                this,
                $"Close {paneCount} terminal session{(paneCount == 1 ? string.Empty : "s")}?",
                "STEM",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _closingConfirmed = true;
        }

        foreach (var tab in _tabs)
        {
            tab.Dispose();
        }
        _tabs.Clear();
    }
}

internal static class TerminalColorExtensions
{
    public static Color ToMediaColor(this TerminalColor color) => Color.FromRgb(color.R, color.G, color.B);
}
