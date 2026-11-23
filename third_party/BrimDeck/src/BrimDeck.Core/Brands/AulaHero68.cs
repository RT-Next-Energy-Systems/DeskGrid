namespace BrimDeck.Core;

// AULA (狼蛛) HERO 68 Air on its 8K receiver 372E:106C, which HERO 68 XS, HERO 75 HE and HERO68 MINI Air share: only the one
// that answers the receiver's identity query with the HERO 68 Air's UUID is read. The collection is usage page 0xFF60 usage
// 0x61, 20-byte reports both ways with report id 9 (the same receiver's 0xFF02 collection is another channel, never written).
// The receiver speaks AULA's "Bytech" protocol, cut in fragments by the 8K wireless layer:
// - A packet is 64 bytes sent (report id 9 first) and 63 received, in five fragments of 14 payload bytes (the last 8 sent, 7
//   received). A fragment is the report 09 66 <total | s2<<7> <current | s1<<7> <length | s0<<7> <payload> <sum>, the sum the
//   low byte of the bytes from 66 to the payload's end; s2 s1 s0 are the three bits of a sequence number that goes up by one
//   per fragment, modulo 8, in each direction.
// - Every fragment is acknowledged before the next: an ACK keeps 66, the total, the current and the three sequence bits, has
//   length 0 and its sum recomputed. A fragment of the reply is acknowledged the same way before it is taken in. A "FE 05"
//   notice between whole packets is acknowledged and passed over. A fragment is taken only in order and with the next
//   sequence number, so a retransmission or a notice cannot be spliced into a reply.
// - The two queries, each a 64-byte packet 09 <command> <argument> 00 01 00 <length> ... <255 - sum>: the identity 82 01, length
//   6, answered 82 01 00 01 00 06 <UUID, six bytes, 12 00 00 00 00 0A for the HERO 68 Air>; and the battery 87 00, length 0,
//   answered 87 00 00 01 00 02 <percent> <flags>, the flags' high nibble set while charging, their low nibble while full. A
//   reply's sum plus the report id 9 is 0xFF.
// Only these two queries are sent, at most 15 ACKs a query, nothing is repeated on a timeout. A keyboard that sleeps does not
// answer; a key press wakes it and the same query is answered again.
// From vigil-watt's docs/hardware/aula.md and its AulaHero68 codec, assembler and transport (MIT; measured on a HERO 68 Air
// against AULA's own A HUB at 10 % on the 2.4 GHz receiver, discharging and on the cable), which in turn follows A HUB's
// public driver behaviour. Not measured by us.
[BrandFamilies]
public static class AulaHero68
{
    public const int Vendor = 0x372E, Receiver = 0x106C;
    public const byte ReportId = 0x09;
    public const int ReportLength = 20, PacketLength = 63, FragmentPayload = 14, MaxFragments = 5;

    private static byte Sum(ReadOnlySpan<byte> bytes)
    {
        int sum = 0;
        foreach (byte value in bytes) sum += value;
        return (byte)sum;
    }

    public static byte[] UuidQuery() => Query(0x82, 0x01, 6);
    public static byte[] BatteryQuery() => Query(0x87, 0x00, 0);

    private static byte[] Query(byte command, byte argument, byte length)
    {
        var packet = new byte[PacketLength + 1];
        packet[0] = ReportId; packet[1] = command; packet[2] = argument; packet[4] = 1; packet[6] = length;
        packet[^1] = (byte)(255 - Sum(packet));
        return packet;
    }

