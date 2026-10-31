namespace DeskGrid.Tests;

/// <summary>
/// Tests that show windows or menus, or ask the Shell for icons, run one at a time: only one menu can be up at once
/// (a second one closes the first), and a window shown by one test takes activation from another's.
/// </summary>
[CollectionDefinition(nameof(ShellCollection), DisableParallelization = true)]
public sealed class ShellCollection;
