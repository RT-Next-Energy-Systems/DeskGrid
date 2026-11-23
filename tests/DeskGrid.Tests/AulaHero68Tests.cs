using BrimDeck.Core;

namespace DeskGrid.Tests;

// AULA HERO 68 Air: the frames are the ones vigil-watt (MIT) captured from a real keyboard on its 2.4 GHz receiver (10 %,
// discharging and on the cable); the exchange is run against a receiver simulated from them. Not run on the hardware here.
public sealed class AulaHero68Tests
{
    private static byte[] UuidReply() => Convert.FromHexString("82010001000612000000000A" + new string('0', 100) + "50");
    private static byte[] BatteryReply() => Convert.FromHexString("8700000100020A00" + new string('0', 108) + "62");

    // The packet's last byte set so that its sum plus the report id 9 is 0xFF.
    private static void FixSum(byte[] packet)
    {
        packet[^1] = 0;
        packet[^1] = (byte)(255 - ((packet.Sum(b => b) + AulaHero68.ReportId) & 255));
    }

    private static byte[][] Fragments(byte[] packet, int firstSync = 6)
        => Enumerable.Range(0, 5).Select(i => AulaHero68.Fragment(packet.AsSpan(i * 14, Math.Min(14, packet.Length - i * 14)), 5, i + 1, (i + firstSync) & 7)).ToArray();

    // The receiver as the captures show it: every fragment of ours is acknowledged, and a whole query is answered with the five
    // fragments of its reply (the sequence number going on from 6). Our acknowledgements of those are taken and not answered.
    private sealed class Receiver(byte[] uuid, byte[] battery, bool deaf = false, bool deafAfterIdentity = false) : IHidExchange
    {
        private readonly Queue<byte[]> _incoming = new();
        private readonly List<byte> _query = [];
        private int _sync = 5;
        public List<byte> Commands { get; } = [];
        public List<byte[]> Written { get; } = [];

        public void Write(byte[] report)
        {
            Written.Add(report);
            if (deaf || deafAfterIdentity && Commands.Count > 0 || !AulaHero68.TryParseFragment(report, out int total, out int current, out _, out int length) || length == 0) return;
            _incoming.Enqueue(AulaHero68.Ack(report));
            if (current == 1) _query.Clear();
            _query.AddRange(report.AsSpan(5, length).ToArray());
            if (current != total) return;
            Commands.Add(_query[1]);
            var reply = _query[1] == 0x82 ? uuid : battery;
            foreach (var fragment in Fragments(reply, _sync + 1)) _incoming.Enqueue(fragment);
            _sync = (_sync + 5) & 7;
        }

        public byte[]? Read(int timeoutMs)
        {
            if (_incoming.Count > 0) return _incoming.Dequeue();
            Thread.Sleep(5);
            return null;
        }
    }

    [Fact]
    public void Queries_MatchTheOfficialBytes_AndTheCapturedFirstFragment()
    {
        var uuid = AulaHero68.UuidQuery();
        Assert.Equal(64, uuid.Length);
        Assert.Equal("09820100010006", Convert.ToHexString(uuid[..7]));
        Assert.Equal(0x6C, uuid[^1]);
        Assert.Equal("096605018E09820100010006000000000000008D", Convert.ToHexString(AulaHero68.Fragment(uuid.AsSpan(0, 14), 5, 1, 1)));
        var battery = AulaHero68.BatteryQuery();
        Assert.Equal("09870000010000", Convert.ToHexString(battery[..7]));
        Assert.Equal(0x6E, battery[^1]);
    }

    [Fact]
    public void Ack_IsTheCapturedOne_AndMatchesOnlyItsOwnFragment()
    {
        var sent = Convert.FromHexString("096605018E09820100010006000000000000008D");
        var ack = Convert.FromHexString("0966050180EC0000000000000000000000000000");
        Assert.Equal(ack, AulaHero68.Ack(sent));
        Assert.True(AulaHero68.IsAckOf(ack, sent));
        Assert.False(AulaHero68.IsAckOf(ack, AulaHero68.Fragment(new byte[14], 5, 1, 2))); // another sequence number
        ack[5] ^= 1;
        Assert.False(AulaHero68.IsAckOf(ack, sent));                                       // a sum that does not hold
    }

