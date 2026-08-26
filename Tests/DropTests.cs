using DmitryAndDemid.Data;
using Xunit;

namespace DmitryAndDemid.Tests;

/// <summary>
/// The item-drop bit mask (<see cref="Drop"/>), packed into a single int in stage data. The layout is spec'd
/// in <c>Data/Drop.sp</c> — five flag bits in the low byte, then three one-byte counts — and
/// <see cref="The_mask_matches_the_drop_sp_spec"/> pins the code to that spec. Pure bit work; no GPU.
/// </summary>
public class DropTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(0x1F)]           // every flag, no counts
    [InlineData(0x01020310)]     // distinct count bytes, one flag
        [InlineData(0x1234561F)]     // distinct count bytes, every flag
    public void Mask_round_trips(int mask)
    {
        Assert.Equal(mask, new Drop(mask).ToInt32());
    }

    /// <summary>Data/Drop.sp: bit 0 heart, 1 heart piece, 2 star, 3 star piece, 4 full power; byte 1 large
    /// power, byte 2 power, byte 3 score.</summary>
    [Fact]
    public void The_mask_matches_the_drop_sp_spec()
    {
        Assert.True(new Drop(0x01).DropHeart);
        Assert.True(new Drop(0x02).DropHeartPiece);
        Assert.True(new Drop(0x04).DropStar);
        Assert.True(new Drop(0x08).DropStarPiece);
        Assert.True(new Drop(0x10).DropFullPower);
        Assert.Equal(7, new Drop(0x0700).DropLargePower);
        Assert.Equal(9, new Drop(0x090000).DropPower);
        Assert.Equal(11, new Drop(0x0B000000).DropScore);

        // And nothing bleeds across: a lone flag leaves every count and every other flag clear.
        Drop heartOnly = new Drop(0x01);
        Assert.False(heartOnly.DropStar);
        Assert.Equal(0, heartOnly.DropScore);
        Assert.Equal(0, heartOnly.DropPower);
        Assert.Equal(0, heartOnly.DropLargePower);
    }

    [Fact]
    public void ToInt32_packs_flags_into_the_low_byte_and_counts_above_it()
    {
        var drop = new Drop
        {
            DropHeart = true,
            DropStarPiece = true,
            DropFullPower = true,
            DropLargePower = 0x12,
            DropPower = 0x34,
            DropScore = 0x56,
        };
        Assert.Equal(0x56341219, drop.ToInt32());
    }

    [Fact]
    public void Default_drop_is_empty()
    {
        Assert.Equal(0, new Drop().ToInt32());
    }
}
