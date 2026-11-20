using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using BrimDeck.Core;

namespace BrimDeck;

// The music page's gestures (MusicGestures), on a page of their own under music: dragging with the left button in four directions,
// and a two-finger touchpad swipe to either side. A two-finger swipe up or down is the mouse wheel's (DeckSettings.MouseWheel).
public sealed partial class SettingsWindow
{
    internal const int MusicGesturesPage = 15;

    internal static string GestureName(MusicGestureAction action) => action switch
    {
        MusicGestureAction.NextTrack => Loc.T("下一首", "Next song"),
        MusicGestureAction.PreviousTrack => Loc.T("上一首", "Previous song"),
        MusicGestureAction.NextPlayer => Loc.T("下一個播放器", "Next player"),
        MusicGestureAction.PreviousPlayer => Loc.T("上一個播放器", "Previous player"),
        MusicGestureAction.PlayPause => Loc.T("播放/暫停", "Play/pause"),
        MusicGestureAction.VolumeUp => Loc.T("增大音量", "Volume up"),
        MusicGestureAction.VolumeDown => Loc.T("減小音量", "Volume down"),
        _ => Loc.T("無操作", "No action")
    };

    private void MusicGesturesSettings()
    {
        SectionTitle(Loc.T("滑鼠拖動", "Mouse drag"));
        Group(GestureRow(Loc.T("向左拖動", "Drag left"), S.MusicDragLeft, v => Change(s => s.MusicDragLeft = v)),
            GestureRow(Loc.T("向右拖動", "Drag right"), S.MusicDragRight, v => Change(s => s.MusicDragRight = v)),
            GestureRow(Loc.T("向上拖動", "Drag up"), S.MusicDragUp, v => Change(s => s.MusicDragUp = v)),
            GestureRow(Loc.T("向下拖動", "Drag down"), S.MusicDragDown, v => Change(s => s.MusicDragDown = v)));
        SectionTitle(Loc.T("觸控板", "Touchpad"), help: () => Loc.T("在音樂頁面上用兩根手指左右滑動。左右撥動滑鼠滾輪也會觸發。兩根手指上下滑動按“滑鼠滾輪”設定處理。",
            "Swipe sideways with two fingers over the music page. Tilting the mouse wheel sideways does the same. A two-finger swipe up or down follows the mouse wheel setting."));
        Group(GestureRow(Loc.T("雙指左滑", "Two-finger swipe left"), S.MusicSwipeLeft, v => Change(s => s.MusicSwipeLeft = v)),
            GestureRow(Loc.T("雙指右滑", "Two-finger swipe right"), S.MusicSwipeRight, v => Change(s => s.MusicSwipeRight = v)));
    }

    private FrameworkElement GestureRow(string title, MusicGestureAction selected, Action<MusicGestureAction> change)
    {
        var actions = Enum.GetValues<MusicGestureAction>();
        var choice = new ComboBox { Width = 150, Style = (Style)FindResource("SettingsCombo"), ItemsSource = actions.Select(GestureName).ToList(),
            SelectedIndex = Array.IndexOf(actions, selected), IsEnabled = S.MusicPage };
        AutomationProperties.SetName(choice, title); PopupMotion.Attach(choice);
        choice.SelectionChanged += (_, _) => { if (choice.SelectedIndex >= 0) change(actions[choice.SelectedIndex]); };
        return Row(title, choice);
    }
}
