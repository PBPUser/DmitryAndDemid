namespace DmitryAndDemid.Rendering;

/// <summary>Shared GLSL helpers. Both backends consume the same shader sources from Assets/Shaders.</summary>
public static class ShaderSource
{
    /// <summary>Pulls `uniform &lt;type&gt; &lt;name&gt;;` declarations out of GLSL, for the debug shader previewer.</summary>
    public static string[] ParseUniformNames(string source)
    {
        List<string> names = new();
        foreach (string line in source.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("uniform"))
                continue;
            string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                continue;
            names.Add(parts[2].TrimEnd(';').Split('[')[0]);
        }
        return names.ToArray();
    }
}
