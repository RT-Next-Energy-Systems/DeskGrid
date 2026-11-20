using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using BrimDeck.Core;

namespace BrimDeck;

// "頁面佈局", first in the clipboard's group ("中轉站"): which columns the clipboard and file transfer page shows, and, when it
// shows both, how it arranges them, chosen on cards with a diagram of each arrangement (ShelfArrangementPreview). With one column
// there is nothing to arrange, so the arrangement and its settings are hidden. Under the cards stand the settings of the arrangement
// chosen, and only those, as on the collapsed view's page: side by side, how the width is shared between the columns, which
// dragging the divider on the panel also sets; stacked, the further ways of bringing the other card forward
// (MainWindow.ShelfArrange.cs). Below them, for each column the page shows, the way that column shows its entries (ClipView,
// FileView), chosen on cards as well (ShelfViewPreview).
public partial class SettingsWindow
{
    internal const int ShelfLayoutSettingsPage = 17;

    private static string ArrangementName(ShelfArrangement arrangement) =>
        arrangement == ShelfArrangement.Split ? Loc.T("並排", "Side by side") : Loc.T("疊放", "Stacked");

    private void ShelfLayoutSettings()
    {
        FrameworkElement? arranging = null, clips = null, files = null;
        // The arrangement shows with both columns; each column's own cards show while the page shows that column.
        void Show(ShelfShown shown)
        {
            if (arranging is null || clips is null || files is null) return;
            arranging.Visibility = shown == ShelfShown.Both ? Visibility.Visible : Visibility.Collapsed;
            clips.Visibility = shown != ShelfShown.Files ? Visibility.Visible : Visibility.Collapsed;
            files.Visibility = shown != ShelfShown.Clipboard ? Visibility.Visible : Visibility.Collapsed;
        }
        Group(MusicChoice(Loc.T("顯示內容", "Show"), S.ShelfShown,
            [(ShelfShown.Clipboard, Loc.T("剪貼簿", "Clipboard")), (ShelfShown.Files, Loc.T("檔案中轉", "File transfer")), (ShelfShown.Both, Loc.T("都顯示", "Both"))],
            value => { Change(s => s.ShelfShown = value); Show(value); }));
        arranging = Collect(Arrangement);
        clips = Collect(() => ViewCards(Loc.T("剪貼簿的顯示方式", "How the clipboard shows entries"), "ShelfClipView",
            [(ClipView.Single, Loc.T("單行", "One line")), (ClipView.Three, Loc.T("三行", "Three lines")), (ClipView.Blocks, Loc.T("方塊", "Blocks"))],
            S.ShelfClipView, value => { if (S.ShelfClipView != value) Change(s => s.ShelfClipView = value); }, value => ShelfViewPreview.Create(value, _palette)));
        files = Collect(() => ViewCards(Loc.T("檔案中轉的顯示方式", "How the file transfer shows files"), "ShelfFileView",
            [(FileView.Single, Loc.T("單行", "One line")), (FileView.Two, Loc.T("雙行", "Two lines")), (FileView.Grid, Loc.T("網格", "Grid")),
                (FileView.Row, Loc.T("橫排", "Row")), (FileView.Column, Loc.T("豎排", "Column"))],
            S.ShelfFileView, value =>
            {
                if (S.ShelfFileView != value) Change(s => s.ShelfFileView = value);
                // The widths' slider reaches as far as the way chosen lets the file transfer narrow.
                if (_shareSlider is { } slider) slider.Maximum = 100 - ShelfLayout.LeastShare(value);
            }, value => ShelfViewPreview.Create(value, _palette)));
        Target.Children.Add(arranging); Target.Children.Add(clips); Target.Children.Add(files);
        Show(S.ShelfShown);
    }