    [Theory]
    [InlineData(0x0A, true)]  // the HERO 68 Air
    [InlineData(0x10, false)] // another keyboard of the shared receiver
    [InlineData(0x15, false)]
    [InlineData(0x04, false)]
    public void TheSharedReceiver_IsReadOnlyForTheHero68AirUuid(int model, bool expected)
    {
        var packet = UuidReply();
        packet[11] = (byte)model;
        FixSum(packet);
        Assert.Equal(expected, AulaHero68.IsHero68Air(packet));
    }

    [Fact]
    public void Battery_IsTenPercentDischarging_ThenTenPercentCharging_AsCaptured()
    {
        var discharging = AulaHero68.Battery(BatteryReply());
        Assert.Equal(10, discharging?.Percent);
        Assert.False(discharging?.Charging);
        var charging = AulaHero68.Battery(Convert.FromHexString("8700000100020A10" + new string('0', 108) + "52"));
        Assert.Equal(10, charging?.Percent);
        Assert.True(charging?.Charging);
    }

    [Theory]
    [InlineData(0, 0x00, false)]
    [InlineData(100, 0x00, false)]
    [InlineData(37, 0x10, true)]
    [InlineData(100, 0x11, true)] // charging wins over full
    public void Battery_TakesTheHighNibbleAsCharging(int percent, int flag, bool charging)
    {
        var packet = BatteryReply();
        packet[6] = (byte)percent; packet[7] = (byte)flag;
        FixSum(packet);
        var read = AulaHero68.Battery(packet);
        Assert.Equal(percent, read?.Percent);
        Assert.Equal(charging, read?.Charging);
    }

    [Fact]
    public void Battery_RefusesCorruption_WrongHeaders_ANotice_AndAPercentOver100()
    {
        var packet = BatteryReply();
        for (int i = 0; i < packet.Length; i++)
        {
            var corrupt = packet.ToArray();
            corrupt[i] ^= 1;
            Assert.Null(AulaHero68.Battery(corrupt));
        }
        for (int index = 0; index <= 5; index++)
        {
            var wrong = packet.ToArray();
            wrong[index] ^= 1;
            FixSum(wrong);
            Assert.Null(AulaHero68.Battery(wrong));
        }
        packet[6] = 101;
        FixSum(packet);
        Assert.Null(AulaHero68.Battery(packet));
        Assert.Null(AulaHero68.Battery(UuidReply()));
        Assert.Null(AulaHero68.Battery(Convert.FromHexString("FE050A" + new string('0', 118) + "E9")));
    }

    [Fact]
    public void Assembler_PutsARealReplyTogether_AcrossTheSequenceWrap_AndIgnoresRetransmissions()
    {
        var assembler = new AulaHero68.Assembler();
        var frames = Fragments(UuidReply());
        for (int i = 0; i < 4; i++)
        {
            Assert.Null(assembler.Accept(frames[i]));
            Assert.Null(assembler.Accept(frames[i])); // sent twice
        }
        Assert.Equal(UuidReply(), assembler.Accept(frames[4]));
        Assert.Null(assembler.Accept(frames[4]));
        foreach (var frame in Fragments(BatteryReply()).Take(4)) Assert.Null(assembler.Accept(frame));
        Assert.Equal(BatteryReply(), assembler.Accept(Fragments(BatteryReply())[4]));
    }

    [Fact]
    public void Assembler_CannotBeSplicedByAFragmentOutOfOrder_OrWithAnotherSequenceNumber()
    {
        var frames = Fragments(BatteryReply());
        var assembler = new AulaHero68.Assembler();
        Assert.Null(assembler.Accept(frames[0]));
        foreach (var frame in frames.Skip(2)) Assert.Null(assembler.Accept(frame)); // the second is missing
        Assert.Null(assembler.Accept(AulaHero68.Fragment(new byte[14], 5, 2, 3)));    // a sequence number that is not the next
        foreach (var frame in frames.Take(4)) Assert.Null(assembler.Accept(frame));
        Assert.Equal(BatteryReply(), assembler.Accept(frames[4]));                    // a new first fragment starts afresh
    }

