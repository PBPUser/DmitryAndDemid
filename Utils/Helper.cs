using System.Numerics;
using Raylib_cs;

namespace DmitryAndDemid.Utils;

public static class Helper
{
    public static void LoadShaderAttribs()
    {
        LocationCloudRadius = Raylib.GetShaderLocation(Runtime.CurrentRuntime.Shaders["cloud"], "radius");
        LocationCloudDimensions = Raylib.GetShaderLocation(Runtime.CurrentRuntime.Shaders["cloud"], "dimenssions");
        LocationCloudAngle = Raylib.GetShaderLocation(Runtime.CurrentRuntime.Shaders["cloud"], "angle");
        LocationCloudWidth = Raylib.GetShaderLocation(Runtime.CurrentRuntime.Shaders["cloud"], "width");
        LocationCloudSize = Raylib.GetShaderLocation(Runtime.CurrentRuntime.Shaders["cloud"], "size");

        LocationWaveScale = Raylib.GetShaderLocation(Runtime.CurrentRuntime.Shaders["wave"], "scale");
        LocationWaveXPower = Raylib.GetShaderLocation(Runtime.CurrentRuntime.Shaders["wave"], "xPower");
        LocationWaveOffsetX = Raylib.GetShaderLocation(Runtime.CurrentRuntime.Shaders["wave"], "offsetX");
        LocationWaveOffsetY = Raylib.GetShaderLocation(Runtime.CurrentRuntime.Shaders["wave"], "offsetY");
        LocationWaveScreenSize = Raylib.GetShaderLocation(Runtime.CurrentRuntime.Shaders["wave"], "screenSize");
        LocationWaveScreenColor = Raylib.GetShaderLocation(Runtime.CurrentRuntime.Shaders["wave"], "color");

        LocationFlipScreenSize = Raylib.GetShaderLocation(Runtime.CurrentRuntime.Shaders["flip"], "screenSize");

        PizzaSource = new Rectangle(0, 0, Runtime.CurrentRuntime.Textures["pizza.png"].Width, Runtime.CurrentRuntime.Textures["pizza.png"].Height);
    }

    static Rectangle PizzaSource;

    public static bool GetResolutionFromString(string str, out (int width, int height) res)
    {
        res = (0, 0);
        var split = str.Split("x");
        if (split.Length < 2)
            return false;
        if (!int.TryParse(split[0], out res.width))
            return false;
        return int.TryParse(split[1], out res.height);
    }

    public static bool GetMultiplyerFromRes(string str, out double multiplyer)
    {
        multiplyer = 0;
        (int width, int height) res;
        if (!GetResolutionFromString(str, out res))
            return false;
        multiplyer = ((double)res.width) / 640d;
        return true;
    }

    static int LocationCloudRadius;
    static int LocationCloudDimensions;
    static int LocationCloudAngle;
    static int LocationCloudWidth;
    static int LocationCloudSize;
    static int LocationCloudScreenSize;

