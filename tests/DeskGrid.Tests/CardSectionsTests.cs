using DeskGrid.Island;
using Xunit;

namespace DeskGrid.Tests;

public class CardSectionsTests
{
    [Fact]
    public void Parse_NothingStored_ShowsEveryPartInTheDefaultOrder() =>
        Assert.Equal(CardSections.All, CardSections.Parse(null));

    [Fact]
    public void Parse_KeepsTheStoredOrder_DropsUnknownAndRepeatedIds() =>
        Assert.Equal(["shelf", "players"], CardSections.Parse("shelf, nope,players,SHELF"));

    [Fact]
    public void Parse_Empty_ShowsNothing() => Assert.Empty(CardSections.Parse(""));

    [Fact]
    public void Store_TheDefault_IsNull_AnythingElseIsListed()
    {
        Assert.Null(CardSections.Store(CardSections.All));
        Assert.Equal("shelf,usage", CardSections.Store(["shelf", "usage"]));
        Assert.Equal("", CardSections.Store([]));
    }

    [Fact]
    public void ForEditing_ListsShownPartsFirstThenTheHiddenOnes()
    {
        var rows = CardSections.ForEditing("shelf,players");
        Assert.Equal(CardSections.All.Count, rows.Count);
        Assert.Equal([("shelf", true), ("players", true)], rows.Take(2));
        Assert.All(rows.Skip(2), row => Assert.False(row.Shown));
    }

    [Fact]
    public void AFileDraggedFromTheShelf_IsToldApartFromOneBroughtIn()
    {
        // The card refuses its own shelf's files, so letting one go over the card does not take it off the shelf.
        var fromShelf = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { @"C:\a.txt" });
        fromShelf.SetData(IslandCard.ShelfFormat, true);
        Assert.True(IslandCard.IsFromShelf(fromShelf));
        Assert.False(IslandCard.IsFromShelf(new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { @"C:\a.txt" })));
    }
}
