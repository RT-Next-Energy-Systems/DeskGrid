namespace DeskGrid.Folders;

/// <summary>A subfolder of the root folder, shown as one widget.</summary>
internal sealed record WidgetFolder(string Name, string FullPath, IReadOnlyList<FolderItem> Items);

/// <summary>
/// A visible file inside a widget folder (.lnk, .url, .exe or any other file). <paramref name="TargetWriteTimeUtc"/>
/// is the last-write time of the file a shortcut points to (its icon changes when the program is updated), or
/// <see langword="default"/> for anything else.
/// </summary>
internal sealed record FolderItem(string Name, string FullPath, DateTime LastWriteTimeUtc, DateTime TargetWriteTimeUtc = default);

internal sealed record WidgetRenamedEventArgs(string OldName, string NewName);

internal sealed record ItemRenamedEventArgs(string WidgetName, string OldName, string NewName);
