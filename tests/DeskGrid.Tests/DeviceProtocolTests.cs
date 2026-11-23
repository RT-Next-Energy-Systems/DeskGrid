using BrimDeck.Core;

namespace DeskGrid.Tests;

// The battery protocols of the headsets and the gamepad that BrimDeck's brand families learned later on (2026-11-23): every frame here is one
// the sources print (HaloBattery, vigil-watt, HeadsetControl), checked byte for byte; none of it was run against the hardware.
public sealed class DeviceProtocolTests
{
    private static HidShape Shape(int vendor, int product, int page, int usage, int input, int output, params byte[] outputIds)
        => new(vendor, product, page, usage, 3, input, output, 0, new HashSet<byte>(outputIds), new HashSet<byte>(), "");

    private static string? Brand(HidShape shape) => BrandFamilies.Find(shape)?.Family.Brand;

    // ----- Plantronics / Poly BT600 -----

    private static byte[] BtReply(byte level, byte steps, byte charging)
    {
        var reply = new byte[64];
        byte[] head = [0x07, 0x01, 0x01, 0x10, 0x0F, 0x02, 0x00, 0x00, 0x03, 0x0A, 0x1A, level, steps, charging];
        head.CopyTo(reply, 0);
        return reply;
    }

    [Fact]
    public void Plantronics_BatteryRequest_IsTheGetOfGroup0AItem1AToTheHeadset()
    {
        var request = PlantronicsBt600.Request();
        Assert.Equal(64, request.Length);
        Assert.Equal(new byte[] { 0x07, 0x01, 0x01, 0x10, 0x06, 0x20, 0x00, 0x00, 0x02, 0x0A, 0x1A, 0x00 }, request[..12]);
    }

    [Fact]
    public void Plantronics_Attach_NudgesThenGreetsTheAdapterAndTheHeadset()
    {
        var attach = PlantronicsBt600.Attach();
        Assert.Equal(3, attach.Count);
        Assert.Equal(new byte[] { 0x06, 0x01, 0x00 }, attach[0][..3]);
        // A HELLO is length 7 (a GET is 6), operation 1, group 1, item 2; the adapter is address 0, the headset 0x20.
        Assert.Equal(new byte[] { 0x07, 0x01, 0x01, 0x10, 0x07, 0x00, 0x00, 0x00, 0x01, 0x01, 0x02 }, attach[1][..11]);
        Assert.Equal(new byte[] { 0x07, 0x01, 0x01, 0x10, 0x07, 0x20, 0x00, 0x00, 0x01, 0x01, 0x02 }, attach[2][..11]);
    }

    [Theory]
    [InlineData(5, 11, 0, 50)]   // five of ten steps
    [InlineData(10, 11, 1, 100)] // full, on the charger
    [InlineData(0, 11, 0, 0)]
    [InlineData(3, 5, 0, 75)]    // four steps
    public void Plantronics_Battery_IsTheLevelOverTheStepsLessOne(int level, int steps, int charging, int percent)
    {
        var read = PlantronicsBt600.Battery(BtReply((byte)level, (byte)steps, (byte)charging));
        Assert.Equal(percent, read?.Percent);
        Assert.Equal(charging != 0, read?.Charging);
    }

    [Fact]
    public void Plantronics_Battery_IgnoresOtherReports()
    {
        var ack = BtReply(5, 11, 0); ack[8] = 0x08;            // a HELLO's answer
        var other = BtReply(5, 11, 0); other[10] = 0x1B;       // another item
        var adapter = BtReply(5, 11, 0); adapter[5] = 0x00;    // the adapter's, not the headset's
        Assert.Null(PlantronicsBt600.Battery(ack));
        Assert.Null(PlantronicsBt600.Battery(other));
        Assert.Null(PlantronicsBt600.Battery(adapter));
        Assert.Null(PlantronicsBt600.Battery(BtReply(11, 11, 0))); // a level beyond the scale
        Assert.Null(PlantronicsBt600.Battery(BtReply(0, 1, 0)));   // no denominator
    }

    [Fact]
    public void Plantronics_TakesTheVendorCollectionOfItsAdapters_AndNothingElse()
    {
        Assert.Equal("Plantronics BT600", Brand(Shape(0x047F, 0x02EE, 0xFFA2, 0x0003, 64, 64, 0x07)));
        Assert.Equal("Plantronics BT600", Brand(Shape(0x047F, 0x02F7, 0xA2FF, 0x0003, 64, 64, 0x07)));
        Assert.Null(Brand(Shape(0x047F, 0x02EE, 0xFFA2, 0x0001, 64, 64, 0x07)));
        Assert.Null(Brand(Shape(0x047F, 0x0001, 0xFFA2, 0x0003, 64, 64, 0x07)));
    }

    // ----- MOJHON GALE2 -----

    [Fact]
    public void Mojhon_Request_IsTheRegister04ReadInAnUnnumberedReport()
    {
        var request = Mojhon.Request();
        Assert.Equal(65, request.Length);
        Assert.Equal(new byte[] { 0x00, 0xA5, 0x04, 0x04, 0xAD }, request[..5]);
    }

    // Frames from vigil-watt's capture of a real GALE2.
    [Theory]
    [InlineData("a5 06 04 12 00 c1", 18, false)]
    [InlineData("a5 06 04 24 00 d3", 36, false)]
    [InlineData("a5 06 04 24 01 d4", 36, true)]
    [InlineData("a5 06 04 16 01 c6", 22, true)]
    public void Mojhon_Battery_ReadsTheCapturedFrames(string hex, int percent, bool charging)
    {
        var read = Mojhon.Battery(Convert.FromHexString(hex.Replace(" ", "")));
        Assert.Equal(percent, read?.Percent);
        Assert.Equal(charging, read?.Charging);
    }

