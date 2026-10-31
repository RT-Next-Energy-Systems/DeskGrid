using System.Windows;

namespace DeskGrid.Panel;

/// <summary>
/// What a panel puts on an item it drags, so panels and widgets can tell it from files dragged in from elsewhere.
/// </summary>
internal static class PanelDragData
{
    /// <summary>The dragged item's name.</summary>
    public const string ItemFormat = "DeskGrid.PanelItem";

    /// <summary>The name of the folder whose panel the item is dragged out of.</summary>
    public const string FolderFormat = "DeskGrid.PanelFolder";

    /// <summary>Whether <paramref name="data"/> is an item dragged out of the panel of <paramref name="folder"/>.</summary>
    public static bool IsFrom(IDataObject data, string folder) =>
        data.GetDataPresent(ItemFormat) && data.GetData(FolderFormat) is string owner
        && string.Equals(owner, folder, StringComparison.OrdinalIgnoreCase);
}
