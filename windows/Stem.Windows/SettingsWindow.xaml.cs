using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Stem.Windows;

public partial class SettingsWindow : Window
{
    private sealed class ProfileEditor
    {
        public required string OriginalId { get; init; }
        public required Border Container { get; init; }
        public required TextBox IdBox { get; init; }
        public required TextBox NameBox { get; init; }
        public required ComboBox KindBox { get; init; }
        public required TextBox CommandBox { get; init; }
        public required TextBox WorkingDirectoryBox { get; init; }
    }
    private readonly StemSettings _initial;
    private readonly List<TextBox> _ansiBoxes = [];
    private readonly List<ProfileEditor> _profileEditors = [];
    private readonly IReadOnlyList<StemProfile> _profileCatalog;
    private bool _loading = true;
    private bool _updatingCode;
    private bool _codeDirty;
    private bool _saved;

    public SettingsWindow(StemSettings settings)
    {
        _initial = settings;
        KryptonTheme.ApplyApplication(settings.Theme, settings.Opacity);
        InitializeComponent();
        DarkWindowTheme.Apply(this, dark: !KryptonTheme.IsLight(settings.Theme), settings.Opacity);
        _profileCatalog = StemProfileCatalog.Discover(settings);
        PopulateFontFamilies();
        PopulateAnsiPalette();
        PopulateProfiles();
        PopulateControls();
        _loading = false;
        SetGeneratedCode(GenerateConfig());
    }

