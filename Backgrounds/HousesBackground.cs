using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Backgrounds;

/// <summary>
/// An endless field of low houses seen from a camera flying ~9 m above the ground.
/// Likhanov32D only rasterises in 2D — the 3D in its name is raymarching like this — so the city lives in the
/// <c>houses.fs</c> fragment shader; this class just drives its <c>time</c> uniform and
/// blits a full-screen quad through it (same pattern as <see cref="DrogichinBackground"/>).
///
/// It is also the reference user of <see cref="BackgroundLighting"/>: the city is rendered unlit into
/// <see cref="Scene"/> and then lit by street lamps sliding past the camera, plus a cool light from above.
/// </summary>
public class HousesBackground : StageBackground
{
    private const int Width = 384, Height = 448;
    private const int LampCount = 3;

    private readonly TargetHandle Temp;    // dummy quad source; the cityscape is procedural
    private readonly TargetHandle Scene;   // the unlit city, before lighting
    private readonly ShaderHandle Shader;
    private readonly int LocationTime;

    private readonly BackgroundLighting Lighting = new(Width, Height);
    private readonly Light[] Lamps = new Light[LampCount];

    public HousesBackground()
    {
        Temp = LoadRenderTexture(Width, Height);
        Scene = LoadRenderTexture(Width, Height);
        Shader = Runtime.CurrentRuntime.Shaders["houses"];
        LocationTime = GetShaderLocation(Shader, "time");

        // The cityscape is a rainy DAYLIT scene, so this only pulls it down to a cold overcast dusk rather
        // than to night — the lamps then read as warm pools over it instead of being the only light there is.
        Lighting.Ambient = new Rgba(150, 158, 182, 255);

        // Street lamps: warm, tight, flickering, sliding down the screen as the camera flies over them. They
        // are spread out along the fall so one is always somewhere on screen.
        for (int i = 0; i < LampCount; i++)
            Lamps[i] = Lighting.Add(new Light
            {
                Color = new Rgba(255, 186, 92, 255),
                Radius = 130f,
                Intensity = 1.15f,
                Falloff = 2.2f,
                FlickerAmount = 0.12f,
                PulseAmount = 0.06f,
                PulseSpeed = 0.7f,
                Phase = i / (float)LampCount,
            });

        // A wide cool wash from above the horizon, so the far end of the city is not pitch black.
        Lighting.Add(new Light
        {
            Position = new Vector2(Width * 0.5f, Height * 0.12f),
            Color = new Rgba(150, 186, 255, 255),
            Radius = 320f,
            Intensity = 0.7f,
            Falloff = 1.4f,
            PulseAmount = 0.08f,
            PulseSpeed = 0.13f,
        });
    }

    protected override void Update(int tick, float delta)
    {
        if (!Configuration.Config.HighGraphics)
            return;

        // The unlit city, into our own target so the lighting pass has something to read.
        BeginTextureMode(Scene);
        SetShaderValue(Shader, LocationTime, tick / 60f + delta, UniformType.Float);
        BeginShaderMode(Shader);
        DrawTexturePro(Temp.Texture, Helper.GetFullSourceRenderTexture(Temp),
            new Rect(0, 0, Width, Height), Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
        EndTextureMode();

        // Lamps drift down the screen at the flight speed and wrap around, each on its own lane.
        float seconds = tick / 60f + delta;
        float span = Height + 220f;
        for (int i = 0; i < LampCount; i++)
        {
            float offset = (seconds * 62f + span * i / LampCount) % span;
            Lamps[i].Position = new Vector2(Width * (0.22f + 0.28f * i), offset - 110f);
        }

        Lighting.Update(tick, delta);
    }

    protected override void Render(TargetHandle texture, int tick, float delta)
    {
        // Low graphics: the houses field is a heavy per-pixel shader, so skip it and draw a plain fill instead.
        if (!Configuration.Config.HighGraphics)
        {
            DrawRectangle(0, 0, Width, Height, new Rgba(22, 24, 34, 255));
            return;
        }
        Lighting.Draw(Scene, new Rect(0, 0, Width, Height));
    }

    protected override void Unload()
    {
        UnloadRenderTexture(Temp);
        UnloadRenderTexture(Scene);
        Lighting.Unload();
        base.Unload();
    }
}