    // One fragment: current of total, the payload, and the sequence number's three bits (sync 0 to 7).
    public static byte[] Fragment(ReadOnlySpan<byte> payload, int total, int current, int sync)
    {
        if (payload.Length is < 1 or > FragmentPayload || total is < 1 or > MaxFragments || current < 1 || current > total || sync is < 0 or > 7)
            throw new ArgumentOutOfRangeException(nameof(payload));
        var report = new byte[ReportLength];
        report[0] = ReportId; report[1] = 0x66;
        report[2] = (byte)(total | (sync & 4) << 5);
        report[3] = (byte)(current | (sync & 2) << 6);
        report[4] = (byte)(payload.Length | (sync & 1) << 7);
        payload.CopyTo(report.AsSpan(5));
        report[5 + payload.Length] = Sum(report.AsSpan(1, 4 + payload.Length));
        return report;
    }

    public static bool TryParseFragment(ReadOnlySpan<byte> report, out int total, out int current, out int sync, out int length)
    {
        total = current = sync = length = 0;
        if (report.Length != ReportLength || report[0] != ReportId || report[1] != 0x66) return false;
        total = report[2] & 0x7F; current = report[3] & 0x7F; length = report[4] & 0x7F;
        sync = (report[2] & 0x80) >> 5 | (report[3] & 0x80) >> 6 | (report[4] & 0x80) >> 7;
        return total is >= 1 and <= MaxFragments && current >= 1 && current <= total && length <= FragmentPayload
            && report[5 + length] == Sum(report.Slice(1, 4 + length));
    }

    // The acknowledgement of a fragment: its header and sequence bits, length 0, the sum of the four bytes.
    public static byte[] Ack(ReadOnlySpan<byte> report)
    {
        if (!TryParseFragment(report, out _, out _, out _, out int length) || length == 0) throw new ArgumentException("Not a data fragment.", nameof(report));
        var ack = new byte[ReportLength];
        report[..5].CopyTo(ack);
        ack[4] &= 0x80;
        ack[5] = Sum(ack.AsSpan(1, 4));
        return ack;
    }

    public static bool IsAckOf(ReadOnlySpan<byte> report, ReadOnlySpan<byte> sent)
        => TryParseFragment(report, out _, out _, out _, out int length) && length == 0 && sent.Length == ReportLength
            && report[2] == sent[2] && report[3] == sent[3] && report[4] == (sent[4] & 0x80);

    // The 63-byte packet a reply's fragments make: its command, argument and length, and its sum.
    public static bool IsReply(ReadOnlySpan<byte> packet, byte command, byte argument, byte length)
        => packet.Length == PacketLength && packet[0] == command && packet[1] == argument && packet[2] == 0 && packet[3] == 1
            && packet[4] == 0 && packet[5] == length && (byte)(Sum(packet) + ReportId) == 0xFF;

    // The receiver's identity answer for a HERO 68 Air (the shared receiver's other keyboards answer another UUID).
    public static bool IsHero68Air(ReadOnlySpan<byte> packet)
        => IsReply(packet, 0x82, 0x01, 6) && packet.Slice(6, 6).SequenceEqual(new byte[] { 0x12, 0, 0, 0, 0, 0x0A });

    public static BatteryRead? Battery(ReadOnlySpan<byte> packet)
        => IsReply(packet, 0x87, 0x00, 2) && packet[6] <= 100 ? new BatteryRead(packet[6], (packet[7] & 0xF0) != 0) : null;

    // Puts a reply's fragments back together: in order, each with the sequence number after the last's, a retransmission
    // passed over, anything else dropping what was gathered.
    public sealed class Assembler
    {
        private readonly byte[] _packet = new byte[PacketLength];
        private int _total, _current, _sync, _length;

        public void Reset() => _total = _current = _sync = _length = 0;

        public byte[]? Accept(ReadOnlySpan<byte> report)
        {
            if (!TryParseFragment(report, out int total, out int current, out int sync, out int length) || length == 0) return null;
            if (current == _current && sync == _sync && total == _total) return null;
            if (current == 1) { Reset(); _total = total; }
            if (total != _total || current != _current + 1 || _length + length > _packet.Length || (current > 1 && sync != ((_sync + 1) & 7)))
            {
                Reset();
                return null;
            }
            report.Slice(5, length).CopyTo(_packet.AsSpan(_length));
            _length += length; _current = current; _sync = sync;
            return current == total && _length == _packet.Length ? _packet.ToArray() : null;
        }
    }