    private void PopulateProfiles()
    {
        foreach (var profile in _profileCatalog)
        {
            DefaultProfileBox.Items.Add(new ComboBoxItem
            {
                Content = profile.Name + (profile.AutoDetected ? "  (detected)" : string.Empty),
                Tag = profile.Id
            });

            if (profile.Id.Equals("default", StringComparison.OrdinalIgnoreCase) || profile.AutoDetected)
            {
                var chip = new Border
                {
                    Margin = new Thickness(0, 0, 8, 8),
                    Padding = new Thickness(10, 6, 10, 6),
                    CornerRadius = new CornerRadius(8),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(64, 49, 104)),
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(Color.FromRgb(24, 18, 42)),
                    Child = new TextBlock
                    {
                        Text = profile.Kind.ToString().ToUpperInvariant() + "  " + profile.Name,
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.FromRgb(196, 167, 255))
                    }
                };
                DetectedProfilesPanel.Children.Add(chip);
            }
        }

        foreach (var profile in _initial.Profiles)
        {
            AddProfileEditor(profile, addToDefaultList: false);
        }
        SelectByTag(DefaultProfileBox, _initial.DefaultProfile);
    }

    private void AddProfileEditor(StemProfile profile, bool addToDefaultList)
    {
        var idBox = new TextBox { Text = profile.Id };
        var nameBox = new TextBox { Text = profile.Name };
        var commandBox = new TextBox { Text = profile.CommandLine };
        var workingBox = new TextBox { Text = profile.WorkingDirectory };
        var kindBox = new ComboBox();
        foreach (var kind in Enum.GetValues<StemProfileKind>())
        {
            kindBox.Items.Add(new ComboBoxItem
            {
                Content = kind.ToString(),
                Tag = kind.ToString().ToLowerInvariant()
            });
        }
        SelectByTag(kindBox, profile.Kind.ToString().ToLowerInvariant());

        var remove = new Button
        {
            Content = "REMOVE",
            Height = 28,
            Padding = new Thickness(10, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(40, 21, 34)),
            Foreground = new SolidColorBrush(Color.FromRgb(241, 126, 142)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(104, 48, 65)),
            BorderThickness = new Thickness(1),
            FocusVisualStyle = null
        };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = profile.Kind.ToString().ToUpperInvariant() + " PROFILE",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(196, 167, 255))
        });
        Grid.SetColumn(remove, 1);
        header.Children.Add(remove);

        var fields = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 0) };
        fields.Children.Add(ProfileField("ID", idBox, new Thickness(0, 0, 10, 8)));
        fields.Children.Add(ProfileField("NAME", nameBox, new Thickness(0, 0, 0, 8)));
        fields.Children.Add(ProfileField("KIND", kindBox, new Thickness(0, 0, 10, 8)));
        fields.Children.Add(ProfileField("WORKING DIRECTORY", workingBox, new Thickness(0, 0, 0, 8)));

        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(fields);
        content.Children.Add(ProfileField("COMMAND", commandBox, new Thickness(0)));

        var container = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(40, 54, 77)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(10, 16, 26)),
            Child = content
        };
        var editor = new ProfileEditor
        {
            OriginalId = profile.Id,
            Container = container,
            IdBox = idBox,
            NameBox = nameBox,
            KindBox = kindBox,
            CommandBox = commandBox,
            WorkingDirectoryBox = workingBox
        };
        remove.Tag = editor;
        remove.Click += OnRemoveProfileClick;
        _profileEditors.Add(editor);
        ProfileEditorsPanel.Children.Add(container);

        if (addToDefaultList)
        {
            DefaultProfileBox.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Id });
        }
    }

    private static StackPanel ProfileField(string label, Control control, Thickness margin)
    {
        var field = new StackPanel { Margin = margin };
        field.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 0, 3),
            FontSize = 8,
            Foreground = new SolidColorBrush(Color.FromRgb(120, 133, 154))
        });
        field.Children.Add(control);
        return field;
    }

    private void OnAddSshProfileClick(object sender, RoutedEventArgs e)
    {
        var id = UniqueProfileId("ssh-server");
        AddProfileEditor(
            new StemProfile(
                id,
                "SSH Server",
                "ssh user@example.com",
                _initial.WorkingDirectory,
                StemProfileKind.Ssh),
            addToDefaultList: true);
    }

    private void OnAddCustomProfileClick(object sender, RoutedEventArgs e)
    {
        var id = UniqueProfileId("custom");
        AddProfileEditor(
            new StemProfile(
                id,
                "Custom Command",
                "your-command.exe",
                _initial.WorkingDirectory,
                StemProfileKind.Custom),
            addToDefaultList: true);
    }

    private string UniqueProfileId(string seed)
    {
        var ids = _profileCatalog.Select(profile => profile.Id)
            .Concat(_profileEditors.Select(editor => editor.IdBox.Text))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var id = seed;
        var suffix = 2;
        while (ids.Contains(id))
        {
            id = seed + "-" + suffix++;
        }
        return id;
    }

    private void OnRemoveProfileClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProfileEditor editor })
        {
            return;
        }

        _profileEditors.Remove(editor);
        ProfileEditorsPanel.Children.Remove(editor.Container);
        var menuItem = DefaultProfileBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(
                item.Tag?.ToString(),
                editor.OriginalId,
                StringComparison.OrdinalIgnoreCase));
        if (menuItem is not null)
        {
            DefaultProfileBox.Items.Remove(menuItem);
        }
        if (DefaultProfileBox.SelectedItem is null)
        {
            SelectByTag(DefaultProfileBox, "default");
        }
    }

    private IReadOnlyList<StemProfile> ReadProfiles()
    {
        var profiles = new List<StemProfile>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var editor in _profileEditors)
        {
            var id = ProfileId(editor.IdBox.Text);
            if (!ids.Add(id))
            {
                throw new FormatException($"Profile ID '{id}' is duplicated.");
            }
            var kindText = SelectedTag(editor.KindBox, "custom");
            var kind = Enum.TryParse<StemProfileKind>(kindText, ignoreCase: true, out var parsed)
                ? parsed
                : StemProfileKind.Custom;
            profiles.Add(new StemProfile(
                id,
                SingleLine(editor.NameBox.Text, id),
                SingleLine(editor.CommandBox.Text, string.Empty),
                SingleLine(editor.WorkingDirectoryBox.Text, _initial.WorkingDirectory),
                kind));
            if (string.IsNullOrWhiteSpace(profiles[^1].CommandLine))
            {
                throw new FormatException($"Profile '{id}' needs a command.");
            }
        }
        return profiles;
    }

    private string SelectedDefaultProfileId()
    {
        var selected = (DefaultProfileBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "default";
        var editor = _profileEditors.FirstOrDefault(candidate =>
            candidate.OriginalId.Equals(selected, StringComparison.OrdinalIgnoreCase));
        return editor is null ? selected : ProfileId(editor.IdBox.Text);
    }

    private static string ProfileId(string value)
    {
        var id = value.Trim();
        if (id.Length == 0 || id.Any(character =>
                !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new FormatException("Profile IDs may contain only letters, numbers, hyphens, and underscores.");
        }
        return id;
    }

    private void PopulateFontFamilies()
    {
        foreach (var family in Fonts.SystemFontFamilies
                     .Select(font => font.Source)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            FontFamilyBox.Items.Add(family);
        }
    }

    private void PopulateAnsiPalette()
    {
        for (var index = 0; index < 16; index++)
        {
            var label = new TextBlock
            {
                Text = $"COLOR {index}",
                Margin = new Thickness(0, 0, 0, 3),
                FontSize = 8,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 133, 154))
            };
            var box = new TextBox { Tag = index };
            var group = new StackPanel
            {
                Margin = new Thickness(0, 0, index % 4 == 3 ? 0 : 10, 8)
            };
            group.Children.Add(label);
            group.Children.Add(box);
            AnsiPalettePanel.Children.Add(group);
            _ansiBoxes.Add(box);
        }
    }

    private void PopulateControls()
    {
        SelectByTag(ThemeBox, KryptonTheme.Normalize(_initial.Theme));
        ThemeBadgeText.Text = KryptonTheme.IsLight(_initial.Theme) ? "KRYPTON LIGHT" : "KRYPTON DARK";
        WindowTitleBox.Text = _initial.WindowTitle;
        ShellBox.Text = _initial.Shell;
        WorkingDirectoryBox.Text = _initial.WorkingDirectory;
        TermBox.Text = _initial.Term;
        ColumnsBox.Text = _initial.Columns.ToString(CultureInfo.InvariantCulture);
        RowsBox.Text = _initial.Rows.ToString(CultureInfo.InvariantCulture);

        FontFamilyBox.Text = _initial.FontFamily;
        FontSizeBox.Text = Number(_initial.FontSize);
        PaddingBox.Text = Number(_initial.Padding);
        LineSpacingBox.Text = Number(_initial.LineSpacing);
        OpacitySlider.Value = _initial.Opacity;

        BackgroundColorBox.Text = Hex(_initial.BackgroundColor);
        ForegroundColorBox.Text = Hex(_initial.ForegroundColor);
        AccentColorBox.Text = Hex(_initial.AccentColor);
        SelectionColorBox.Text = Hex(_initial.SelectionColor);
        CursorColorBox.Text = Hex(_initial.CursorColor);
        SplitDividerColorBox.Text = Hex(_initial.SplitDividerColor);
        for (var index = 0; index < _ansiBoxes.Count; index++)
        {
            _ansiBoxes[index].Text = Hex(_initial.AnsiPalette[index]);
        }

        ScrollbackBox.Text = _initial.ScrollbackLines.ToString(CultureInfo.InvariantCulture);
        CopyOnSelectBox.IsChecked = _initial.CopyOnSelect;
        ConfirmCloseBox.IsChecked = _initial.ConfirmClose;
        RestoreSessionBox.IsChecked = _initial.RestoreSession;
        FocusFollowsMouseBox.IsChecked = _initial.FocusFollowsMouse;
        SelectByTag(BellBox, _initial.Bell.ToString().ToLowerInvariant());
        SelectByTag(CursorStyleBox, _initial.CursorStyle.ToString().ToLowerInvariant());
        CursorBlinkBox.Text = _initial.CursorBlinkMilliseconds.ToString(CultureInfo.InvariantCulture);
        UnfocusedPaneOpacitySlider.Value = _initial.UnfocusedPaneOpacity;
        UpdateSliderLabels();
    }

    private static void SelectByTag(ComboBox combo, string tag)
    {
        combo.SelectedItem = combo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));
        combo.SelectedIndex = Math.Max(0, combo.SelectedIndex);
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateSliderLabels();
        if (!_loading && ThemeBox is not null)
        {
            var theme = SelectedTag(ThemeBox, KryptonTheme.Dark);
            KryptonTheme.ApplyApplication(theme, OpacitySlider.Value);
            DarkWindowTheme.Apply(this, dark: !KryptonTheme.IsLight(theme), OpacitySlider.Value);
        }
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedItem is null)
        {
            return;
        }

        var theme = SelectedTag(ThemeBox, KryptonTheme.Dark);
        KryptonTheme.ApplyApplication(theme, OpacitySlider.Value);
        DarkWindowTheme.Apply(this, dark: !KryptonTheme.IsLight(theme), OpacitySlider.Value);
        ThemeBadgeText.Text = KryptonTheme.IsLight(theme) ? "KRYPTON LIGHT" : "KRYPTON DARK";
        SettingsStatus.Text = "Theme preview active. Save & Apply to keep it.";
        SettingsStatus.SetResourceReference(TextBlock.ForegroundProperty, "KryptonAccentHighlightBrush");
    }

    private void OnPaneOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateSliderLabels();

    private void UpdateSliderLabels()
    {
        if (OpacityValue is not null)
        {
            OpacityValue.Text = $"{OpacitySlider.Value:P0}";
        }
        if (PaneOpacityValue is not null)
        {
            PaneOpacityValue.Text = $"{UnfocusedPaneOpacitySlider.Value:P0}";
        }
    }

    private void OnSettingsTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SettingsTabs.SelectedItem != ConfigCodeTab || _codeDirty)
        {
            return;
        }

        TryRegenerateCode();
    }

    private void OnGenerateCodeClick(object sender, RoutedEventArgs e)
    {
        if (_codeDirty)
        {
            var result = MessageBox.Show(
                this,
                "Replace the manual config-code edits with values from the GUI?",
                "Generate config code",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        TryRegenerateCode();
    }

    private void TryRegenerateCode()
    {
        try
        {
            SetGeneratedCode(GenerateConfig());
            SettingsStatus.Text = "Config code regenerated from the GUI.";
            SettingsStatus.Foreground = new SolidColorBrush(Color.FromRgb(196, 167, 255));
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid setting", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetGeneratedCode(string text)
    {
        _updatingCode = true;
        ConfigCodeBox.Text = text;
        _updatingCode = false;
        _codeDirty = false;
    }

    private void OnConfigCodeChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _updatingCode)
        {
            return;
        }

        _codeDirty = true;
        SettingsStatus.Text = "Manual config code will be saved.";
        SettingsStatus.Foreground = new SolidColorBrush(Color.FromRgb(228, 184, 86));
    }

    private string GenerateConfig()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# STEM configuration generated by the Windows settings GUI.");
        builder.AppendLine("# Portable key = value syntax; safe to edit by hand.");
        builder.AppendLine();
        builder.AppendLine($"theme = {SelectedTag(ThemeBox, KryptonTheme.Dark)}");
        builder.AppendLine($"title = {SingleLine(WindowTitleBox.Text, "STEM")}");
        builder.AppendLine();
        builder.AppendLine("# Shell and startup");
        builder.AppendLine($"shell = {SingleLine(ShellBox.Text, string.Empty)}");
        builder.AppendLine($"working_directory = {SingleLine(WorkingDirectoryBox.Text, "~")}");
        builder.AppendLine($"term = {SingleLine(TermBox.Text, "xterm-256color")}");
        builder.AppendLine();
        builder.AppendLine("# Initial grid");
        builder.AppendLine($"cols = {Integer(ColumnsBox, 20, 400, "Columns")}");
        builder.AppendLine($"rows = {Integer(RowsBox, 5, 200, "Rows")}");
        builder.AppendLine();
        builder.AppendLine("# Typography and spacing");
        builder.AppendLine($"font_family = {SingleLine(FontFamilyBox.Text, StemSettings.DefaultFontFamily)}");
        builder.AppendLine($"font_size = {Decimal(FontSizeBox, 8, 40, "Font size")}");
        builder.AppendLine($"padding = {Decimal(PaddingBox, 0, 40, "Padding")}");
        builder.AppendLine($"line_spacing = {Decimal(LineSpacingBox, -4, 20, "Line spacing")}");
        builder.AppendLine();
        builder.AppendLine("# History and interaction");
        builder.AppendLine($"scrollback_lines = {Integer(ScrollbackBox, 0, 1_000_000, "Scrollback lines")}");
        builder.AppendLine($"copy_on_select = {Boolean(CopyOnSelectBox.IsChecked)}");
        builder.AppendLine($"confirm_close = {Boolean(ConfirmCloseBox.IsChecked)}");
        builder.AppendLine($"restore_session = {Boolean(RestoreSessionBox.IsChecked)}");
        builder.AppendLine($"bell = {SelectedTag(BellBox, "visual")}");
        builder.AppendLine();
        builder.AppendLine("# Cursor and panes");
        builder.AppendLine($"cursor_style = {SelectedTag(CursorStyleBox, "bar")}");
        builder.AppendLine($"cursor_blink_ms = {Integer(CursorBlinkBox, 0, 5_000, "Cursor blink")}");
        builder.AppendLine($"unfocused_pane_opacity = {Number(UnfocusedPaneOpacitySlider.Value)}");
        builder.AppendLine($"focus_follows_mouse = {Boolean(FocusFollowsMouseBox.IsChecked)}");
        builder.AppendLine();
        builder.AppendLine("# Window and Krypton colors");
        builder.AppendLine($"opacity = {Number(OpacitySlider.Value)}");
        builder.AppendLine($"background = {ColorValue(BackgroundColorBox, "Background")}");
        builder.AppendLine($"foreground = {ColorValue(ForegroundColorBox, "Foreground")}");
        builder.AppendLine($"accent = {ColorValue(AccentColorBox, "Accent")}");
        builder.AppendLine($"selection_background = {ColorValue(SelectionColorBox, "Selection")}");
        builder.AppendLine($"cursor_color = {ColorValue(CursorColorBox, "Cursor")}");
        builder.AppendLine($"split_divider_color = {ColorValue(SplitDividerColorBox, "Split divider")}");
        builder.AppendLine();
        builder.AppendLine("# ANSI 16-color palette");
        for (var index = 0; index < _ansiBoxes.Count; index++)
        {
            builder.AppendLine($"color{index} = {ColorValue(_ansiBoxes[index], $"Color {index}")}");
        }

        builder.AppendLine();
        builder.AppendLine("# Terminal profiles");
        var profiles = ReadProfiles();
        builder.AppendLine($"default_profile = {SelectedDefaultProfileId()}");
        foreach (var profile in profiles)
        {
            builder.AppendLine();
            builder.AppendLine($"profile.{profile.Id}.name = {profile.Name}");
            builder.AppendLine($"profile.{profile.Id}.kind = {profile.Kind.ToString().ToLowerInvariant()}");
            builder.AppendLine($"profile.{profile.Id}.command = {profile.CommandLine}");
            builder.AppendLine($"profile.{profile.Id}.working_directory = {profile.WorkingDirectory}");
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string SingleLine(string? value, string fallback)
    {
        var result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (result.Contains((char)13) || result.Contains((char)10))
        {
            throw new FormatException("Text settings must stay on one line.");
        }
        return result;
    }

    private static string Integer(TextBox box, int minimum, int maximum, string label)
    {
        if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
            value < minimum ||
            value > maximum)
        {
            throw new FormatException($"{label} must be between {minimum:N0} and {maximum:N0}.");
        }
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Decimal(TextBox box, double minimum, double maximum, string label)
    {
        if (!double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            value < minimum ||
            value > maximum)
        {
            throw new FormatException($"{label} must be between {minimum} and {maximum}.");
        }
        return Number(value);
    }

    private static string ColorValue(TextBox box, string label)
    {
        var value = box.Text.Trim();
        if (value.StartsWith('#'))
        {
            value = value[1..];
        }
        if (value.Length != 6 ||
            !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
        {
            throw new FormatException($"{label} must be a six-digit hex color such as #8B5CF6.");
        }
        return "#" + value.ToUpperInvariant();
    }

    private static string SelectedTag(ComboBox combo, string fallback) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private static string Boolean(bool? value) => value == true ? "true" : "false";

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Hex(TerminalColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = _codeDirty ? ConfigCodeBox.Text : GenerateConfig();
            ValidateConfigCode(text);
            SaveConfigAtomically(StemSettings.ConfigPath, text);
            _saved = true;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Could not save settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void ValidateConfigCode(string text)
    {
        var lineNumber = 0;
        foreach (var source in text.Split((char)10))
        {
            lineNumber++;
            var line = source.TrimEnd((char)13).Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            if (!line.Contains('=') || line.StartsWith('='))
            {
                throw new FormatException($"Config code line {lineNumber} must use key = value syntax.");
            }
        }
    }

    private static void SaveConfigAtomically(string path, string text)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new IOException("The STEM configuration directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        var lines = text.Split((char)10).Select(line => line.TrimEnd((char)13));
        var normalized = string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
        File.WriteAllText(temporaryPath, normalized, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (!_saved)
        {
            KryptonTheme.ApplyApplication(_initial.Theme, _initial.Opacity);
            if (Owner is Window owner)
            {
                DarkWindowTheme.Apply(owner, dark: !KryptonTheme.IsLight(_initial.Theme), _initial.Opacity);
            }
        }
        base.OnClosed(e);
    }
}
