using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BrimDeck.Core;

namespace BrimDeck;

public sealed partial class SettingsWindow
{
    // keepPreview marks the segments alone as preview controls, so a press on the rest of the row still ends the preview.
    private FrameworkElement MusicChoice<T>(string title, T selected, (T Value, string Label)[] options, Action<T> change, bool enabled = true, bool keepPreview = false, string? help = null, double width = 292) where T : notnull
    {
        var strip = new UniformGrid { Columns = options.Length, Width = width, IsEnabled = enabled };
        if (keepPreview) KeepPreview(strip);
        foreach (var option in options)
        {
            var choice = new RadioButton { Style = (Style)FindResource("SettingsSegment"), Content = option.Label, GroupName = title, IsChecked = EqualityComparer<T>.Default.Equals(option.Value, selected) };
            AutomationProperties.SetName(choice, title + " " + option.Label); choice.Checked += (_, _) => change(option.Value); strip.Children.Add(choice);
        }
        // Only the options are turned off: the row's info mark still shows its description on hover.
        var segments = SegmentBorder(strip); segments.IsEnabled = enabled;
        return Row(title, segments, help: help is null ? null : () => help);
    }
    // The options under the style cards belong to the chosen style. Choosing another card slides the current options
    // out and the chosen style's options in, moving the way the chosen card lies from the previous one.
    private Grid? _styleOptions;
    private void ShowStyleOptions(CompactStyle style, CompactStyle? previous)
    {
        if (_styleOptions is not { } host) return;
        var incoming = Collect(() => { StyleOptions(style); CollapsedSizeOptions(style); });
        SwapOptions(host, incoming, previous is { } from && from != style ? (style > from ? 1 : -1) : 0);
    }
    // Shows `incoming` in a host in place of what it shows: at once (direction 0, nothing shown yet, or without animations), or
    // with the old options leaving to one side as the new come in from the other (1: the new from the right; -1: from the left).
    // The options of a choice among cards (the collapsed style, the clipboard page's arrangement) are shown this way.
    private static void SwapOptions(Grid host, FrameworkElement incoming, int direction)
    {
        var outgoing = host.Children.OfType<FrameworkElement>().LastOrDefault();
        double width = host.ActualWidth;
        if (outgoing is null || direction == 0 || width <= 0 || !Soft.Enabled)
        { host.Children.Clear(); host.Children.Add(incoming); return; }
        // A panel still leaving from an earlier choice is dropped; the one arriving becomes the one leaving.
        foreach (var leaving in host.Children.OfType<FrameworkElement>().Where(child => child != outgoing).ToList()) host.Children.Remove(leaving);
        host.ClipToBounds = true; outgoing.IsHitTestVisible = false; host.Children.Add(incoming);
        var leave = outgoing.RenderTransform as TranslateTransform ?? new TranslateTransform(); outgoing.RenderTransform = leave;
        var enter = new TranslateTransform(direction * width, 0); incoming.RenderTransform = enter;
        DoubleAnimation Slide(double to) => new(to, TimeSpan.FromMilliseconds(300)) { EasingFunction = Soft.Out };
        var leaveMotion = Slide(-direction * width); leaveMotion.Completed += (_, _) => host.Children.Remove(outgoing);
        var enterMotion = Slide(0); enterMotion.Completed += (_, _) => { if (host.Children.Count == 1) host.ClipToBounds = false; };
        leave.BeginAnimation(TranslateTransform.XProperty, leaveMotion); enter.BeginAnimation(TranslateTransform.XProperty, enterMotion);
    }
    private void StyleOptions(CompactStyle style)
    {
        SectionTitle(Loc.T("顯示內容 · ", "Content · ") + StyleName(style));
        if (style == CompactStyle.Line)
        {
            Group(Toggle(Loc.T("顯示播放進度", "Show playback progress"), S.MusicIndicatorProgress, v => Change(s => s.MusicIndicatorProgress = v)));
            return;
        }
        // The rows are in the order the collapsed row shows the parts, left to right; only the cover keeps its place at the left.
        var rows = new StackPanel();
        _partOptions.Clear();
        foreach (var part in S.CompactOrder(style))
            rows.Children.Add(part switch { CompactPart.Clock => ClockRow(style, rows), _ => PartRow(style, part, rows) });
        UpdateDividers(rows);
        Group(rows);
    }

