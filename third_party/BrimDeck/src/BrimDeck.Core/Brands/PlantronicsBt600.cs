namespace BrimDeck.Core;

// Plantronics / Poly headsets behind the BT600 USB adapter (the Voyager 8200 UC), vendor 0x047F, adapter 0x02EE (the id
// HeadsetControl's class covers) and 0x02F7 (the adapter of a HaloBattery report, same collections and product string). The
// BT600 is a Bluetooth relay: the headset speaks to it over Bluetooth and Poly's software reads the headset through it, with a
// get / set protocol tunnelled in report 0x07 on the vendor collection (usage page 0xFFA2, usage 3, interface 3), 64 bytes
// both ways: 07 01 01 10 <length> <address> 00 00 <operation> <group> <item> <value...>. The address is 0x00 for the adapter
// and 0x20 for the headset; replies carry 0x00 and 0x02. The length byte is 6 for a GET and 7 for the rest. The adapter has
// to be attached first, as Windows' own software does: a two-byte output report 06 01, a HELLO (operation 1, group 1, item 2)
// to the adapter, then one to the headset; it is idempotent, so it is sent before every read. The battery is GET group 0x0A
// item 0x1A, answered <level> <steps> <charging> <minutes high> <minutes low> <marker> from byte 11, the percent Poly's own
// level / (steps - 1) (steps counts the zero, so 0x0B means ten steps). A headset that is off or out of range does not answer
// the GET, so it is no reading. Only the battery is read: nothing is ever set.
// From HaloBattery's plantronics.py (MIT; confirmed on a Voyager 8200 UC) and HeadsetControl's plantronics_bt600.hpp (GPL;
// facts only). Not measured by us.
[BrandFamilies]
public static class PlantronicsBt600
{
    public const int Vendor = 0x047F;
    public static readonly int[] Adapters = [0x02EE, 0x02F7];

    private const byte ReportId = 0x07, Adapter = 0x00, Headset = 0x20, HeadsetReply = 0x02;
    private const byte Hello = 0x01, Get = 0x02, GetReply = 0x03, Length = 64;

    // A request: report 7, the fixed header, the length byte for the operation, the address, then operation, group, item.
    public static byte[] Frame(byte operation, byte group, byte item, byte address)
        => BrandFamilies.Frame(Length, ReportId, 0x01, 0x01, 0x10, operation == Get ? (byte)0x06 : (byte)0x07, address, 0x00, 0x00, operation, group, item);

    public static byte[] Request() => Frame(Get, 0x0A, 0x1A, Headset);

    // What attaches the session, in order: the nudge, then a HELLO to the adapter and one to the headset.
    public static IReadOnlyList<byte[]> Attach() =>
        [BrandFamilies.Frame(Length, 0x06, 0x01), Frame(Hello, 0x01, 0x02, Adapter), Frame(Hello, 0x01, 0x02, Headset)];

    // The battery answer; null for any other report (the HELLO's answer, another group's, an event).
    public static BatteryRead? Battery(byte[] r)
    {
        if (r.Length < 14 || r[0] != ReportId || r[1] != 0x01 || r[2] != 0x01 || r[3] != 0x10 || r[5] != HeadsetReply
            || r[8] != GetReply || r[9] != 0x0A || r[10] != 0x1A) return null;
        int level = r[11], steps = r[12];
        if (steps < 2 || level >= steps) return null;
        return new(Math.Min(100, level * 100 / (steps - 1)), r[13] != 0);
    }

    private static bool Takes(HidShape s) => s.Vendor == Vendor && Adapters.Contains(s.Product) && HeadsetPages.Is(s, 0xFFA2) && s.Usage == 0x0003
        && s.InputLength >= 16 && s.OutputLength >= 16;

    public static IEnumerable<BrandFamily> Families =>
    [
        new("Plantronics BT600", Takes, _ =>
            [new BrandWay("plantronics.battery", WayTransport.Output, Request(), Battery) { Before = Attach(), GapMs = 120, TimeoutMs = 1500 }])
        {
            Kind = _ => DeviceKind.Headphones, Prefix = "plantronics",
            DisplayName = shape => shape.Product == 0x02EE ? "Poly Voyager 8200 UC" : null,
        },
    ];
}