    [Theory]
    [InlineData("a5 06 04 12 00 c0")] // a sum that does not hold
    [InlineData("a5 06 0b 61 00 17")] // another register, its sum right (0x0B stays 97 and is not the level)
    [InlineData("a5 06 04 00 00 af")] // a level of 0 is no reading
    [InlineData("a5 04 04 ad")]       // the request read back
    public void Mojhon_Battery_RefusesWhatIsNotTheLevel(string hex)
        => Assert.Null(Mojhon.Battery(Convert.FromHexString(hex.Replace(" ", ""))));

    [Fact]
    public void Mojhon_TakesTheVendorChannelOfTheGale2_NotItsOtherCollections()
    {
        Assert.Equal("MOJHON", Brand(Shape(0xE2B7, 0x122C, 0xFFAB, 0x0001, 65, 65, 0x00)));
        Assert.Null(Brand(Shape(0xE2B7, 0x122C, 0xFFB1, 0x0001, 0, 13, 0x00))); // write-only collection
        Assert.Null(Brand(Shape(0xE2B7, 0x122C, 0x0001, 0x0005, 15, 0)));       // the gamepad itself
        Assert.Null(Brand(Shape(0xE2B7, 0x0001, 0xFFAB, 0x0001, 65, 65, 0x00))); // another product
    }

    // ----- EPOS H3PRO Hybrid -----

    // A reply: the frame the headset sends back, the target one less than the request's, with the payload's CRC-8.
    private static byte[] EposReply(ushort target, ushort group, ushort command, ushort value, byte crc)
    {
        var report = new byte[107];
        void Put(int at, ushort word) { report[at] = (byte)word; report[at + 1] = (byte)(word >> 8); }
        report[0] = 0x01; report[1] = 0xEB; report[2] = 0xFE;
        Put(3, 1); Put(5, 4); Put(7, target); Put(9, 6); Put(11, crc);
        Put(13, group); Put(15, command); Put(17, value);
        return report;
    }

    [Fact]
    public void Epos_Crc8_MatchesAnIndependentImplementation()
    {
        // 0xA5 and 0x5C were computed apart from this code (CRC-8, polynomial 0x07, initial value 0xFF).
        Assert.Equal(0xA5, EposH3Pro.Crc8([0x12, 0x00, 0x03, 0x00]));
        Assert.Equal(0x5C, EposH3Pro.Crc8([0x12, 0x00, 0x03, 0x00, 0x3C, 0x00]));
    }

    [Fact]
    public void Epos_BatteryRequest_IsTheFrameOfGroup12Command3()
    {
        var request = EposH3Pro.Request(0x0005, 0x12, 0x03);
        Assert.Equal(107, request.Length);
        Assert.Equal(new byte[] { 0x01, 0xEB, 0xFE, 0x01, 0x00, 0x04, 0x00, 0x05, 0x00, 0x04, 0x00, 0xA5, 0x00, 0x12, 0x00, 0x03, 0x00 }, request[..17]);
    }

    [Fact]
    public void Epos_Battery_ReadsTheHeadsetBehindTheDongle()
    {
        var read = EposH3Pro.Battery(0x0005)(EposReply(0x0004, 0x12, 0x03, 60, 0x5C));
        Assert.Equal(60, read?.Percent);
        Assert.False(read?.Asleep);
    }

    [Fact]
    public void Epos_Battery_ReadsTheCabledHeadset()
        => Assert.Equal(60, EposH3Pro.Battery(0x0001)(EposReply(0x0000, 0x12, 0x03, 60, 0x5C))?.Percent);

    [Fact]
    public void Epos_Battery_SaysAsleep_WhenTheDongleAnswersForItself()
    {
        var reply = EposReply(0x0001, 0x12, 0x03, 0, 0);
        Fix(reply);
        var read = EposH3Pro.Battery(0x0005)(reply);
        Assert.True(read?.Asleep);
        Assert.Null(read?.Percent);
    }

    // The frame's CRC set for whatever was put in it, so a test can build any reply.
    private static void Fix(byte[] report) => report[11] = EposH3Pro.Crc8(report.AsSpan(13, 6));

    [Fact]
    public void Epos_Battery_RefusesABadCrc_AnotherCommand_AndANonsenseLevel()
    {
        var battery = EposH3Pro.Battery(0x0005);
        Assert.Null(battery(EposReply(0x0004, 0x12, 0x03, 60, 0x00)));  // CRC wrong
        var other = EposReply(0x0004, 0x0A, 0x0C, 60, 0); Fix(other);   // another command (the sidetone)
        Assert.Null(battery(other));
        var high = EposReply(0x0004, 0x12, 0x03, 200, 0); Fix(high);    // a percent over 100
        Assert.Null(battery(high));
    }

    [Fact]
    public void Epos_TakesTheVendorCollectionOfTheDongleAndTheCable()
    {
        Assert.Equal("EPOS H3PRO", Brand(Shape(0x1395, 0x0303, 0xFFFF, 0x0001, 107, 107, 0x01)));
        Assert.Equal("EPOS H3PRO", Brand(Shape(0x1395, 0x0101, 0xFFFF, 0x0001, 107, 107, 0x01)));
        Assert.Null(Brand(Shape(0x1395, 0x0303, 0x000C, 0x0001, 3, 0)));
        Assert.Null(Brand(Shape(0x1395, 0x0999, 0xFFFF, 0x0001, 107, 107, 0x01)));
    }

    [Fact]
    public void TheNewFamilies_AreFoundOnce_UnderPrefixesNoOtherFamilyUses()
    {
        foreach (var prefix in new[] { "plantronics", "mojhon", "epos" })
            Assert.Single(BrandFamilies.All, family => family.Prefix == prefix);
    }
}
