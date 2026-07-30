using System.Numerics;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Common;

/// <summary>
/// One light in a background's <see cref="BackgroundLighting"/>. Position is in the background's own pixel
/// space (the playfield target is 384x448), origin top-left, exactly the coordinates the background draws in.
/// Everything is a plain mutable field: a background is expected to move its lights around in its Update.
/// </summary>
public sealed class Light
{
    public Vector2 Position;
    public Rgba Color = Rgba.White;

    /// <summary>Reach in pixels. Beyond this the light contributes nothing at all.</summary>
    public float Radius = 120f;

    /// <summary>Brightness at the centre. 1 lights the scene to its unlit colour; above that overexposes it.</summary>
    public float Intensity = 1f;

    /// <summary>Falloff curve: 1 is a linear cone, 2 reads like a normal lamp, higher tightens the core.</summary>
    public float Falloff = 2f;

    /// <summary>Sine breathing, as a fraction of <see cref="Intensity"/>, at <see cref="PulseSpeed"/> Hz.</summary>
    public float PulseAmount;
    public float PulseSpeed = 1f;

    /// <summary>Ragged jitter on top of the pulse, as a fraction of <see cref="Intensity"/> — tube/candle flicker.</summary>
    public float FlickerAmount;

    /// <summary>Offsets this light's pulse and flicker so identical lamps do not beat in unison.</summary>
    public float Phase;

    public bool Enabled = true;

    /// <summary>Brightness at <paramref name="seconds"/> with the pulse and flicker applied.</summary>
    internal float IntensityAt(float seconds)
    {
        float value = Intensity;
        if (PulseAmount != 0f)
            value += Intensity * PulseAmount * MathF.Sin((seconds * PulseSpeed + Phase) * MathF.Tau);
        if (FlickerAmount != 0f)
        {
            // Steps of a hash at ~14 Hz, smoothed between steps: irregular enough to read as a failing tube,
            // and a pure function of the tick so a replay lights the scene the same way twice.
            float t = seconds * 14f + Phase * 10f;
            float a = Hash(MathF.Floor(t)), b = Hash(MathF.Floor(t) + 1f);
            float f = t - MathF.Floor(t);
            value -= Intensity * FlickerAmount * (a + (b - a) * (f * f * (3f - 2f * f)));
        }
        return MathF.Max(value, 0f);
    }

    private static float Hash(float n)
    {
        float s = MathF.Sin(n * 12.9898f) * 43758.5453f;
        return s - MathF.Floor(s);
    }
}

/// <summary>
/// A lighting rig for a <see cref="StageBackground"/>: any number of point lights, accumulated into a light
/// map and multiplied over the background's unlit scene, with a bloom off whatever the lights pick out.
///
/// A background composes one of these and drives it in two steps, because the light map has to be built while
/// no other render target is bound:
/// <code>
///   protected override void Update(int tick, float delta)   // runs OUTSIDE the destination's texture mode
///   {
///       ...draw the unlit scene into your own target...
///       Lighting.Update(tick, delta);
///   }
///   protected override void Render(TargetHandle texture, int tick, float delta)
///   {
///       Lighting.Draw(Scene, new Rect(0, 0, 384, 448));
///   }
/// </code>
///
/// Lights are stamped one draw each rather than passed as a uniform array, so there is no fixed light limit
/// (the backends have no uniform-array support to lean on either way). The map is an 8-bit target, so
/// overlapping lights saturate to white instead of accumulating without bound, which is what you want anyway.
/// </summary>
public sealed class BackgroundLighting
{
    /// <summary>
    /// What unlit parts of the scene are multiplied by — the colour and level of the darkness. Dim it for a
    /// night scene; leave it near white for a daylit one, where the lights then act as overexposure on top.
    /// It is applied in the composite rather than being the light map's clear colour, so it does not eat the
    /// 8-bit map's range (see light_composite.fs).
    /// </summary>
    public Rgba Ambient = new(46, 50, 68, 255);

    /// <summary>Strength of the glow off lit areas. Forced to 0 on low graphics, which skips the gather.</summary>
    public float Bloom = 0.9f;

    /// <summary>How bright a lit pixel must be before it starts to bloom.</summary>
    public float BloomThreshold = 0.55f;

    public readonly List<Light> Lights = new();