    public static RenderTexture2D RenderTextureInCloud(Texture2D texture, float radius = 3f, float angle = -0.85f, float width = 0.35f, float size = 1.4f)
    {
        RenderTexture2D cloud = Raylib.LoadRenderTexture(texture.Width * 2, texture.Height * 2);
        var arr = new float[] { 1, 1 };
        Raylib.SetShaderValue(Runtime.CurrentRuntime.Shaders["cloud"], LocationCloudRadius, radius, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(Runtime.CurrentRuntime.Shaders["cloud"], LocationCloudAngle, angle, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(Runtime.CurrentRuntime.Shaders["cloud"], LocationCloudWidth, width, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(Runtime.CurrentRuntime.Shaders["cloud"], LocationCloudSize, size, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(Runtime.CurrentRuntime.Shaders["cloud"], LocationCloudDimensions, arr, ShaderUniformDataType.Vec2);
        Raylib.BeginTextureMode(cloud);
        Raylib.BeginShaderMode(Runtime.CurrentRuntime.Shaders["cloud"]);
        Raylib.DrawTexturePro(Runtime.CurrentRuntime.Textures["pizza.png"], PizzaSource, new Rectangle(0, 0, cloud.Texture.Width, cloud.Texture.Height), Vector2.Zero, 0f, Color.White);//
        Raylib.EndShaderMode();
        Raylib.DrawTexture(texture, texture.Width / 2, texture.Height / 2, Color.White);
        Raylib.EndTextureMode();
        return cloud;
    }

    static int LocationWaveScale;
    static int LocationWaveXPower;
    static int LocationWaveOffsetX;
    static int LocationWaveOffsetY;
    static int LocationWaveScreenSize;
    static int LocationWaveScreenColor;

    public static void DrawWave(Color color, float offsetX, float offsetY, float xPower, float scale, Rectangle target)
    {
        Raylib.SetShaderValue(Runtime.CurrentRuntime.Shaders["wave"], LocationWaveScale, scale, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(Runtime.CurrentRuntime.Shaders["wave"], LocationWaveXPower, xPower, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(Runtime.CurrentRuntime.Shaders["wave"], LocationWaveOffsetX, offsetX, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(Runtime.CurrentRuntime.Shaders["wave"], LocationWaveOffsetY, offsetY, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(Runtime.CurrentRuntime.Shaders["wave"], LocationWaveScreenColor, ColorToVector(color), ShaderUniformDataType.Vec4);
        Raylib.SetShaderValue(Runtime.CurrentRuntime.Shaders["wave"], LocationWaveScreenSize, new float[] { target.Width, target.Height }, ShaderUniformDataType.Vec2);
        Raylib.BeginShaderMode(Runtime.CurrentRuntime.Shaders["wave"]);
        Raylib.DrawRectanglePro(target, Vector2.Zero, 0, Color.White);
        Raylib.EndShaderMode();
    }

    static int LocationFlipScreenSize;

    public static Vector4 ColorToVector(Color color)
    {
        return new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
    }

    public static Rectangle Mix(Rectangle rc1, Rectangle rc2, float mix)
    {
        float imix = 1f - mix;
        return new Rectangle(
            rc1.X * imix + rc2.X * mix,
            rc1.Y * imix + rc2.Y * mix,
            rc1.Width * imix + rc2.Width * mix,
            rc1.Height * imix + rc2.Height * mix
        );
    }

    public static Vector4 Mix(Vector4 color1, Vector4 color2, float mix)
    {
        float imix = 1f - mix;
        return new Vector4(
            color1[0] * imix + color2[0] * mix,
            color1[1] * imix + color2[1] * mix,
            color1[2] * imix + color2[2] * mix,
            color1[3] * imix + color2[3] * mix
        );
    }

    public static Color Mix(Color color1, Color color2, float mix)
    {
        float imix = 1f - mix;
        return new Color(
            (byte)(color1.R * imix + color2.R * mix),
            (byte)(color1.G * imix + color2.G * mix),
            (byte)(color1.B * imix + color2.B * mix),
            (byte)(color1.A * imix + color2.A * mix)
        );
    }
    ///<summary>
    /// Computes object time
    /// </summary>
    public static double ComputeObjectTime(double time, double start, double appearLength, double end, double disappearLength)
    {
        double timeAppear = Math.Clamp((time - start) / appearLength, 0, 1);
        double timeDisappear = Math.Clamp((end - time) / disappearLength, 0, 1);
        return timeAppear * timeDisappear;
    }

    public static double ComputeObjectTimeStart(double time, double start, double appearLength)
    {
        return Math.Clamp((time - start) / appearLength, 0, 1);
    }

    public static byte TimeToTransparency(double time)
    {
        return (byte)(255 * time);
    }

    public static float Pow2F(float x)
    {
        return x * x;
    }

    public static float EaseInOutElasticF(float x)
    {
        float c5 = (2f * MathF.PI) / 4.5f;
        return x == 0
        ? 0
        : x == 1
        ? 1
        : x < 0.5
        ? -(MathF.Pow(2, 20 * x - 10) * MathF.Sin((20 * x - 11.125f) * c5)) / 2
        : (MathF.Pow(2, -20 * x + 10) * MathF.Sin((20 * x - 11.125f) * c5)) / 2 + 1;
    }

    public static RenderTexture2D? DrawText(string s, int hPadding, int vPadding)
    {
        int size = (int)(24 * Runtime.CurrentRuntime.Scale);
        int width = size * s.Length + hPadding * 2;
        int height = size + vPadding * 2;
        Console.WriteLine($"Size: {width}x{height}");
        try
        {
            RenderTexture2D rt2d = Raylib.LoadRenderTexture(width, height);
            return rt2d;

        }
        catch
        {
            return null;
        }

        //Raylib.SetShaderValue(Runtime.CurrentRuntime.Shaders["flip"], LocationWaveScreenSize, new float[] { rt2d.Texture.Width, rt2d.Texture.Height }, ShaderUniformDataType.Vec2);
        //Raylib.BeginTextureMode(rt2d);
        //Raylib.BeginShaderMode(Runtime.CurrentRuntime.Shaders["flip"]);
        //Raylib.DrawText(s, hPadding, vPadding, size, Color.White);
        //Raylib.EndShaderMode();
        //Raylib.EndTextureMode();
    }
}
