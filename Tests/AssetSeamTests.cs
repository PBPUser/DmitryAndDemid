using DmitryAndDemid.Utils;
using Xunit;

namespace DmitryAndDemid.Tests;

/// <summary>
/// Smoke tests for the asset seam itself: the files the game reaches for on the very first frames must resolve
/// through <see cref="Assets"/>. These are the assertions <c>--selftest</c> prints as diagnostics, promoted to
/// real pass/fail so a missing or moved core asset is caught before it becomes a black-screen boot.
/// </summary>
public class AssetSeamTests
{
    public AssetSeamTests() => TestEnvironment.UseRepoAssets();

    [Theory]
    [InlineData("Assets/Data/translation.json")]
    [InlineData("Assets/Data/cyrilic-transliteration-table.json")]
    [InlineData("Assets/Shaders/base.vs")]
    public void Core_startup_asset_resolves(string path)
    {
        Assert.True(Assets.Exists(path), $"Startup asset '{path}' did not resolve to '{Assets.Resolve(path)}'.");
    }

    [Fact]
    public void Enumerated_files_are_ordinally_sorted_and_stable()
    {
        // The dictionaries are populated in enumeration order, so a stable, sorted enumeration is what makes the
        // whole "same count / same keys across restarts" property hold. Verify the seam's contract directly.
        string[] first = Assets.Files("Assets/Textures", "*.png");
        string[] second = Assets.Files("Assets/Textures", "*.png");

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
        Assert.Equal(first.OrderBy(p => p, StringComparer.Ordinal), first);
    }
}