    private readonly TargetHandle LightMap;
    private readonly TargetHandle Dummy;   // 1x1 quad source; both light shaders are procedural
    private readonly ShaderHandle PointShader, CompositeShader;
    private readonly int LocationLightColor, LocationFalloff;
    private readonly int LocationAmbient, LocationResolution, LocationBloom, LocationThreshold, LocationLightMap;
    private readonly int Width, Height;

    public BackgroundLighting(int width = 384, int height = 448)
    {
        Width = width;
        Height = height;
        LightMap = LoadRenderTexture(width, height);
        // Something for DrawTexturePro to stretch over each light's box. Its contents never matter and are
        // deliberately left uninitialised: light_point.fs is fully procedural and never samples texture0.
        Dummy = LoadRenderTexture(1, 1);

        PointShader = Runtime.CurrentRuntime.Shaders["light_point"];
        LocationLightColor = GetShaderLocation(PointShader, "light_color");
        LocationFalloff = GetShaderLocation(PointShader, "falloff");

        CompositeShader = Runtime.CurrentRuntime.Shaders["light_composite"];
        LocationAmbient = GetShaderLocation(CompositeShader, "ambient");
        LocationResolution = GetShaderLocation(CompositeShader, "resolution");
        LocationBloom = GetShaderLocation(CompositeShader, "bloom");
        LocationThreshold = GetShaderLocation(CompositeShader, "threshold");
        LocationLightMap = GetShaderLocation(CompositeShader, "lightMap");
    }

    /// <summary>Adds a light and hands it back, so a caller can keep hold of it and move it later.</summary>
    public Light Add(Light light)
    {
        Lights.Add(light);
        return light;
    }

    /// <summary>
    /// Advances the lights' animation and rebuilds the light map. Must be called from the background's Update
    /// (or anywhere else with no render target bound), never from inside its Render.
    /// </summary>
    public void Update(int tick, float delta)
    {
        float seconds = tick / 60f + delta;
        BeginTextureMode(LightMap);
        ClearBackground(Rgba.Black);   // pure light only; ambient is added in the composite
        BeginBlendMode(BlendMode.Additive);
        BeginShaderMode(PointShader);
        Rect source = Helper.GetFullSourceRenderTexture(Dummy);
        foreach (Light light in Lights)
        {
            float intensity = light.Enabled ? light.IntensityAt(seconds) : 0f;
            if (intensity <= 0f || light.Radius <= 0f)
                continue;
            SetShaderValue(PointShader, LocationLightColor,
                new Vector4(light.Color.R / 255f, light.Color.G / 255f, light.Color.B / 255f, intensity),
                UniformType.Vec4);
            SetShaderValue(PointShader, LocationFalloff, light.Falloff, UniformType.Float);
            // One quad over the light's bounding square; the shader shapes the disc inside it.
            DrawTexturePro(Dummy.Texture, source,
                new Rect(light.Position.X - light.Radius, light.Position.Y - light.Radius,
                         light.Radius * 2, light.Radius * 2),
                Vector2.Zero, 0f, Rgba.White);
        }
        EndShaderMode();
        EndBlendMode();
        EndTextureMode();
    }

    /// <summary>
    /// Draws <paramref name="scene"/> — the background's unlit render — lit by the map built in
    /// <see cref="Update"/>. Call from the background's Render, inside the destination's texture mode.
    /// </summary>
    public void Draw(TargetHandle scene, Rect destination, Rgba? tint = null)
    {
        float bloom = Configuration.Config.HighGraphics ? Bloom : 0f;
        SetShaderValue(CompositeShader, LocationAmbient,
            new Vector4(Ambient.R / 255f, Ambient.G / 255f, Ambient.B / 255f, 1f), UniformType.Vec4);
        SetShaderValue(CompositeShader, LocationResolution, new Vector2(Width, Height), UniformType.Vec2);
        SetShaderValue(CompositeShader, LocationBloom, bloom, UniformType.Float);
        SetShaderValue(CompositeShader, LocationThreshold, BloomThreshold, UniformType.Float);
        SetShaderValueTexture(CompositeShader, LocationLightMap, LightMap.Texture);
        BeginShaderMode(CompositeShader);
        DrawTexturePro(scene.Texture, Helper.GetFullSourceRenderTexture(scene), destination, Vector2.Zero, 0f,
            tint ?? Rgba.White);
        EndShaderMode();
    }

    public void Unload()
    {
        UnloadRenderTexture(LightMap);
        UnloadRenderTexture(Dummy);
    }
}