    [Fact]
    public void Read_AsksWhoItIs_ThenForTheBattery_AndAcknowledgesEveryFragment()
    {
        var receiver = new Receiver(UuidReply(), BatteryReply());
        var read = AulaHero68.Read(receiver);
        Assert.Equal(10, read?.Percent);
        Assert.False(read?.Charging);
        Assert.Equal(new byte[] { 0x82, 0x87 }, receiver.Commands);
        // Ten fragments of ours, ten acknowledgements of the receiver's: the sequence number goes on from 1 to 10 modulo 8.
        var fragments = receiver.Written.Where(report => (report[4] & 0x7F) != 0).ToList();
        Assert.Equal(10, fragments.Count);
        for (int i = 0; i < fragments.Count; i++)
        {
            Assert.True(AulaHero68.TryParseFragment(fragments[i], out _, out int current, out int sync, out _));
            Assert.Equal(i % 5 + 1, current);
            Assert.Equal((i + 1) & 7, sync);
        }
        Assert.Equal(10, receiver.Written.Count(report => (report[4] & 0x7F) == 0));
    }

    [Fact]
    public void Read_ReportsCharging_WhenTheKeyboardIsOnItsCable()
    {
        var charging = Convert.FromHexString("8700000100020A10" + new string('0', 108) + "52");
        Assert.True(AulaHero68.Read(new Receiver(UuidReply(), charging))?.Charging);
    }

    [Fact]
    public void Read_SendsNoBatteryQuery_ToAnotherKeyboardOfTheSharedReceiver()
    {
        var other = UuidReply();
        other[11] = 0x10;
        FixSum(other);
        var receiver = new Receiver(other, BatteryReply());
        Assert.Null(AulaHero68.Read(receiver));
        Assert.Equal(new byte[] { 0x82 }, receiver.Commands);
    }

    // A keyboard that sleeps sends nothing. That is the answer "asleep", which BrimDeck remembers and reads again at every pass; a
    // read that gave nothing instead would be tried three times and then let go, leaving a keyboard that was asleep when the
    // program started without a level until it was plugged in again.
    [Fact]
    public void Read_SaysAsleep_FromAReceiverThatDoesNotAnswer_AndStopsAfterTheFirstFragment()
    {
        var receiver = new Receiver(UuidReply(), BatteryReply(), deaf: true);
        var read = AulaHero68.Read(receiver);
        Assert.True(read?.Asleep);
        Assert.Null(read?.Percent);
        Assert.Single(receiver.Written); // nothing is repeated, and no second fragment goes out unacknowledged
    }

    [Fact]
    public void Read_SaysAsleep_WhenTheKeyboardFallsAsleepBetweenTheTwoQueries()
    {
        var receiver = new Receiver(UuidReply(), BatteryReply(), deafAfterIdentity: true);
        var read = AulaHero68.Read(receiver);
        Assert.True(read?.Asleep);
        Assert.Equal(new byte[] { 0x82 }, receiver.Commands);
    }

    [Fact]
    public void Read_IsAFailureNotSleep_WhenTheReceiverAnswersButTheReplyIsNotUsable()
    {
        var corrupt = BatteryReply();
        corrupt[^1] ^= 1; // a sum that does not hold: the receiver answered, so the keyboard is awake
        Assert.Null(AulaHero68.Read(new Receiver(UuidReply(), corrupt)));
    }

    [Fact]
    public void TheFamily_TakesOnlyTheCollectionOfTheHero68AirReceiver_AndRunsItsOwnExchange()
    {
        HidShape Shape(int product, int page, int usage, int length, byte id)
            => new(0x372E, product, page, usage, 3, length, length, 0, new HashSet<byte> { id }, new HashSet<byte>(), "");
        var found = BrandFamilies.Find(Shape(0x106C, 0xFF60, 0x0061, 20, 0x09));
        Assert.Equal("AULA HERO 68", found?.Family.Brand);
        Assert.NotNull(Assert.Single(found!.Value.Ways).Exchange);
        Assert.Equal("AULA HERO 68", BrandFamilies.Find(Shape(0x106C, 0x60FF, 0x6100, 20, 0x09))?.Family.Brand); // the bytes swapped, as Windows reports some
        Assert.Null(BrandFamilies.Find(Shape(0x106C, 0xFF02, 0x0002, 20, 0x09)));   // the other channel, never written
        Assert.Null(BrandFamilies.Find(Shape(0x103E, 0xFF60, 0x0061, 20, 0x09)));   // another product
        Assert.Null(BrandFamilies.Find(Shape(0x106C, 0xFF60, 0x0061, 64, 0x09)));   // another report length
        Assert.Empty(BrandFamilies.Find(Shape(0x106C, 0xFF60, 0x0061, 20, 0x08))!.Value.Ways); // the report id is not declared: no read
    }
}
