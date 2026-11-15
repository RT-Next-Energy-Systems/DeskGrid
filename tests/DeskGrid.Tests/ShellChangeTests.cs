using System.IO;
using DeskGrid.Shell;

namespace DeskGrid.Tests;

public sealed class ShellChangeTests
{
    [Fact]
    public void FolderChanged_AnExistingFolder_DoesNotThrow()
    {
        using var folder = new TempDirectory();

        ShellChange.FolderChanged(folder.Path);
    }

    [Fact]
    public void FolderChanged_AFolderThatIsGone_DoesNotThrow() =>
        ShellChange.FolderChanged(Path.Combine(Path.GetTempPath(), "DeskGrid-gone-" + Guid.NewGuid().ToString("N")));
}
