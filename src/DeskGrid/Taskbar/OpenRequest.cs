namespace DeskGrid.Taskbar;

/// <summary>
/// The command line of a taskbar shortcut: <c>DeskGrid.exe --open "Folder"</c> asks the running DeskGrid
/// to open that folder's panel, <c>DeskGrid.exe --all</c> the list of all folders.
/// </summary>
internal static class OpenRequest
{
    /// <summary>What <see cref="Parse"/> returns for the list of all folders; no folder can have this name.</summary>
    public const string AllFolders = "*";

    private const string Switch = "--open";
    private const string AllSwitch = "--all";

    public static string AllFoldersArguments => AllSwitch;

    public static string Arguments(string folderName) => $"{Switch} \"{folderName}\"";

    /// <summary>
    /// The folder name asked for, <see cref="AllFolders"/>, or <see langword="null"/> if <paramref name="args"/>
    /// isn't an open request.
    /// </summary>
    /// <summary>
    /// What <see cref="Parse"/> gives for the arguments as a shortcut stores them (<see cref="Arguments"/> and
    /// <see cref="AllFoldersArguments"/> are what is written), or <see langword="null"/> if they are not an open request.
    /// </summary>
    public static string? ParseArguments(string arguments)
    {
        var text = arguments.Trim();
        if (string.Equals(text, AllSwitch, StringComparison.OrdinalIgnoreCase))
        {
            return AllFolders;
        }

        var prefix = Switch + " \"";
        return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && text.EndsWith('"') && text.Length > prefix.Length + 1
            ? text[prefix.Length..^1]
            : null;
    }

    public static string? Parse(IReadOnlyList<string> args) => args switch
    {
        [var option, ..] when string.Equals(option, AllSwitch, StringComparison.OrdinalIgnoreCase) => AllFolders,
        [var option, { Length: > 0 } name, ..] when string.Equals(option, Switch, StringComparison.OrdinalIgnoreCase) => name,
        _ => null,
    };
}