    // A row of cards that choose one way of showing a column, each with its diagram and its name under it; the cards share the
    // row's width. Choosing does not open the panel.
    private void ViewCards<T>(string title, string group, IReadOnlyList<(T Value, string Name)> choices, T current, Action<T> choose, Func<T, FrameworkElement> preview) where T : struct, Enum
    {
        SectionTitle(title);
        var cards = new UniformGrid { Columns = choices.Count, Margin = new Thickness(-6, 0, -6, 12) };
        foreach (var (value, name) in choices)
        {
            var card = new RadioButton
            {
                Style = (Style)FindResource("SettingsChoice"), Content = preview(value), Margin = new Thickness(6, 3, 6, 0), Tag = name, GroupName = group,
                IsChecked = EqualityComparer<T>.Default.Equals(current, value)
            };
            AutomationProperties.SetName(card, name);
            card.Checked += (_, _) => choose(value);
            cards.Children.Add(card);
        }
        Target.Children.Add(cards);
    }

    private void Arrangement()
    {
        SectionTitle(Loc.T("排列方式", "Arrangement"));
        // Two cards over the whole row, as the collapsed style's three are, with the same room for the focus ring. Their diagrams
        // are wider than those, so the cards are no taller (ShelfArrangementPreview).
        var cards = new UniformGrid { Columns = 2, Margin = new Thickness(-6, 0, -6, 24) };
        var options = new Grid();
        // The settings of an arrangement: side by side the widths, stacked the ways of switching. Chosen on its card, they take the
        // place of the other's, as the collapsed style's options do.
        void Show(ShelfArrangement arrangement, ShelfArrangement? previous)
        {
            var incoming = Collect(() =>
            {
                if (arrangement == ShelfArrangement.Split)
                    Group(ColumnWidths());
                else
                {
                    Group(MusicChoice(Loc.T("堆疊位置", "Stack position"), S.ShelfSameSideStack,
                        [(false, Loc.T("異側堆疊", "Opposite sides")), (true, Loc.T("同側堆疊", "Same side"))],
                        value => Change(s => s.ShelfSameSideStack = value)));
                    SectionTitle(Loc.T("切換方式", "Switching"), help: () => Loc.T(
                        "單擊側條、卡片標題或當前頁圖示也能切換。",
                        "A click on the side strip, the card's title or the current page's icon switches as well."));
                    Group(Toggle(Loc.T("指標懸停在側條時切換", "Switch when the pointer hovers over the side strip"), S.ShelfHoverSwitch, value => Change(s => s.ShelfHoverSwitch = value)),
                        Toggle(Loc.T("按下滑鼠中鍵切換", "Switch with a middle click"), S.ShelfMiddleSwitch, value => Change(s => s.ShelfMiddleSwitch = value)),
                        Toggle(Loc.T("觸控板雙指橫滑或 Shift + 滾輪切換", "Switch with a two-finger sideways swipe or Shift + wheel"), S.ShelfSwipeSwitch, value => Change(s => s.ShelfSwipeSwitch = value)));
                }
            });
            SwapOptions(options, incoming, previous is { } from && from != arrangement ? (arrangement > from ? 1 : -1) : 0);
        }
        foreach (var arrangement in Enum.GetValues<ShelfArrangement>())
        {
            // The diagrams stand still, whatever the pointer does and whatever the widths are set to: they only say what the
            // arrangement is.
            var card = new RadioButton
            {
                Style = (Style)FindResource("SettingsChoice"), Content = ShelfArrangementPreview.Create(arrangement, _palette),
                Margin = new Thickness(6, 3, 6, 0), Tag = ArrangementName(arrangement), GroupName = "ShelfArrangement", IsChecked = S.ShelfArrangement == arrangement
            };
            AutomationProperties.SetName(card, ArrangementName(arrangement));
            card.Checked += (_, _) =>
            {
                var previous = S.ShelfArrangement;
                if (previous != arrangement) Change(s => s.ShelfArrangement = arrangement);
                Show(arrangement, previous);
            };
            cards.Children.Add(card);
        }
        Target.Children.Add(cards); Target.Children.Add(options);
        Show(S.ShelfArrangement, null);
    }

