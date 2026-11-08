using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeskGrid.Hotkeys;
using DeskGrid.Resources;

namespace DeskGrid.Settings;

/// <summary>
/// A box that records a shortcut: click it and press the keys. Backspace or Delete clears it. It never types anything itself.
/// </summary>
internal sealed class HotkeyBox : TextBox
{
    private Hotkey? _value;

    public HotkeyBox()
    {
        // A style made for TextBox (the Fluent theme's) isn't applied to a class derived from it unless asked for.
        SetResourceReference(StyleProperty, typeof(TextBox));
        IsReadOnly = true;
        IsUndoEnabled = false;
        VerticalContentAlignment = VerticalAlignment.Center;
        Padding = new Thickness(6, 5, 6, 5);
        Cursor = Cursors.Hand;
        ContextMenuService.SetIsEnabled(this, false);
        ShowIdle();
    }

    /// <summary>The recorded shortcut, or <see langword="null"/> for none. Setting it does not raise <see cref="ValueChanged"/>.</summary>
    public Hotkey? Value
    {
        get => _value;
        set
        {
            _value = value;
            ShowIdle();
        }
    }

    /// <summary>Asked before a recorded shortcut is accepted: returns why it can't be used, or <see langword="null"/>.</summary>
    public Func<Hotkey, string?>? Check { get; set; }

    /// <summary>Raised when the user recorded or cleared a shortcut.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>Raised with the reason when keys were pressed that can't be used.</summary>
    public event EventHandler<string>? Rejected;

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        ShowIdle();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        ShowIdle();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        var modifiers = HeldModifiers();

        // Tab moves on and Esc closes the window, as everywhere else.
        if (modifiers == HotkeyModifiers.None && key is Key.Tab or Key.Escape)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        if (IsModifier(key))
        {
            Text = Hotkey.ModifierPrefix(modifiers);
            FontStyle = FontStyles.Normal;
        }
        else if (modifiers == HotkeyModifiers.None && key is Key.Back or Key.Delete)
        {
            Commit(null);
        }
        else if (Hotkey.IsKnownKey(KeyInterop.VirtualKeyFromKey(key)))
        {
            Record(new Hotkey(modifiers, KeyInterop.VirtualKeyFromKey(key)));
        }
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (HeldModifiers() == HotkeyModifiers.None)
        {
            ShowIdle();
        }
    }

    private void Record(Hotkey hotkey)
    {
        if (!hotkey.IsValid)
        {
            Rejected?.Invoke(this, Strings.HotkeysNeedModifier);
        }
        else if (Check?.Invoke(hotkey) is { } problem)
        {
            Rejected?.Invoke(this, problem);
        }
        else
        {
            Commit(hotkey);
        }
    }

    private void Commit(Hotkey? hotkey)
    {
        Value = hotkey;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    // What is shown when no key is being held: the shortcut, or a hint about what to do.
    private void ShowIdle()
    {
        Text = _value?.ToString() ?? (IsKeyboardFocused ? Strings.HotkeysPress : Strings.HotkeysNone);
        FontStyle = _value is null ? FontStyles.Italic : FontStyles.Normal;
    }

    private static HotkeyModifiers HeldModifiers()
    {
        var held = Keyboard.Modifiers;
        return (held.HasFlag(ModifierKeys.Control) ? HotkeyModifiers.Ctrl : 0)
            | (held.HasFlag(ModifierKeys.Alt) ? HotkeyModifiers.Alt : 0)
            | (held.HasFlag(ModifierKeys.Shift) ? HotkeyModifiers.Shift : 0)
            | (held.HasFlag(ModifierKeys.Windows) ? HotkeyModifiers.Win : 0);
    }

    private static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
}
