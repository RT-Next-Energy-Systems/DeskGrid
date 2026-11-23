namespace BrimDeck.Core;

// MOJHON (墨將, formerly BIGBIGWON) GALE2 gamepad on its 2.4 GHz link, 0xE2B7:0x122C (it enumerates as an Xbox 360 controller,
// whose XInput level is always FULL for a receiver and carries nothing). Its one two-way vendor collection is usage page 0xFFAB,
// usage 1, 65-byte reports both ways with no report id (the leading zero is Windows' padding, not an id). A frame is
// A5 <length> <register> <data...> <sum>, the sum the low byte of every byte before it; a read of a register is
// A5 04 <register> <sum>, answered with the register echoed. Register 0x04 is the battery: A5 06 04 <percent> <charging> <sum>,
// charging 1 on the cable and 0 off it (0x0B, which stays 0x61, is not the level). The device does not send it unasked.
// From vigil-watt's docs/hardware/mojhon.md (MIT; measured on a GALE2 against the vendor's own assistant at 18 % and over the
// cable switching with the level fixed at 36 %, 35 of 35 captured frames checked). The 0x122D id the vendor's software also
// names is not taken: nothing says it answers the same. Not measured by us.
[BrandFamilies]
public static class Mojhon
{
    public const int Vendor = 0xE2B7, Gale2 = 0x122C;

    // A5 04 04 AD, after the zero the 65-byte output report starts with.
    public static byte[] Request() => BrandFamilies.Frame(65, 0x00, 0xA5, 0x04, 0x04, 0xAD);

    // The answer without its zero report id.
    public static BatteryRead? Battery(byte[] r)
    {
        if (r.Length < 6 || r[0] != 0xA5 || r[1] != 0x06 || r[2] != 0x04) return null;
        int sum = 0; for (int i = 0; i < 5; i++) sum += r[i];
        return (byte)sum == r[5] && r[3] is > 0 and <= 100 ? new(r[3], r[4] == 0x01) : null;
    }

    private static bool Takes(HidShape s) => s.Vendor == Vendor && s.Product == Gale2 && MouseShapes.Collection(s, 0xFFAB, 0x0001)
        && s.InputLength >= 65 && s.OutputLength >= 65;

    public static IEnumerable<BrandFamily> Families =>
    [
        new("MOJHON", Takes, _ => [new BrandWay("mojhon.battery", WayTransport.Output, Request(), Battery) { TimeoutMs = 600, StripZero = true }])
        { Kind = _ => DeviceKind.Controller, Prefix = "mojhon", DisplayName = _ => "MOJHON GALE2" },
    ];
}