    // "兩欄寬度": a slider that stands for the divider, with the clipboard's name and share at its left and the file transfer's at
    // its right. Moved to the right it widens the clipboard, as the divider does on the panel. The names stand outside the
    // slider, so neither is ever cut, however narrow its column.
    private Slider? _shareSlider;
    private bool _shareFollowing;
    // The divider was dragged on the panel: the slider follows it.
    internal void ShelfShareChanged()
    {
        if (!IsLoaded || _page != ShelfLayoutSettingsPage || _shareSlider is not { IsLoaded: true } slider) return;
        _shareFollowing = true;
        try { slider.Value = Math.Clamp(100 - ShelfLayout.ClampShare(S.ShelfFileShare, S.ShelfFileView), slider.Minimum, slider.Maximum); }
        finally { _shareFollowing = false; }
    }
    private FrameworkElement ColumnWidths()
    {
        // In one column the file transfer may be a narrow strip (ShelfLayout.LeastShare), so the clipboard may take more.
        double least = 100 - ShelfLayout.MaxShare, most = 100 - ShelfLayout.LeastShare(S.ShelfFileView);
        var slider = new Slider
        {
            Style = (Style)FindResource("SettingsSlider"), Minimum = least, Maximum = most, Value = Math.Clamp(100 - ShelfLayout.ClampShare(S.ShelfFileShare, S.ShelfFileView), least, most),
            SmallChange = 1, LargeChange = 5, IsMoveToPointEnabled = true, Width = 190,
            Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center
        };
        TextBlock Number(TextAlignment align)
        {
            var number = TextLine("", 12, TextPrimary, FontWeights.SemiBold);
            number.MinWidth = 32; number.TextAlignment = align; number.VerticalAlignment = VerticalAlignment.Center;
            Typography.SetNumeralAlignment(number, FontNumeralAlignment.Tabular);
            return number;
        }
        TextBlock Name(string text) { var name = TextLine(text, 12, TextSecondary); name.VerticalAlignment = VerticalAlignment.Center; return name; }
        var clipsNumber = Number(TextAlignment.Right); var filesNumber = Number(TextAlignment.Left);
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var part in new UIElement[] { Name(Loc.T("剪貼簿", "Clipboard")), clipsNumber, slider, filesNumber, Name(Loc.T("檔案中轉", "File transfer")) }) line.Children.Add(part);
        void Show()
        {
            double clips = slider.Value;
            clipsNumber.Text = clips.ToString("0", CultureInfo.InvariantCulture) + "%";
            filesNumber.Text = (100 - clips).ToString("0", CultureInfo.InvariantCulture) + "%";
            AutomationProperties.SetName(slider, Loc.T($"兩欄寬度：剪貼簿 {clips:0}%，檔案中轉 {100 - clips:0}%", $"Column widths: clipboard {clips:0}%, file transfer {100 - clips:0}%"));
        }
        Show();
        _shareSlider = slider;
        KeepPreview(slider);
        // The panel opens on the page while the widths are set here, as it does for a page's size, so the change is seen. While the
        // thumb is dragged the panel's columns follow it directly, as they follow the divider, and nothing is saved until it is
        // let go: saving applies all the settings again, which is too slow to do for every step of a drag.
        bool dragging = false;
        double Share() => Math.Round(100 - slider.Value, 1);
        slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) =>
        {
            dragging = true;
            if (!DeckPages.Shown(S, DeckPage.Clipboard)) return;
            _preview = true; _app.PreviewDeck.Preview(true); _app.PreviewDeck.SelectPage(DeckPage.Clipboard, remember: false);
        }));
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
        {
            dragging = false;
            double share = Share();
            if (Math.Abs(share - S.ShelfFileShare) >= .05) Change(s => s.ShelfFileShare = share);
            _app.PreviewDeck.PreviewShelfShare(null);
        }));
        slider.ValueChanged += (_, _) =>
        {
            Show();
            // Following the panel's divider, the share is already saved: it is not written back.
            if (_shareFollowing) return;
            double share = Share();
            if (dragging) _app.PreviewDeck.PreviewShelfShare(share);
            else if (Math.Abs(share - S.ShelfFileShare) >= .05) Resize(s => s.ShelfFileShare = share, DeckPage.Clipboard);
        };
        return Row(Loc.T("兩欄寬度", "Column widths"), line, help: () => Loc.T(
            "在面板上拖動分隔線也能調整。雙擊分隔線，恢復為剪貼簿 60%、檔案中轉 40%。",
            "Dragging the divider on the panel works as well. A double-click on it gives back 60% for the clipboard and 40% for the file transfer."));
    }
}