    private const double PartRowHeight = 44, PartHandleWidth = 40;
    // The parts whose options are open (the clock, the quota rings), kept while the page is rebuilt or the collapsed style changes.
    private readonly HashSet<CompactPart> _openParts = [];
    private readonly List<(Border Row, Border Options, Button Arrow)> _partOptions = [];
    // One draggable row of the collapsed content: the handle, the name and the switch.
    private Border PartRow(CompactStyle style, CompactPart part, StackPanel rows)
    {
        bool notch = style == CompactStyle.Notch;
        var (title, shown, help) = part switch
        {
            CompactPart.Rings => (Loc.T("配額環", "Quota rings"), S.ShowsSummary(style), null),
            CompactPart.Equalizer => (Loc.T("律動條", "Equalizer"), S.ShowsEqualizer(style), null),
            CompactPart.Music => (Loc.T("正在播放的音樂", "Music now playing"), S.ShowsMusic(style),
                Loc.T("拖動只改變歌名或歌詞的位置。封面始終在最左側。", "Dragging moves only the title or lyric. The cover stays at the left.")),
            CompactPart.Transfer => (Loc.T("檔案中轉", "File transfer"), style == CompactStyle.Notch ? S.NotchTransfer : S.CapsuleTransfer,
                Loc.T("顯示檔案中轉裡的檔案數量，沒有檔案時不顯示。單擊直接展開到剪貼簿與檔案中轉頁。", "Shows how many files the file transfer holds, and nothing while it is empty. Clicking it opens the clipboard and file transfer page.")),
            CompactPart.Devices => (Loc.T("裝置電量", "Device batteries"), S.ShowsDevices(style),
                Loc.T("顯示哪些裝置、是否顯示數字，在“裝置電量”設定頁中選擇。", "Which devices show, and whether with figures, is chosen on the device batteries settings page.")),
            _ => (Loc.T("系統監控", "System monitor"), S.ShowsMonitor(style), null)
        };
        void Show(bool on) => Change(s =>
        {
            switch (part)
            {
                case CompactPart.Rings: if (notch) s.NotchSummary = on; else s.CapsuleSummary = on; break;
                case CompactPart.Music: if (notch) s.NotchMusic = on; else s.CapsuleMusic = on; break;
                case CompactPart.Equalizer: if (notch) s.NotchEqualizer = on; else s.CapsuleEqualizer = on; break;
                case CompactPart.Transfer: if (notch) s.NotchTransfer = on; else s.CapsuleTransfer = on; break;
                case CompactPart.Devices: if (notch) s.NotchDevices = on; else s.CapsuleDevices = on; break;
                default: if (notch) s.NotchMonitor = on; else s.CapsuleMonitor = on; break;
            }
        });
        var row = new Grid { Background = Brushes.Transparent };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PartHandleWidth) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var handle = new GripHandle { Width = 22, Background = Brushes.Transparent, Cursor = Cursors.SizeAll, HorizontalAlignment = HorizontalAlignment.Left, Child = Grip(TextTertiary) };
        row.Children.Add(handle);
        var label = SettingLabel(title, help is null ? null : () => help); Grid.SetColumn(label, 1); row.Children.Add(label);
        var toggle = new CheckBox { Style = (Style)FindResource("SettingsToggle"), IsChecked = shown, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, title); toggle.Checked += (_, _) => Show(true); toggle.Unchecked += (_, _) => Show(false);
        Grid.SetColumn(toggle, 2); row.Children.Add(toggle);
        AttachPartDrag(handle, title, style, rows);
        return new Border { Child = row, Height = PartRowHeight, BorderBrush = UI.Brush(_palette.RowDivider), BorderThickness = new Thickness(0), Tag = part, RenderTransform = new TranslateTransform() };
    }
    // Dropped in a new place, the collapsed row takes the new order at once. The open options close when a drag begins, so every
    // row is one line high while they move.
    private void AttachPartDrag(Border handle, string name, CompactStyle style, StackPanel rows) => AttachDrag(handle, name, () => rows, PartRowHeight, list =>
    {
        var order = list.Children.OfType<Border>().Where(b => b.Tag is CompactPart).Select(b => (CompactPart)b.Tag).ToArray();
        Change(s => s.SetCompactOrder(style, order));
    }, ClosePartOptions);
    private void ClosePartOptions()
    {
        _openParts.Clear();
        foreach (var (row, options, arrow) in _partOptions) { options.Visibility = Visibility.Collapsed; PointArrow(arrow, false); row.Height = PartRowHeight; }
    }
    // A part with options under it: the handle, the title with its arrow, and the switch, which only turns the part on or off.
    // The title and its arrow open and close the options; the options are dimmed while the part is off. Without padding they lie
    // under the title, a little indented, as the page rows' sizes do; with it, on a card across the row.
    private Border OptionsPartRow(CompactStyle style, CompactPart part, StackPanel rows, string title, string arrowName, bool shown, Action<bool> turn, FrameworkElement content, Thickness? padding = null)
    {
        var options = padding is { } card ? new Border { Background = UI.Brush(Inset), CornerRadius = new CornerRadius(10), Padding = card, Margin = new Thickness(0, 0, 0, 12), Child = content }
            : new Border { Margin = new Thickness(20, 0, 0, 8), Child = content };
        void Enable(bool on) { options.IsEnabled = on; options.Opacity = on ? 1 : .45; }
        Enable(shown);
        Button arrow = null!;
        Border item = null!;
        void Show(bool open)
        {
            if (open) _openParts.Add(part); else _openParts.Remove(part);
            options.Visibility = open ? Visibility.Visible : Visibility.Collapsed; PointArrow(arrow, open);
            item.Height = open ? double.NaN : PartRowHeight;
        }
        arrow = ChevronButton(arrowName, () => Show(!_openParts.Contains(part)));
        arrow.Margin = new Thickness(2, 0, 0, 0);
        var heading = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(SettingLabel(title)); heading.Children.Add(arrow);
        var toggle = new CheckBox { Style = (Style)FindResource("SettingsToggle"), IsChecked = shown, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, title);
        toggle.Checked += (_, _) => { turn(true); Enable(true); }; toggle.Unchecked += (_, _) => { turn(false); Enable(false); };
        var row = new Grid { Background = Brushes.Transparent };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PartHandleWidth) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = new GridLength(PartRowHeight) }); row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var handle = new GripHandle { Width = 22, Background = Brushes.Transparent, Cursor = Cursors.SizeAll, HorizontalAlignment = HorizontalAlignment.Left, Child = Grip(TextTertiary) };
        row.Children.Add(handle);
        Grid.SetColumn(heading, 1); row.Children.Add(heading);
        Grid.SetColumn(toggle, 2); row.Children.Add(toggle);
        Grid.SetRow(options, 1); if (padding is null) Grid.SetColumn(options, 1); Grid.SetColumnSpan(options, padding is null ? 2 : 3); row.Children.Add(options);
        // The handle, the switch and the arrow take their own clicks; a click anywhere else on the title line opens or closes the
        // options, as the size rows do.
        row.MouseLeftButtonDown += (_, e) => { if (e.GetPosition(row).Y >= PartRowHeight) return; Show(!_openParts.Contains(part)); e.Handled = true; };
        AttachPartDrag(handle, title, style, rows);
        item = new Border { Child = row, BorderBrush = UI.Brush(_palette.RowDivider), BorderThickness = new Thickness(0), Tag = part, RenderTransform = new TranslateTransform() };
        _partOptions.Add((item, options, arrow));
        Show(_openParts.Contains(part));
        return item;
    }
    // The quota rings' look, on the quota page beside the applications: one ring or nested rings, whether the arcs show what is used
    // or what is left, and whether each application's letter (its display name's) is written inside its ring. They are shared by the
    // notch, the capsule and the side column; each application's quotas are set with the application (RingDetails), and the rings are
    // switched on or off with the collapsed content (PartRow). A new style builds the application rows again, so their ring lines follow it.
    private void QuotaRingSection()
    {
        SectionTitle(Loc.T("配額顯示", "Quota display"));
        Group(MusicChoice(Loc.T("環的樣式", "Ring style"), S.QuotaRingStyle, [(QuotaRingStyle.Single, Loc.T("單環", "Single")), (QuotaRingStyle.Nested, Loc.T("多環", "Nested"))],
                v => { Change(s => s.QuotaRingStyle = v); foreach (var entry in S.Apps) RefreshAppRow(entry.InstanceId); }, help: Loc.T("單環：每個應用一個環，預設顯示已用比例最高的一項額度。\n多環：每個應用最多三個環，分別顯示不同的額度。\n各環顯示哪項額度，在下方展開該應用後設定。",
                    "Single: one ring per app, showing its most used quota by default.\nNested: up to three rings per app, each showing a different quota.\nWhich quota each ring shows is set by expanding the app below."), width: 200),
            MusicChoice(Loc.T("顯示已用或剩餘", "Used or left"), S.QuotaRingRemaining, [(false, Loc.T("已用額度", "Used")), (true, Loc.T("剩餘額度", "Left"))],
                v => Change(s => s.QuotaRingRemaining = v), help: Loc.T("已用額度：顯示已用的比例，用得越多，配額環和進度條越長。\n剩餘額度：顯示剩餘的比例，用得越多，配額環和進度條越短。\n配額環、展開面板的進度條和百分比、配額提醒都按這裡顯示；餘額、扣費等金額不受影響。",
                    "Used: shows the share used; the more is used, the longer the rings and bars.\nLeft: shows the share left; the more is used, the shorter the rings and bars.\nThe quota rings, the panel's bars and percentages, and the quota notices all follow it; amounts such as balances and spending stay as they are."), width: 200),
            Toggle(Loc.T("環內顯示首字母", "Letter in the rings"), S.QuotaRingLetters, v => Change(s => s.QuotaRingLetters = v),
                help: Loc.T("在配額環中間顯示應用名稱的首字母，名稱以下方“顯示名稱”一欄為準。", "Shows the first letter of the app's name inside its quota ring, taken from the Display name column below.")));
    }
    // The clock's swatches but the gradient: the theme's color (theme), the presets, a custom color and the cover's. Used for the
    // equalizer and for the music page's current lyric; save gets the chosen color and the custom one.
    private StackPanel ColorSwatches(string group, string label, ClockColor selected, string custom, Brush theme, Action<ClockColor, string> save)
    {
        var swatches = new StackPanel { Orientation = Orientation.Horizontal };
        ClockColor[] order = [ClockColor.White, ClockColor.Amber, ClockColor.Cyan, ClockColor.Pink, ClockColor.Lime, ClockColor.Custom, ClockColor.Cover];
        foreach (var color in order)
        {
            string name = ColorName(color);
            var swatch = new RadioButton { Style = (Style)FindResource("SettingsClockSwatch"), GroupName = group, IsChecked = selected == color,
                Background = SwatchFill(color), ToolTip = name, Margin = new Thickness(swatches.Children.Count == 0 ? 0 : 6, 0, 0, 0) };
            AutomationProperties.SetName(swatch, label + name);
            swatch.Checked += (_, _) => save(color, custom);
            if (color == ClockColor.Cover) swatch.Content = MusicVisuals.Icon("music", 13, UI.Brush(SwatchInk(SampleCover)));
            if (color == ClockColor.Custom)
            {
                // Choosing this swatch also opens the picker; each later click opens it again.
                swatch.Content = Pencil(ClockFace.Parse(custom));
                AttachClockPicker(swatch, () => custom, ClockColors.DefaultCustom, value =>
                {
                    custom = value; save(ClockColor.Custom, value);
                    swatch.Background = SwatchFill(ClockColor.Custom); swatch.Content = Pencil(ClockFace.Parse(value));
                });
            }
            swatches.Children.Add(swatch);
        }
        return swatches;
        Brush SwatchFill(ClockColor color) => color switch
        {
            ClockColor.Custom => ClockFace.Fill([ClockFace.Parse(custom)]),
            ClockColor.Cover => ClockFace.Fill([SampleCover]),
            ClockColor.White => theme,
            _ => ClockFace.Fill([PanelTheme.Readable(ClockFace.Preset(color))])
        };
    }
    // Settings every player shares: the equalizer, colors, cover and text.
    private void MusicStyle()
    {
        // The equalizer's way and color serve the collapsed and the expanded equalizer alike; each keeps its own length, and the
        // collapsed one's switch stays with the collapsed content (PartRow).
        SectionTitle(Loc.T("通用", "General"));
        var pageBars = SliderRow(Loc.T("展開時律動條長度", "Expanded equalizer length"), S.MusicEqualizerBars, CompactMusicLayout.MinimumBars, CompactMusicLayout.MaximumBars, 1, Loc.T("根", "bars"),
            v => Change(s => s.MusicEqualizerBars = (int)v));
        pageBars.IsEnabled = S.MusicPage;
        Group(MusicChoice(Loc.T("律動方式", "Equalizer mode"), S.EqualizerMode, [(EqualizerMode.Flow, Loc.T("流動", "Flow")), (EqualizerMode.Spectrum, Loc.T("頻譜", "Spectrum"))],
                v => Change(s => s.EqualizerMode = v), help: Loc.T("流動：聲音的起伏從左流到右。頻譜：每根條顯示一段音高，左低右高。",
                    "Flow: the rise and fall of the sound runs from left to right. Spectrum: each bar shows a range of pitch, low on the left.")),
            Row(Loc.T("律動條顏色", "Equalizer color"), ColorSwatches("EqualizerColor", Loc.T("律動條顏色 ", "Equalizer color "), S.EqualizerColor, S.EqualizerCustomColor,
                UI.Brush(PanelTheme.Hex("equalizer")), (chosen, custom) => Change(s => { s.EqualizerColor = chosen; s.EqualizerCustomColor = custom; }))),
            SliderRow(Loc.T("收起時律動條長度", "Collapsed equalizer length"), S.EqualizerBars, CompactMusicLayout.MinimumBars, CompactMusicLayout.MaximumBars, 1, Loc.T("根", "bars"),
                v => Change(s => s.EqualizerBars = (int)v)),
            pageBars,
            MusicChoice(Loc.T("收起時律動條顯示時機", "Collapsed equalizer shown"), S.EqualizerAlways, [(false, Loc.T("播放音樂時", "While music plays")), (true, Loc.T("始終顯示", "Always"))],
                v => Change(s => s.EqualizerAlways = v), help: Loc.T("在收起形態中開啟律動條後生效。始終顯示時，沒有音樂播放也會顯示，讀取系統的全部聲音。",
                    "Applies once the equalizer is switched on in the collapsed content. When always shown, it also shows while no music plays, reading all system sound.")));
        SectionTitle(Loc.T("收起時", "When collapsed"));
        // The instrumental choice belongs to the lyric, so it shows only while the middle text is the lyric.
        var text = new List<FrameworkElement> { MusicChoice(Loc.T("中間文字", "Middle text"), S.MusicText, new[] { (CompactMusicText.None, Loc.T("無", "None")), (CompactMusicText.Title, Loc.T("歌名", "Title")), (CompactMusicText.Lyrics, Loc.T("歌詞", "Lyrics")) },
            v => { Change(s => s.MusicText = v); ShowPage(_page); }) };
        if (S.MusicText == CompactMusicText.Lyrics)
            text.Add(Toggle(Loc.T("純音樂時顯示歌名", "Show the title for instrumentals"), S.MusicInstrumentalTitle, v => Change(s => s.MusicInstrumentalTitle = v),
                help: Loc.T("歌曲本身沒有可靠的純音樂標識，只能根據歌詞內容推斷，因此可能判斷錯誤。",
                    "Songs carry no reliable instrumental mark, so this is inferred from the lyrics and can be wrong.")));
        // The two length choices are separate; each shows with its own middle text.
        if (S.MusicText == CompactMusicText.Lyrics)
            text.Add(Toggle(Loc.T("歌詞區域隨歌詞長度變化", "Lyric area follows the line length"), S.MusicLyricAutoWidth, v => Change(s => s.MusicLyricAutoWidth = v),
                help: Loc.T("需要為劉海或膠囊開啟“自適應寬度”。", "Needs \"Fit width to content\" for the notch or capsule.")));
        else if (S.MusicText == CompactMusicText.Title)
            text.Add(Toggle(Loc.T("歌名區域隨歌名長度變化", "Title area follows the title length"), S.MusicTitleAutoWidth, v => Change(s => s.MusicTitleAutoWidth = v),
                help: Loc.T("需要為劉海或膠囊開啟“自適應寬度”。", "Needs \"Fit width to content\" for the notch or capsule.")));
        Group([.. text,
            MusicChoice(Loc.T("暫停時隱藏", "Hide while paused"), S.MusicPausedHide, new[] { (PausedMusicHide.None, Loc.T("不隱藏", "Nothing")), (PausedMusicHide.Text, Loc.T("文字", "Text")),
                (PausedMusicHide.Cover, Loc.T("封面", "Cover")), (PausedMusicHide.Both, Loc.T("全部", "Both")) }, v => Change(s => s.MusicPausedHide = v),
                help: Loc.T("音樂暫停時，收起狀態下隱藏所選內容，恢復播放後重新顯示。“文字”指按“中間文字”顯示的歌名或歌詞；“全部”指文字和封面，不包括律動條。",
                    "While music is paused, the chosen part is hidden when collapsed and comes back when playback resumes. \"Text\" is the title or lyric shown by \"Middle text\"; \"Both\" is the text and the cover, not the equalizer.")),
            Toggle(Loc.T("切歌時短暫顯示歌名", "Briefly show the title on a track change"), S.MusicTrackNotice, v => Change(s => s.MusicTrackNotice = v)),
            Toggle(Loc.T("顯示封面", "Show cover"), S.MusicCompactCover, v => Change(s => s.MusicCompactCover = v))]);
        SectionTitle(Loc.T("展開時", "When expanded"));
        var lyricColor = Row(Loc.T("當前歌詞顏色", "Current lyric color"), ColorSwatches("MusicLyricColor", Loc.T("當前歌詞顏色 ", "Current lyric color "),
            S.MusicLyricColor, S.MusicLyricCustomColor, UI.Brush(PanelTheme.Hex("textPrimary")),
            (chosen, custom) => Change(s => { s.MusicLyricColor = chosen; s.MusicLyricCustomColor = custom; })));
        lyricColor.IsEnabled = S.MusicPage;
        var rows = new List<FrameworkElement>
        {
            Toggle(Loc.T("顯示封面", "Show cover"), S.MusicPageCover, v => { Change(s => s.MusicPageCover = v); ShowPage(_page); }, S.MusicPage),
            Toggle(Loc.T("顯示歌詞", "Show lyrics"), S.LyricsEnabled, v => { Change(s => s.LyricsEnabled = v); ShowPage(_page); }, S.MusicPage), lyricColor
        };
        if (PageLayoutCards() is { } layout) rows.Add(layout);
        Group([.. rows]);
    }

    // The open page's layout, the last setting of "展開時": its name and, under it, the cards to choose from. The cards follow what
    // the page shows: with the cover and the lyrics both, the two layouts; with the lyrics alone, the columns card without a
    // record and the stacked one at the page's left or in the middle (MusicLyricsAlign); with the cover alone, the columns card
    // without its divider and lyrics and the stacked one without the lyrics. With neither the page has only the titles, centred
    // whatever was chosen, and there is nothing to choose. Each card draws its own diagram (MusicLyricsPreview).
    private FrameworkElement? PageLayoutCards()
    {
        bool cover = S.MusicPageCover, lyrics = S.LyricsEnabled;
        if (!cover && !lyrics) return null;
        var choices = new List<(string Name, bool Selected, Func<FrameworkElement> Preview, Action Choose)>();
        void AddCard(string name, bool selected, Func<FrameworkElement> preview, MusicLyricsLayout layout, MusicLyricsAlign? align = null) => choices.Add((name, selected, preview, () =>
        {
            if (S.MusicLyricsLayout == layout && (align is null || S.MusicLyricsAlign == align)) return;
            Change(s => { s.MusicLyricsLayout = layout; if (align is { } value) s.MusicLyricsAlign = value; });
        }));
        if (lyrics && cover)
        {
            AddCard(Loc.T("堆疊", "Stacked"), S.MusicLyricsLayout == MusicLyricsLayout.Stacked,
                () => MusicLyricsPreview.StackedWithCover(_palette), MusicLyricsLayout.Stacked);
            AddCard(Loc.T("分欄", "Separate columns"), S.MusicLyricsLayout == MusicLyricsLayout.Columns,
            () => MusicLyricsPreview.ColumnsWithCover(_palette), MusicLyricsLayout.Columns);
        }
        else if (lyrics)
        {
            AddCard(Loc.T("居左", "Left"), S.MusicLyricsLayout == MusicLyricsLayout.Stacked && S.MusicLyricsAlign == MusicLyricsAlign.Left,
                () => MusicLyricsPreview.StackedWithoutCover(_palette, MusicLyricsAlign.Left), MusicLyricsLayout.Stacked, MusicLyricsAlign.Left);
            AddCard(Loc.T("居中", "Center"), S.MusicLyricsLayout == MusicLyricsLayout.Stacked && S.MusicLyricsAlign == MusicLyricsAlign.Center,
                () => MusicLyricsPreview.StackedWithoutCover(_palette, MusicLyricsAlign.Center), MusicLyricsLayout.Stacked, MusicLyricsAlign.Center);
            AddCard(Loc.T("分欄", "Separate columns"), S.MusicLyricsLayout == MusicLyricsLayout.Columns,
            () => MusicLyricsPreview.ColumnsWithoutCover(_palette), MusicLyricsLayout.Columns);
        }
        else
        {
            AddCard(Loc.T("堆疊", "Stacked"), S.MusicLyricsLayout == MusicLyricsLayout.Stacked,
                () => MusicLyricsPreview.StackedWithoutLyrics(_palette), MusicLyricsLayout.Stacked);
            AddCard(Loc.T("分欄", "Separate columns"), S.MusicLyricsLayout == MusicLyricsLayout.Columns,
            () => MusicLyricsPreview.ColumnsWithoutLyrics(_palette), MusicLyricsLayout.Columns);
        }
        var cards = new UniformGrid { Columns = choices.Count, Margin = new Thickness(-6, 6, -6, 0), IsEnabled = S.MusicPage };
        foreach (var (name, selected, preview, choose) in choices)
        {
            var card = new RadioButton { Style = (Style)FindResource("SettingsChoice"), Content = preview(), Margin = new Thickness(6, 3, 6, 0), Tag = name, GroupName = "MusicLyricsLayout", IsChecked = selected };
            AutomationProperties.SetName(card, name);
            card.Checked += (_, _) => choose();
            cards.Children.Add(card);
        }
        var title = SettingLabel(Loc.T("布局", "Layout")); title.Margin = new Thickness(0, 12, 0, 0);
        var panel = new StackPanel();
        panel.Children.Add(title); panel.Children.Add(cards);
        return panel;
    }
    // Operations only one player supports, grouped under that player's name.
    private void Players()
    {
        SectionTitle(Loc.T("播放來源", "Playback sources"));
        Group(IgnoredAppsRow());
        SectionTitle(Loc.T("網易雲音樂", "NetEase Cloud Music"));
        // Without full control NetEase is controlled through its system media session, an option in its own settings.
        PlayerNote(Loc.T("在網易雲音樂“設定 › 系統”中開啟“開啟 SMTC”，即可控制播放。",
            "Turn on \"开启 SMTC\" in NetEase Cloud Music's 设置 › 系统 to control playback."));
        Group(Toggle(Loc.T("始終顯示“啟用完整控制”按鈕", "Always show \"Enable full control\""), S.NeteaseFullControlEntry, v => Change(s => s.NeteaseFullControlEntry = v), S.MusicPage,
            Loc.T("在音樂頁面右上角的來源選單末尾固定顯示“網易雲音樂 · 完整控制”，以便隨時啟用完整控制。啟用完整控制後，可以在面板中拖動網易雲音樂的播放進度、切換播放模式，以及喜歡或取消喜歡當前歌曲。",
                "Keeps \"NetEase Cloud Music · Full control\" at the end of the source menu at the top right of the music page, so full control can be enabled at any time. With full control, NetEase Cloud Music's playback position can be dragged in the panel, its playback mode switched, and the current song liked or unliked.")),
            Toggle(Loc.T("啟動後自動啟用完整控制", "Enable full control when it starts"), S.NeteaseAutoFullControl, NeteaseAutoFullControl, S.MusicPage,
                Loc.T("網易雲音樂以普通方式啟動後，BrimDeck 會自動將它重啟一次，改為完整控制，通常需要幾秒鐘。",
                    "When NetEase Cloud Music is started the normal way, BrimDeck restarts it once with full control, which usually takes a few seconds.")),
            NeteaseSwitch());
        SectionTitle(Loc.T("酷狗音樂", "KuGou Music"));
        // Without full control KuGou is seen only through its system media session, an option in KuGou's own settings.
        PlayerNote(Loc.T("在酷狗“設定 › 常規設定 › 播放”中勾選“支援系統播放控制元件，如鎖屏介面”，即可顯示曲目並控制播放。",
            "Check \"支持系统播放控件，如锁屏界面\" in KuGou's 设置 › 常规设置 › 播放 to show the track and control playback."));
        Group(KugouSwitch());
        SectionTitle("Apple Music");
        Group(Toggle(Loc.T("始終啟用完整控制", "Always enable full control"), S.AppleMusicFullControl, v => Change(s => s.AppleMusicFullControl = v), S.MusicPage,
            Loc.T("BrimDeck 啟動後自動啟用 Apple Music 的完整控制。", "Enables full control of Apple Music when BrimDeck starts.")));
        // QQ Music is read through its own pipe; it has nothing to set.
        SectionTitle(Loc.T("QQ 音樂", "QQ Music"));
        PlayerNote(Loc.T("無需任何設定。", "Needs no setup."), last: true);
    }
    // Whether the ignored apps are shown under their row, kept while the row is rebuilt after an app is added or removed.
    private bool _ignoredOpen;
    // Holds the row, so that adding or removing an app rebuilds the row alone, as the app rules row does.
    private Border? _ignoredHost;

    // The applications whose playback the panel leaves out. The menu offers the apps that report playback now, and any
    // executable. An executable is added under its file name, the id most players report; a player that reports an id of
    // its own choosing is matched only while it plays and can be added from the menu then.
    private FrameworkElement IgnoredAppsRow() => _ignoredHost = new Border { Child = IgnoredAppsContent() };
    private void RefreshIgnoredApps() { if (_ignoredHost is not null) _ignoredHost.Child = IgnoredAppsContent(); }

    private FrameworkElement IgnoredAppsContent() => AppListRow(Loc.T("忽略的應用", "Ignored apps"),
        () => Loc.T("被忽略的應用播放時，面板不顯示它的歌曲，來源選單中也不列出它。“新增應用”列出正在播放或已暫停的應用，也可以瀏覽可執行檔案。忽略瀏覽器會忽略其中所有網頁的播放。",
            "While an ignored app plays, the panel does not show its songs and the source menu leaves it out. \"Add app\" lists the apps that are playing or paused, or browse for an executable. Ignoring a browser ignores playback in all its pages."),
        Loc.T("展開忽略的應用", "Show ignored apps"), Loc.T("新增要忽略的應用", "Add an app to ignore"),
        [.. S.IgnoredMediaApps.Select(IgnoredAppLine)], IgnoredAppMenu, _ignoredOpen, open => _ignoredOpen = open);

    private FrameworkElement IgnoredAppLine(IgnoredMediaApp app)
    {
        string name = app.Name.Length > 0 ? app.Name : app.AppId;
        return AppLine(name, IgnoredAppDetail(app), app.Path, app.Path.Length > 0 ? app.Path : app.AppId, () =>
        {
            Change(s => s.IgnoredMediaApps.RemoveAll(entry => string.Equals(entry.AppId, app.AppId, StringComparison.OrdinalIgnoreCase)));
            RefreshIgnoredApps();
        });
    }

    // The executable's file name when it is known, otherwise the id the app's media session reports.
    private static string IgnoredAppDetail(IgnoredMediaApp app) => app.Path.Length > 0 ? System.IO.Path.GetFileName(app.Path) : app.AppId;

    private void IgnoredAppMenu(Button anchor)
    {
        var menu = AppMenu(anchor);
        var apps = (((App)Application.Current).Media?.DetectedApps() ?? [])
            .Where(app => !S.IgnoredMediaApps.Any(entry => string.Equals(entry.AppId, app.AppId, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var app in apps)
            menu.Items.Add(AppMenuItem(app.Name, IgnoredAppDetail(app), app.Path, () =>
            {
                Change(s => s.IgnoredMediaApps.Add(app));
                RefreshIgnoredApps();
            }));
        if (apps.Count == 0) menu.Items.Add(new MenuItem { Header = Loc.T("沒有正在播放的應用", "No app is playing"), IsEnabled = false });
        AddBrowseItem(menu, Loc.T("選擇要忽略的應用", "Choose an app to ignore"), path =>
        {
            var app = new IgnoredMediaApp(System.IO.Path.GetFileName(path), AppName(System.IO.Path.GetFileNameWithoutExtension(path), path), path);
            if (S.IgnoredMediaApps.Any(entry => string.Equals(entry.AppId, app.AppId, StringComparison.OrdinalIgnoreCase))) return;
            Change(s => s.IgnoredMediaApps.Add(app));
            RefreshIgnoredApps();
        });
        menu.IsOpen = true;
    }

    // A line of small text between a player's title and its settings; last: the player has no settings, so the line ends the
    // section with a group's spacing.
    private void PlayerNote(string text, bool last = false)
    {
        var note = TextLine(text, 11, TextTertiary);
        note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(0, 0, 0, last ? 28 : 8); Target.Children.Add(note);
    }
    // KuGou's full control as one switch, since a changed libcef.dll makes every start of KuGou a full control start. On: after
    // the explanation the file is changed and a running KuGou restarts; off: the file is restored and KuGou keeps running until
    // its next start. A line beneath only when something needs saying. It follows the player while the page is open.
    private FrameworkElement KugouSwitch()
    {
        var app = (App)Application.Current;
        string title = Loc.T("始終啟用完整控制", "Always enable full control");
        // The file is read when the page opens: here for the switch, and by media to change it again when an update left it
        // original while the setting is on. A file another tool or an earlier change left changed is on as well: KuGou starts
        // with full control either way.
        var read = app.Media?.KugouState ?? Native.KugouInstall.State();
        app.Media?.CheckKugouInstall(now: true);
        bool changed = read == LibcefPatchState.Patched;
        var toggle = new CheckBox { Style = (Style)FindResource("SettingsToggle"), IsChecked = S.KugouFullControl || changed, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, title);
        var note = TextLine("", 11, TextTertiary); note.TextWrapping = TextWrapping.Wrap;
        bool busy = false, quiet = false;
        string? failure = null;
        async Task Switch(bool on)
        {
            if (quiet) return;
            if (on && new KugouControlWindow(S.Theme) { Owner = this }.ShowDialog() != true) { Set(false); return; }
            busy = true; failure = null; Update();
            try
            {
                if (on)
                {
                    failure = app.Media is { } media ? await media.EnableKugouFullControlAsync(start: false)
                        : await Task.Run(async () => { await Native.KugouInstall.EnableAsync(false, CancellationToken.None); return (string?)null; });
                    if (failure is not null) Set(false);
                    else Change(s => s.KugouFullControl = true);
                }
                else
                {
                    Change(s => s.KugouFullControl = false);
                    await Task.Run(() => Native.KugouInstall.ChangeFileAsync(false, CancellationToken.None));
                }
            }
            catch (Exception ex) { failure = ex.Message; if (on) Set(false); }
            finally { busy = false; app.Media?.CheckKugouInstall(now: true); Update(); }
        }
        // Moves the switch without acting on it.
        void Set(bool on) { quiet = true; toggle.IsChecked = on; quiet = false; }
        toggle.Checked += async (_, _) => await Switch(true);
        toggle.Unchecked += async (_, _) => await Switch(false);
        void Update()
        {
            var media = app.Media;
            var state = media is not null ? media.KugouState ?? read : Native.KugouInstall.State();
            bool working = busy || media is { RestartingKugou: true };
            bool supported = state is LibcefPatchState.Original or LibcefPatchState.Patched or LibcefPatchState.Partial;
            toggle.IsEnabled = supported && !working && S.MusicPage;
            string? error = working ? null : failure ?? media?.KugouError;
            // The port follows the file only from KuGou's next start.
            bool pending = supported && (state == LibcefPatchState.Patched) != (media is { KugouFullControl: true }) && Native.KugouInstall.Running();
            note.Text = working ? Loc.T("正在處理…", "Working…")
                : error is not null ? Loc.T("啟用失敗：", "Could not turn on: ") + error
                : state is null ? Loc.T("未安裝酷狗音樂", "KuGou Music is not installed")
                : !supported ? Loc.T("不支持此版本", "This version is not supported")
                : pending ? Loc.T("酷狗重啟後生效", "Takes effect when KuGou restarts") : "";
            note.Foreground = UI.Brush(error is not null || state is not null && !supported ? _palette.Negative : TextTertiary);
            note.Visibility = note.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        void Changed() => Dispatcher.BeginInvoke(Update);
        Update();
        var row = Row(title, toggle, note, () => Loc.T("與上面勾選酷狗選項的方式相比，還會顯示實際播放進度和更清晰的封面，歌詞隨進度滾動。\n"
            + "啟用時會修改酷狗的 libcef.dll，之後酷狗每次啟動都是完整控制模式。",
            "Compared with checking the KuGou option above, the actual progress and a sharper cover are also shown, and the lyrics follow the song.\n"
            + "Turning it on changes KuGou's libcef.dll; KuGou then starts in full control mode every time."));
        row.Loaded += (_, _) => app.MediaChanged += Changed;
        row.Unloaded += (_, _) => app.MediaChanged -= Changed;
        return row;
    }
    // NetEase Cloud Music's full control as one switch, the same shape as KuGou's: a changed libcef.dll makes every start a full
    // control start. On: after the explanation the file is changed and a running player restarts; off: the file is restored and the
    // player keeps running until its next start.
    private FrameworkElement NeteaseSwitch()
    {
        var app = (App)Application.Current;
        string title = Loc.T("始終啟用完整控制", "Always enable full control");
        // Read when the page opens, as KuGou's.
        var read = app.Media?.NeteasePatchState ?? Native.NeteaseInstall.State();
        app.Media?.CheckNeteaseFile();
        bool changed = read == LibcefPatchState.Patched;
        var toggle = new CheckBox { Style = (Style)FindResource("SettingsToggle"), IsChecked = S.NeteaseFullControl || changed, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, title);
        var note = TextLine("", 11, TextTertiary); note.TextWrapping = TextWrapping.Wrap;
        bool busy = false, quiet = false;
        string? failure = null;
        async Task Switch(bool on)
        {
            if (quiet) return;
            if (on && new NeteaseFileControlWindow(S.Theme) { Owner = this }.ShowDialog() != true) { Set(false); return; }
            busy = true; failure = null; Update();
            try
            {
                if (on)
                {
                    failure = app.Media is { } media ? await media.EnableNeteaseFileControlAsync()
                        : await Task.Run(async () => { await Native.NeteaseInstall.EnableAsync(false, CancellationToken.None); return (string?)null; });
                    if (failure is not null) Set(false);
                    else Change(s => s.NeteaseFullControl = true);
                }
                else
                {
                    Change(s => s.NeteaseFullControl = false);
                    if (app.Media is { } media) await media.DisableNeteaseFileControlAsync();
                    else await Task.Run(() => Native.NeteaseInstall.ChangeFileAsync(false, CancellationToken.None));
                }
            }
            catch (Exception ex) { failure = ex.Message; if (on) Set(false); }
            finally { busy = false; Update(); }
        }
        void Set(bool on) { quiet = true; toggle.IsChecked = on; quiet = false; }
        toggle.Checked += async (_, _) => await Switch(true);
        toggle.Unchecked += async (_, _) => await Switch(false);
        void Update()
        {
            var media = app.Media;
            var state = media is not null ? media.NeteasePatchState ?? read : Native.NeteaseInstall.State();
            bool working = busy || media is { RestartingNetease: true };
            bool supported = state is LibcefPatchState.Original or LibcefPatchState.Patched or LibcefPatchState.Partial;
            // With media on, a null state there may only mean the source has not read the file yet; media.NeteaseInstalled
            // answers whether it is installed. With media off, the state read above is null only when not installed.
            bool installed = media is not null ? media.NeteaseInstalled : state is not null;
            toggle.IsEnabled = (supported || state is null && installed) && !working && S.MusicPage;
            string? error = working ? null : failure;
            // The port follows the file only from NetEase's next start.
            bool pending = supported && (state == LibcefPatchState.Patched) != (media is { NeteaseFullControl: true }) && Native.NeteaseInstall.Running();
            note.Text = working ? Loc.T("正在處理…", "Working…")
                : error is not null ? Loc.T("啟用失敗：", "Could not turn on: ") + error
                : !installed ? Loc.T("未安裝網易雲音樂", "NetEase Cloud Music is not installed")
                : state is not null && !supported ? Loc.T("不支持此版本", "This version is not supported")
                : pending ? Loc.T("網易雲重啟後生效", "Takes effect when NetEase Cloud Music restarts") : "";
            note.Foreground = UI.Brush(error is not null || state is not null && !supported ? _palette.Negative : TextTertiary);
            note.Visibility = note.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        void Changed() => Dispatcher.BeginInvoke(Update);
        Update();
        var row = Row(title, toggle, note, () => Loc.T("會修改網易雲的 libcef.dll，之後網易雲每次啟動都是完整控制模式。",
            "Changes NetEase Cloud Music's libcef.dll; it then starts in full control mode every time."));
        row.Loaded += (_, _) => app.MediaChanged += Changed;
        row.Unloaded += (_, _) => app.MediaChanged -= Changed;
        return row;
    }
    // Switching it on is confirmed with the explanation of the debugging port; declining rebuilds the page with the switch off.
    private void NeteaseAutoFullControl(bool on)
    {
        if (on && new NeteaseControlWindow(S.Theme, NeteaseControlAction.Auto) { Owner = this }.ShowDialog() != true)
        {
            Dispatcher.BeginInvoke(() => ShowPage(_page)); return;
        }
        Change(s => s.NeteaseAutoFullControl = on);
    }
    private void MusicSizeControls()
    {
        var dimensions = new Grid { Margin = new Thickness(0, 8, 0, 0) }; dimensions.ColumnDefinitions.Add(new()); dimensions.ColumnDefinitions.Add(new());
        var width = Dimension(Loc.T("寬度", "Width"), S.MusicWidth, 440, 1200, value => Resize(s => s.MusicWidth = value, DeckPage.Music)); width.Margin = new Thickness(0, 0, 20, 0); dimensions.Children.Add(width);
        var height = Dimension(Loc.T("高度", "Height"), S.MusicHeight, 140, 400, value => Resize(s => s.MusicHeight = value, DeckPage.Music)); height.Margin = new Thickness(20, 0, 0, 0); Grid.SetColumn(height, 1); dimensions.Children.Add(height);
        Target.Children.Add(dimensions);
        var sizes = KeepPreview(new UniformGrid { Columns = 3, Width = 292 });
        foreach (var size in Enum.GetValues<PanelSize>())
        {
            var (w, h) = MusicSizes.For(size);
            var choice = new RadioButton { Style = (Style)FindResource("SettingsSegment"), Content = PanelSizes.Name(size), GroupName = "MusicSize", IsChecked = (S.MusicWidth, S.MusicHeight) == (w, h) };
            AutomationProperties.SetName(choice, Loc.T("音樂快捷尺寸 ", "Music quick size ") + PanelSizes.Name(size)); choice.Checked += (_, _) => { Resize(s => { s.MusicWidth = w; s.MusicHeight = h; }, DeckPage.Music); ShowPage(2); }; sizes.Children.Add(choice);
        }
        QuickSizeRow(sizes);
    }
}