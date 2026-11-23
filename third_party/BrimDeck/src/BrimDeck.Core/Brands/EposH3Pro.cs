namespace BrimDeck.Core;

// EPOS H3PRO Hybrid headset, vendor 0x1395: on its USB dongle (0x0303) or with the headset plugged in by cable (0x0101). Both
// expose the same vendor collection (usage page 0xFFFF, usage 1, interface 3) with 107-byte reports both ways, report id 1. A
// report carries one frame of little-endian 16-bit fields after the id: EB FE, version 1, type, target, payload length, CRC,
// then the payload as 16-bit words (group, command, values). The type is 4 for a request and its reply; the target is 1 for
// the plugged-in device (the dongle, or the cabled headset) and 5 for the headset behind the dongle, a reply coming back with
// the target less one (0 and 4). The CRC is CRC-8 (polynomial 0x07, initial value 0xFF) over the payload, in a 16-bit field.
// The battery is group 0x12 command 3, answered with one value, the percent (seen in steps of 20). While the headset is off
// the dongle answers a request for it with its own target (1) instead of 4: the headset asleep. A reply with a bad CRC, or for
// another command, is none. Nothing is ever set.
// From HeadsetControl's epos_h3pro_hybrid.hpp (GPL; facts only: captured from EPOS Gaming Suite). Not measured by us.
[BrandFamilies]
public static class EposH3Pro
{
    public const int Vendor = 0x1395, Dongle = 0x0303, Cable = 0x0101;
    private const int Size = 107, HeaderSize = 13;
    private const ushort TargetDevice = 0x0001, TargetHeadset = 0x0005, TypeRequest = 0x0004, BatteryGroup = 0x12, BatteryCommand = 0x03;

    public static byte Crc8(ReadOnlySpan<byte> data)
    {
        byte crc = 0xFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 0x80) != 0 ? (byte)((crc << 1) ^ 0x07) : (byte)(crc << 1);
        }
        return crc;
    }

    // A request for group / command to the target, as the 107-byte output report.
    public static byte[] Request(ushort target, ushort group, ushort command)
    {
        byte[] payload = [(byte)group, (byte)(group >> 8), (byte)command, (byte)(command >> 8)];
        var report = new byte[Size];
        void Put(int at, ushort word) { report[at] = (byte)word; report[at + 1] = (byte)(word >> 8); }
        report[0] = 0x01; report[1] = 0xEB; report[2] = 0xFE;
        Put(3, 0x0001); Put(5, TypeRequest); Put(7, target); Put(9, (ushort)payload.Length); Put(11, Crc8(payload));
        payload.CopyTo(report, HeaderSize);
        return report;
    }

    // The battery answer to a request sent to the target; null for any other report. A dongle's own target in the answer to a
    // request for the headset says the headset is off.
    public static Func<byte[], BatteryRead?> Battery(ushort target) => r =>
    {
        if (r.Length < HeaderSize + 4 || r[0] != 0x01 || r[1] != 0xEB || r[2] != 0xFE) return null;
        int Word(int at) => r[at] | r[at + 1] << 8;
        int length = Word(9);
        if (length < 4 || length % 2 != 0 || HeaderSize + length > r.Length) return null;
        if (Word(11) != Crc8(r.AsSpan(HeaderSize, length))) return null;
        if (Word(5) != TypeRequest || Word(13) != BatteryGroup || Word(15) != BatteryCommand) return null;
        if (target == TargetHeadset && Word(7) == TargetDevice) return new BatteryRead(null, Asleep: true);
        if (Word(7) != target - 1 || length != 6) return null;
        int percent = Word(17);
        return percent <= 100 ? new BatteryRead(percent) : null;
    };

    private static bool Takes(HidShape s) => s.Vendor == Vendor && s.Product is Dongle or Cable && MouseShapes.Collection(s, 0xFFFF, 0x0001)
        && s.InputLength >= Size - 7 && s.OutputLength >= Size - 7;

    public static IEnumerable<BrandFamily> Families =>
    [
        new("EPOS H3PRO", Takes, s =>
        {
            ushort target = s.Product == Dongle ? TargetHeadset : TargetDevice;
            return [new BrandWay("epos.battery", WayTransport.Output, Request(target, BatteryGroup, BatteryCommand), Battery(target)) { TimeoutMs = 1000 }];
        })
        { Kind = _ => DeviceKind.Headphones, Prefix = "epos", DisplayName = _ => "EPOS H3PRO Hybrid" },
    ];
}
