using RetroHub.Core.Input;
using Xunit;

namespace RetroHub.Core.Tests;

public class GamepadStatusTests
{
    [Fact]
    public void Exact_percentage_on_battery()
    {
        var pad = new GamepadStatus(0, "DualSense Wireless Controller", PadModel.PlayStation, true, PadPower.Battery, 73);
        Assert.Equal("73%", pad.BatteryText);
        Assert.Equal("DualSense Wireless Controller · 73%", pad.Summary);
        Assert.False(pad.IsLow);
    }

    [Fact]
    public void Low_only_when_running_on_battery()
    {
        Assert.True(new GamepadStatus(0, "Pad", PadModel.Xbox, true, PadPower.Battery, 15).IsLow);
        Assert.False(new GamepadStatus(0, "Pad", PadModel.Xbox, true, PadPower.Charging, 5).IsLow);
        Assert.False(new GamepadStatus(0, "Pad", PadModel.Xbox, true, PadPower.Unknown, null).IsLow);
    }

    [Fact]
    public void Unknown_power_has_no_battery_text()
    {
        var pad = new GamepadStatus(0, "8BitDo Pro 2", PadModel.Generic, false, PadPower.Unknown, null);
        Assert.Equal("", pad.BatteryText);
        Assert.Equal("8BitDo Pro 2", pad.Summary);
    }

    [Fact]
    public void Weakest_is_the_lowest_on_battery()
    {
        var pads = new[]
        {
            new GamepadStatus(0, "A", PadModel.Xbox, true, PadPower.Battery, 80),
            new GamepadStatus(1, "B", PadModel.Xbox, true, PadPower.Charging, 3),
            new GamepadStatus(2, "C", PadModel.Xbox, true, PadPower.Battery, 40),
        };
        Assert.Equal("C", GamepadStatus.Weakest(pads)!.Name);
        Assert.Null(GamepadStatus.Weakest([new GamepadStatus(0, "W", PadModel.Xbox, false, PadPower.Wired, null)]));
    }

    [Theory]
    [InlineData(1, 0, PadPower.Wired, null)]
    [InlineData(2, 1, PadPower.Battery, 15)]
    [InlineData(3, 3, PadPower.Battery, 100)]
    [InlineData(0xFF, 2, PadPower.Unknown, null)]
    public void XInput_battery_maps_to_shared_thresholds(byte type, byte level, PadPower power, int? percent)
    {
        var pad = GamepadStatus.FromXInput(0, 0x045E, type, level);
        Assert.Equal(PadModel.Xbox, pad.Model);
        Assert.Equal(power, pad.Power);
        Assert.Equal(percent, pad.Percent);
    }

    [Fact]
    public void XInput_low_level_counts_as_low_and_reads_as_a_level()
    {
        var pad = GamepadStatus.FromXInput(0, 0x045E, 2, 1);
        Assert.True(pad.IsLow);
        Assert.Equal("Battery low", pad.BatteryText);
    }
}
