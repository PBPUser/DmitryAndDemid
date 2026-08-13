using DmitryAndDemid.Rendering;
using Xunit;

namespace DmitryAndDemid.Tests;

/// <summary>
/// The engine's names — two for the whole, one each for its three parts. It is called the Nikitos Engine and
/// the Lihanov Engine interchangeably, which is a deliberate quirk and not a thing to tidy up — but it only
/// stays a quirk while there are exactly two of them. Hand-typed names are how two becomes four ("Nikitos
/// engine", "the lihanov Engine", …), so everything that puts a name on screen or in a log reads it from
/// <see cref="Engine"/>, and <see cref="No_source_file_hardcodes_an_engine_name_as_a_string_literal"/> is what
/// keeps it that way.
///
/// Prose is exempt on purpose: comments, docs and CLAUDE.md mix the two names freely, because that is exactly
/// what the project does out loud.
/// </summary>
public class EngineNameTests
{
    [Fact]
    public void The_engine_answers_to_both_of_its_names()
    {
        Assert.Equal("Nikitos Engine", Engine.Name);
        Assert.Equal("Lihanov Engine", Engine.AlternateName);
    }

    [Fact]
    public void Each_part_of_the_engine_has_its_own_name()
    {
        Assert.Equal("Likhanov32D", Engine.GraphicsName);   // graphics; the "32D" is 3D and 2D
        Assert.Equal("Demidonic", Engine.AudioName);        // sound
        Assert.Equal("Pizzics", Engine.PhysicsName);        // physics; pizza + physics
    }

    /// <summary>
    /// Likh-anov32D and Lih-anov Engine. The graphics part and the engine alias are spelled differently, which
    /// looks exactly like a typo and is not one — so it gets a test that says so out loud, before someone
    /// helpfully "corrects" one of them into the other.
    /// </summary>
    [Fact]
    public void The_graphics_name_keeps_its_own_spelling_of_Likhanov()
    {
        Assert.StartsWith("Likh", Engine.GraphicsName, StringComparison.Ordinal);
        Assert.StartsWith("Lih", Engine.AlternateName, StringComparison.Ordinal);
        Assert.DoesNotContain("Likh", Engine.AlternateName, StringComparison.Ordinal);
    }

    /// <summary>
    /// The engine's name is not the backend's. These are printed side by side (window title, splash, debug
    /// overlay) so a bug report cannot leave one of them ambiguous, which only works while they are distinct
    /// strings from distinct sources.
    /// </summary>
    [Fact]
    public void The_engine_name_is_not_a_renderer_name()
    {
        foreach ((string key, string name) in RendererRegistry.Available)
        {
            Assert.NotEqual(Engine.Name, name);
            Assert.NotEqual(Engine.AlternateName, name);
            Assert.NotEqual(Engine.Name, key);
        }
    }

    /// <summary>
    /// Searching for the name WITH its quotes is a good enough proxy for "this is a string literal, not a
    /// comment": prose says the Nikitos Engine, code says <c>Engine.Name</c>, and only a literal writes
    /// <c>"Nikitos Engine"</c>. Rendering/Engine.cs is where the two literals live, so it is the one exemption.
    /// </summary>
    [Fact]
    public void No_source_file_hardcodes_an_engine_name_as_a_string_literal()
    {
        string[] literals =
        [
            $"\"{Engine.Name}\"", $"\"{Engine.AlternateName}\"",
            $"\"{Engine.GraphicsName}\"", $"\"{Engine.AudioName}\"", $"\"{Engine.PhysicsName}\"",
        ];
        string[] allowed =
        [
            Path.Combine("Rendering", "Engine.cs"),          // the definitions themselves
            Path.Combine("Tests", "EngineNameTests.cs"),     // this file, which has to name them to check them
        ];

        var offenders = new List<string>();
        foreach (string path in Directory.EnumerateFiles(TestEnvironment.RepoRoot, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(TestEnvironment.RepoRoot, path);
            // bin/ and obj/ are committed in this repo, so the tree is full of generated copies of everything.
            if (relative.Contains($"bin{Path.DirectorySeparatorChar}") ||
                relative.Contains($"obj{Path.DirectorySeparatorChar}") ||
                allowed.Contains(relative))
                continue;
            string text = File.ReadAllText(path);
            foreach (string literal in literals)
                if (text.Contains(literal, StringComparison.Ordinal))
                    offenders.Add($"{relative} contains the literal {literal}");
        }

        Assert.True(offenders.Count == 0,
            "Engine names must come from Engine.Name / Engine.AlternateName, not a typed-out string: " +
            string.Join("; ", offenders));
    }
}
