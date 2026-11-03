using System.Buffers.Binary;
using System.IO;

namespace DeskGrid.Shell;

/// <summary>
/// The "Shell IDList Array" clipboard format (<c>CIDA</c>): the items of a drag as Shell item identifiers, which is
/// how things without a file path (the apps of the Start menu) are carried. Layout: a count, then the offset of the
/// parent folder's identifier list and of each item's, each list ending in a zero-length entry.
/// </summary>
internal static class ShellIdList
{
    /// <summary>The clipboard format name.</summary>
    public const string Format = "Shell IDList Array";

    /// <summary>
    /// Each item as an absolute identifier list (parent folder's followed by the item's), or an empty list if
    /// <paramref name="data"/> isn't a well-formed array.
    /// </summary>
    public static IReadOnlyList<byte[]> Parse(ReadOnlySpan<byte> data)
    {
        var count = data.Length >= sizeof(uint) ? BinaryPrimitives.ReadUInt32LittleEndian(data) : 0;
        if (count == 0 || (long)count + 2 > data.Length / sizeof(uint))
        {
            return [];
        }

        if (ListEnd(data, Offset(data, 0)) is not { } parentEnd)
        {
            return [];
        }

        var parent = data[Offset(data, 0)..(parentEnd - sizeof(ushort))]; // Without its terminator: the item's follows.
        var items = new List<byte[]>();
        for (var i = 1; i <= count; i++)
        {
            var start = Offset(data, i);
            if (ListEnd(data, start) is not { } end)
            {
                return [];
            }

            items.Add([.. parent, .. data[start..end]]);
        }

        return items;
    }

    // The offset stored for the parent (0) or item (1..) list; out of range values are caught by ListEnd.
    private static int Offset(ReadOnlySpan<byte> data, int index) =>
        (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data[((1 + index) * sizeof(uint))..]), int.MaxValue);

    // Where the identifier list starting at start ends (just after its zero-length terminator), or null if it runs
    // off the data.
    private static int? ListEnd(ReadOnlySpan<byte> data, int start)
    {
        var position = start;
        while (position >= 0 && position + sizeof(ushort) <= data.Length)
        {
            var size = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
            if (size == 0)
            {
                return position + sizeof(ushort);
            }

            if (size < sizeof(ushort))
            {
                return null;
            }

            position += size;
        }

        return null;
    }
}