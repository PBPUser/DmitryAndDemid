using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Tests;

/// <summary>
/// Shared setup for the headless test suite. The game reads all of its content through <see cref="Assets"/>
/// (the <see cref="IAssetSource"/> seam); a ProjectReference does not copy the game's Assets/ folder into the
/// test output, so before any test can read content it has to point that seam at the real repository Assets/.
/// <see cref="UseRepoAssets"/> does exactly that, and is idempotent so every test fixture can call it freely.
/// </summary>
public static class TestEnvironment
{
    /// <summary>The repository root — the nearest ancestor of the test binary that actually contains Assets/.</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        // Walk up from the test binary (…/Tests/bin/Debug/net10.0/linux-x64/) until a directory holding the
        // game's texture folder is found. Anchoring on Assets/Textures (not just Assets/) avoids matching a
        // stray empty "Assets" dir, and works no matter how deep the build output nests.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "Textures")))
                return dir.FullName;

        throw new DirectoryNotFoundException(
            "Could not locate the repository root (an ancestor directory containing Assets/Textures) starting " +
            $"from '{AppContext.BaseDirectory}'. Run the tests from within the repo working tree.");
    }

    /// <summary>Point the asset seam at the shipped repo Assets/, so tests see exactly what the game loads.</summary>
    public static void UseRepoAssets() => Assets.Source = new FileSystemAssetSource(RepoRoot);
}
