using System.IO;

namespace DeskGrid.Island;

/// <summary>What a click on an island line may do. A line comes from a script, so only a few kinds of target are allowed.</summary>
internal static class IslandAction
{
    public const string FolderPrefix = "folder:";

    /// <summary>A DeskGrid folder (<c>folder:Name</c>), an http(s) address, or a file or folder that exists.</summary>
    public static bool IsAllowed(string target) =>
        target.StartsWith(FolderPrefix, StringComparison.Ordinal) && target.Length > FolderPrefix.Length
        || Uri.TryCreate(target, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        || Path.IsPathFullyQualified(target) && (File.Exists(target) || Directory.Exists(target));

    /// <summary>The folder name in <c>folder:Name</c>, or <see langword="null"/> for any other target.</summary>
    public static string? FolderOf(string target) =>
        target.StartsWith(FolderPrefix, StringComparison.Ordinal) ? target[FolderPrefix.Length..] : null;
}
