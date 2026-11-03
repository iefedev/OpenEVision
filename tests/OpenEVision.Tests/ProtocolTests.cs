using OpenEVision.Dev;
using OpenEVision.Device;
using Xunit;

namespace OpenEVision.Tests;

public class ProtocolTests
{
    static readonly DeviceProfile P = DeviceProfiles.Active;

    [Fact]
    public void Packet_has_report_id_header_and_checksum()
    {
        var b = HidKeyboard.Packet(HidKeyboard.CmdReadConfig, 0x0123, default, 8);
        Assert.Equal(HidKeyboard.Pkt, b.Length);
        Assert.Equal(HidKeyboard.ReportId, b[0]);
        Assert.Equal(HidKeyboard.CmdReadConfig, b[3]);
        Assert.Equal(8, b[4]);
        Assert.Equal(0x23, b[5]);
        Assert.Equal(0x01, b[6]);
        int sum = b.Skip(3).Sum(x => x);
        Assert.Equal(sum & 0xFF, b[1]);
        Assert.Equal(sum >> 8, b[2]);
    }

    [Theory]
    [InlineData(HidKeyboard.CmdWriteConfig, 0x00, 8, true)]       // effect header
    [InlineData(HidKeyboard.CmdWriteConfig, 0xBF, 2, false)]      // straddles header and key table
    [InlineData(HidKeyboard.CmdWriteConfig, 0xC0, 384, true)]     // whole key table
    [InlineData(HidKeyboard.CmdWriteConfig, 0xC0, 385, false)]
    [InlineData(HidKeyboard.CmdWriteConfig, 0x2C0, 1, false)]     // copies of the key table: unknown purpose
    [InlineData(HidKeyboard.CmdWriteCustom, 0, 126 * 3, true)]
    [InlineData(HidKeyboard.CmdWriteCustom, 0, 126 * 3 + 1, false)]
    [InlineData(HidKeyboard.CmdLive, 0, 128 * 3, true)]
    [InlineData(HidKeyboard.CmdReadConfig, 0, 1, false)]          // not a write command
    [InlineData(0x7F, 0, 1, false)]
    public void Write_guard_only_allows_known_regions(byte cmd, int offset, int length, bool allowed) =>
        Assert.Equal(allowed, HidKeyboard.WriteAllowed(P, cmd, offset, length));

    [Fact]
    public void Writes_outside_the_guard_never_reach_the_device()
    {
        using var kb = new HidKeyboard(new DemoTransport(P), P);
        Assert.Throws<InvalidOperationException>(() => kb.Write(HidKeyboard.CmdWriteConfig, 0x2C0, [0]));
    }

    [Fact]
    public void Multi_packet_write_and_read_round_trip()
    {
        DemoTransport.Reset();
        using var kb = new HidKeyboard(new DemoTransport(P), P);
        var colors = Enumerable.Range(0, P.LedCount * 3).Select(i => (byte)(i * 7)).ToArray();
        kb.Configure(() => kb.Write(HidKeyboard.CmdWriteCustom, 0, colors));
        Assert.Equal(colors, kb.Read(HidKeyboard.CmdReadCustom, 0, colors.Length));
        DemoTransport.Reset();
    }

    [Fact]
    public void Custom_led_matrix_is_unique_and_in_range()
    {
        var leds = KeyLayout.CustomLed.Values.ToList();
        Assert.Equal(leds.Count, leds.Distinct().Count());
        Assert.All(leds, i => Assert.InRange(i, 0, P.LedCount - 1));
        // Every key drawn on screen has an LED.
        Assert.All(KeyLayout.Keys, k => Assert.True(KeyLayout.CustomLed.ContainsKey(k.Code), k.Code));
    }

    [Fact]
    public void Key_table_entries_decode_back_to_their_keys()
    {
        var codes = KeyLayout.CustomLed.Keys.ToList();
        var table = new byte[P.KeyTableEntries * 3];
        for (int i = 0; i < codes.Count; i++) LiveMap.Encode(codes[i])!.CopyTo(table, i * 3);
        var map = LiveMap.Decode(table);
        for (int i = 0; i < codes.Count; i++) Assert.Equal(i, map[codes[i]]);
    }

    [Fact]
    public void Every_key_action_round_trips_through_its_bytes()
    {
        foreach (var a in KeyActions.All) Assert.Equal(a.Id, KeyActions.FromBytes(KeyActions.ToBytes(a.Id)));
        Assert.Throws<ArgumentException>(() => KeyActions.ToBytes("k:ff"));
    }
}
