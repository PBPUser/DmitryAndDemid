using System.Numerics;
using DmitryAndDemid.Gameplay.RuntimeData;
using DmitryAndDemid.Utils;
using Xunit;

namespace DmitryAndDemid.Tests;

/// <summary>
/// The pure parts of Dmitry's fourth stage-3 card (the complaints box and the moon): the rectangle hit test the
/// Pizzics sweep uses for the box, and the per-difficulty rebound stock the moon is launched with. The card's
/// tick scripts themselves need a GameBox, so they are verified by playing it.
/// </summary>
public class GrievanceCardTests
{
    public GrievanceCardTests() => TestEnvironment.UseRepoAssets();

    private static readonly Vector2 BoxCentre = new(192, 232);
    private static readonly Vector2 BoxSize = new(160, 80);

    [Theory]
    [InlineData(192, 232)]     // dead centre
    [InlineData(112, 232)]     // on the left edge
    [InlineData(272, 192)]     // top-right corner
    [InlineData(192, 275)]     // 3px below the bottom edge, inside a 4px radius
    public void Shot_inside_or_touching_the_box_hits(float x, float y)
    {
        Assert.True(HelperPure.CircleTouchesBox(new Vector2(x, y), 4f, BoxCentre, BoxSize));
    }

    [Theory]
    [InlineData(100, 232)]     // 12px left of the left edge
    [InlineData(192, 180)]     // 12px above the top edge
    [InlineData(276, 188)]     // diagonally off the top-right corner (4px out on both axes = 5.6px away)
    public void Shot_clear_of_the_box_misses(float x, float y)
    {
        Assert.False(HelperPure.CircleTouchesBox(new Vector2(x, y), 4f, BoxCentre, BoxSize));
    }

    /// <summary>A circle the size of the old collision disc would have swallowed a corner miss; the rectangle
    /// is exactly what the sprite covers, no more.</summary>
    [Fact]
    public void Corner_miss_that_a_circle_would_have_caught_is_a_miss()
    {
        // 6px out on both axes from the top-right corner: 8.5px from the corner, well inside a 40px radius
        // disc centred on the box, but not touching the rectangle with a 4px shot.
        Assert.False(HelperPure.CircleTouchesBox(new Vector2(278, 186), 4f, BoxCentre, BoxSize));
    }

    [Theory]
    [InlineData(0, 3)]      // Easy
    [InlineData(1, 5)]      // Normal
    [InlineData(2, 6)]      // Hard
    [InlineData(3, 7)]      // Max
    [InlineData(4, 7)]      // Extra / a higher practice tier plays it at Max
    [InlineData(-1, 3)]     // clamped below
    [InlineData(9, 7)]      // clamped above
    public void Moon_rebound_stock_follows_difficulty(int difficulty, int bounces)
    {
        Assert.Equal(bounces, ActionsScope.MoonBounceLimit(difficulty));
    }
}
