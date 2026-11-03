using System.Globalization;

namespace DeskGrid.Taskbar;

/// <summary>
/// What a second DeskGrid process sends the running one: which panel to open and where the cursor was when the
/// process started (the click on the taskbar button), since by the time the request arrives the cursor may have moved on.
/// </summary>
/// <remarks>One line on the pipe: <c>x TAB y TAB name</c>. A tab can't be part of a file name.</remarks>
internal readonly record struct OpenMessage(string Name, int CursorX, int CursorY)
{
    private const char Separator = '\t';

    public string Format() => string.Join(Separator, CursorX.ToString(CultureInfo.InvariantCulture), CursorY.ToString(CultureInfo.InvariantCulture), Name);

    public static OpenMessage? TryParse(string line) =>
        line.Split(Separator, 3) is [var x, var y, { Length: > 0 } name]
        && int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cursorX)
        && int.TryParse(y, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cursorY)
            ? new OpenMessage(name, cursorX, cursorY)
            : null;
}