    private const int QueryMs = 3000, AckMs = 500, MaxAcks = 15;

    // One query over the collection: the fragments one by one, each acknowledged by the receiver, then the reply's fragments,
    // each acknowledged by us. The sequence number continues from one query to the next. Null when it was not answered.
    // heard: whether the receiver sent anything at all, an acknowledgement or a fragment; a keyboard that sleeps sends nothing.
    public static byte[]? Ask(IHidExchange link, byte[] query, byte command, byte argument, byte length, ref int sync, out bool heard)
    {
        heard = false;
        var assembler = new Assembler();
        int acks = 0;
        byte[]? reply = null;
        long start = Environment.TickCount64;

        bool Take(byte[] input)
        {
            if (!TryParseFragment(input, out _, out _, out _, out int n) || n == 0) return true;
            if (++acks > MaxAcks) return false;
            link.Write(Ack(input));
            if (assembler.Accept(input) is { } packet && IsReply(packet, command, argument, length)) reply = packet;
            return true;
        }

        for (int index = 0; index < MaxFragments; index++)
        {
            if (Environment.TickCount64 - start > QueryMs) return null;
            int offset = index * FragmentPayload;
            sync = (sync + 1) & 7;
            var sent = Fragment(query.AsSpan(offset, Math.Min(FragmentPayload, query.Length - offset)), MaxFragments, index + 1, sync);
            link.Write(sent);
            bool acknowledged = false;
            long waited = Environment.TickCount64;
            while (Environment.TickCount64 - waited < AckMs)
            {
                if (link.Read(100) is not { } input) continue;
                heard = true;
                if (IsAckOf(input, sent)) { acknowledged = true; break; }
                if (!Take(input)) return null;
            }
            if (!acknowledged) return null;
        }
        while (reply is null && Environment.TickCount64 - start < QueryMs)
            if (link.Read(100) is { } input)
            {
                heard = true;
                if (!Take(input)) return null;
            }
        return reply;
    }

    // The whole read: the identity query (the HERO 68 Air is the only keyboard of the shared receiver read), then the battery.
    // A receiver that answers nothing to a query has its keyboard asleep (or away, or on another connection): that is the
    // answer "asleep", not a failure. It matters: a device that has never answered is tried three times and then let go until it
    // is plugged in again, while one that answered "asleep" is remembered, kept on the list with its last level and read again at
    // every pass, which is what a keyboard that sleeps when the program starts needs. (Another keyboard of the shared receiver,
    // asleep when first seen, is listed the same way until it answers: its name is only known from the identity answer.)
    public static BatteryRead? Read(IHidExchange link)
    {
        int sync = 0;
        var identity = Ask(link, UuidQuery(), 0x82, 0x01, 6, ref sync, out bool heard);
        if (identity is null) return heard ? null : new BatteryRead(null, Asleep: true);
        if (!IsHero68Air(identity)) return null;
        var packet = Ask(link, BatteryQuery(), 0x87, 0x00, 2, ref sync, out heard);
        return packet is { } ? Battery(packet) : heard ? null : new BatteryRead(null, Asleep: true);
    }

    public static bool Takes(HidShape s) => s.Vendor == Vendor && s.Product == Receiver && MouseShapes.Collection(s, 0xFF60, 0x0061)
        && s.InputLength == ReportLength && s.OutputLength == ReportLength;

    public static IEnumerable<BrandFamily> Families =>
    [
        new("AULA HERO 68", Takes, _ => [new BrandWay("aula.hero68", WayTransport.Output, [ReportId], _ => null) { Exchange = Read }])
        { Kind = _ => DeviceKind.Keyboard, Prefix = "aula-hero68", DisplayName = _ => "AULA HERO 68 Air" },
    ];
}
