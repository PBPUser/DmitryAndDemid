using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using DmitryAndDemid.Gameplay;
using Pango;
using Raylib_cs;
using static Raylib_cs.Raylib;
using Color = Raylib_cs.Color;
using Font = Raylib_cs.Font;
using Rectangle = Raylib_cs.Rectangle;

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
        
        LocationRenderSelectionHeight = Raylib.GetShaderLocation(Runtime.CurrentRuntime.Shaders["selection"], "height");
        LocationRenderSelectionScreenSize = Raylib.GetShaderLocation(Runtime.CurrentRuntime.Shaders["selection"], "screenSize");
        
        LocationContrastOpacity = GetShaderLocation(Runtime.CurrentRuntime.Shaders["contrast"], "opacity");
        LocationContrastLevel = GetShaderLocation(Runtime.CurrentRuntime.Shaders["contrast"], "contrastLevel");

        LocationRotateYaw = GetShaderLocation(Runtime.CurrentRuntime.Shaders["rotate"], "yaw");
        LocationRotatePitch = GetShaderLocation(Runtime.CurrentRuntime.Shaders["rotate"], "pitch");
        LocationRotateRoll = GetShaderLocation(Runtime.CurrentRuntime.Shaders["rotate"], "roll");
        LocationRotateFocal = GetShaderLocation(Runtime.CurrentRuntime.Shaders["rotate"], "focal");

        LocationDisappearShootPosition = GetShaderLocation(Runtime.CurrentRuntime.Shaders["disappear_shoot"], "pos");
        LocationDisappearShootTime = GetShaderLocation(Runtime.CurrentRuntime.Shaders["disappear_shoot"], "u_time");
        
        LocationShadowDepth = GetShaderLocation(Runtime.CurrentRuntime.Shaders["shadow"], "depth");
        LocationShadowResolution = GetShaderLocation(Runtime.CurrentRuntime.Shaders["shadow"], "res");

        LocationGradientBorderWidth = GetShaderLocation(Runtime.CurrentRuntime.Shaders["gradient"], "border_width");
        LocationGradientResoulution = GetShaderLocation(Runtime.CurrentRuntime.Shaders["gradient"], "res");

        PizzaSource = new Rectangle(0, 0, Runtime.CurrentRuntime.Textures["pizza.png"].Width, Runtime.CurrentRuntime.Textures["pizza.png"].Height);
    }

    static Rectangle PizzaSource;

    private static int LocationGradientBorderWidth;
    private static int LocationGradientResoulution;

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

    private static int LocationContrastLevel;
    private static int LocationContrastOpacity;

    static int LocationRotateRoll;
    static int LocationRotatePitch;
    static int LocationRotateYaw;
    static int LocationRotateFocal;

    private static int LocationShadowDepth;
    private static int LocationShadowResolution;

    public static void BeginRotateShader(float roll, float pitch, float yaw, float focal)
    {
        SetShaderValue(Runtime.CurrentRuntime.Shaders["rotate"], LocationRotateFocal, focal, ShaderUniformDataType.Float);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["rotate"], LocationRotateRoll, roll, ShaderUniformDataType.Float);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["rotate"], LocationRotatePitch, pitch, ShaderUniformDataType.Float);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["rotate"], LocationRotateYaw, yaw, ShaderUniformDataType.Float);
        BeginShaderMode(Runtime.CurrentRuntime.Shaders["rotate"]);
    }
    
    public static void BeginContrastShader(float contrastLevel, float opacity)
    {
        SetShaderValue(Runtime.CurrentRuntime.Shaders["contrast"], LocationContrastOpacity, opacity, ShaderUniformDataType.Float);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["contrast"], LocationContrastLevel, contrastLevel, ShaderUniformDataType.Float);
        BeginShaderMode(Runtime.CurrentRuntime.Shaders["contrast"]);
    }
    
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

    public static RenderTexture2D DrawDialog(string text, float angle)
    {
        var tx = DrawText(text, 16, 4, 4, 2, GetFontDefault(), Color.Black, "shadow");
        var vx = RenderTextureInCloud(tx.Texture, 3f, angle);
        UnloadRenderTexture(tx);
        return vx;
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

    public static float Mix(float f1, float f2, float mix)
    {
        return f1 * (1 - mix) + f2 * mix;
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

    static float Clamp(float value, float min, float max)
    {
        return MathF.Max(MathF.Min(value, max), min);
    }

    public static float ComputeObjectTime(float time, float start, float appearLength, float end, float disappearLength)
    {
        float timeAppear = Clamp((time - start) / appearLength, 0, 1);
        float timeDisappear = Clamp((end - time) / disappearLength, 0, 1);
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

    public static RenderTexture2D DrawTextScaled(string s, int fontSize, int hPadding, int vPadding, int spacing, Font font, string shader = "shadow") => DrawText(s, 
        (int)(fontSize*Runtime.CurrentRuntime.Scale), 
        (int)(hPadding*Runtime.CurrentRuntime.Scale), 
        (int)(vPadding*Runtime.CurrentRuntime.Scale), 
        (int)(spacing*Runtime.CurrentRuntime.Scale),
        font, 
        Color.White,
        shader,
        Runtime.CurrentRuntime.ScaleF);
    public static RenderTexture2D DrawText(string s, int fontSize, int hPadding, int vPadding, int spacing, Font font, string shader = "shadow", float scale = 1f) => 
        DrawText(s, fontSize, hPadding, vPadding, spacing, font, Color.White, shader, scale);

    public static void DrawTextOnRenderTexture(ref RenderTexture2D texture, string s, int fontSize, int hPadding, int vPadding, int spacing, Font font, Color color, string shader, float scale = 1f)
    {
        if(IsRenderTextureValid(texture))
            UnloadRenderTexture(texture);
        int width = s.Length * fontSize + hPadding * 2;
        int height = fontSize + vPadding * 2;
        RenderTexture2D temp = Raylib.LoadRenderTexture(width, height);
        texture = Raylib.LoadRenderTexture(width, height);
        BeginTextureMode(temp);
        DrawTextEx(font, s, new Vector2(hPadding, vPadding), fontSize, spacing, color);
        EndTextureMode();
        switch (shader)
        {
            case "shadow":
                SetShaderValue(Runtime.CurrentRuntime.Shaders["shadow"], LocationShadowDepth, 4f, ShaderUniformDataType.Float);
                SetShaderValue(Runtime.CurrentRuntime.Shaders["shadow"], LocationShadowResolution, new float[] { width, height }, ShaderUniformDataType.Vec2);
                break;
            case "gradient":
                SetShaderValue(Runtime.CurrentRuntime.Shaders["gradient"], LocationGradientBorderWidth, 2f, ShaderUniformDataType.Float);
                SetShaderValue(Runtime.CurrentRuntime.Shaders["gradient"], LocationGradientResoulution, new Vector2(width,height), ShaderUniformDataType.Vec2);
                break;
        }
        BeginTextureMode(texture);
        BeginShaderMode(Runtime.CurrentRuntime.Shaders[shader]);
        DrawTexture(temp.Texture, 0, 0, Color.White);
        EndShaderMode();
        EndTextureMode();
        UnloadRenderTexture(temp);
    }
    
    private static RenderTexture2D ScoreDigits;
    public static Vector2 ScoreDigitSize;
    
    public static RenderTexture2D DrawText(string s, int fontSize, int hPadding, int vPadding, int spacing, Font font, Color color, string shader, float scale = 1f)
    {
        RenderTexture2D texture = new RenderTexture2D();
        DrawTextOnRenderTexture(ref texture, s, fontSize, hPadding, vPadding, spacing, font, color, shader, scale);
        return texture;
    }

    public static Rectangle GetFullSource(Texture2D t) => new Rectangle(0, 0, t.Width, t.Height);
    public static Rectangle GetFullSourceRenderTexture(RenderTexture2D rt2d) => new Rectangle(0, 0, rt2d.Texture.Width, -rt2d.Texture.Height);

    public static Rectangle GetFullscreenSource() => new Rectangle(0, 0, Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height);

    public static Rectangle ScaleByHeight(float middle, float y, Vector2 size, float newHeight)
    {
        float mp = newHeight / size.Y;
        return new Rectangle(middle, y, mp * size.X, newHeight);
    }

    public static Rectangle Scale(Rectangle rc, double scale)
    {
        return Scale(rc, (float)scale);
    }

    public static Rectangle Scale(Rectangle rc, float scale)
    {
        return new Rectangle(rc.Position * scale, rc.Size * scale);
    }

    private static int LocationRenderSelectionScreenSize;
    private static int LocationRenderSelectionHeight;
    
    public static Texture2D RenderSelectionBackground(int width, int height, int vPadding)
    {
        int h = height + vPadding * 2;
        RenderTexture2D texture = LoadRenderTexture(width, h);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["selection"], LocationRenderSelectionHeight, (float)height, ShaderUniformDataType.Float);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["selection"], LocationRenderSelectionScreenSize, new float[] { 200f, 200f }, ShaderUniformDataType.Vec2);
        BeginTextureMode(texture);
        BeginShaderMode(Runtime.CurrentRuntime.Shaders["selection"]);
        DrawRectanglePro(new Rectangle(0,0,width,height), Vector2.Zero, 0, Color.White);
        EndShaderMode();
        EndTextureMode();
        return texture.Texture;
    }

    public static RenderTexture2D FillTextureWithColor(Color color, int w, int h)
    {
        var texture = LoadRenderTexture(w, h);
        BeginTextureMode(texture);
        DrawRectangle(0,0,w,h,color);
        EndTextureMode();
        return texture;
    }

    public static float FindAngle(Vector2 v1, Vector2 v2)
    {
        var angle = MathF.Atan2(v2.Y, v2.X)-MathF.Atan2(v1.Y, v1.X);
        return angle;
    }

    public static float FindAngleDegress(Vector2 v1, Vector2 v2)
    {
        return 180 / MathF.PI * FindAngle(v1, v2);
    }

    public static Vector2 GetDirection(Vector2 v1, Vector2 v2)
    {
        var angle = FindAngle(Vector2.Zero, v2-v1);
        return GetDirection(angle);
    }
    
    public static Vector2 GetDirection(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));

    private static int LocationDisappearShootPosition;
    private static int LocationDisappearShootTime;
    
    public static void DrawDeathPoints(List<RemovedBullet> objects, string shader)
    {
        float time = (float)GetTime();
        foreach (var obj in objects)
        {
            SetShaderValue(Runtime.CurrentRuntime.Shaders[shader], LocationDisappearShootTime, time - obj.Time, ShaderUniformDataType.Float);
            SetShaderValue(Runtime.CurrentRuntime.Shaders[shader], LocationDisappearShootPosition, obj.Position, ShaderUniformDataType.Vec2);
            BeginShaderMode(Runtime.CurrentRuntime.Shaders[shader]);
            DrawRectangle(0,0,384,448,Color.White);
            EndShaderMode();
        }
    }

    public static Vector2 Half = Vector2.One / 2;

    public static bool IsInArea(Vector2 xPositionTo, Vector2 areaStart, Vector2 areaEnd)
    {
        return 
            areaStart.X < xPositionTo.X && areaStart.Y < xPositionTo.Y &&
            areaEnd.X > xPositionTo.X && areaEnd.Y > xPositionTo.Y;
    }

    public static bool IsCollied(Rectangle rc1, Rectangle rc2)
    {
        #if DEBUG
        var vecDistance = Raymath.Vector2Distance(rc1.Center, rc2.Center);
        var wDistance = (rc1.Width + rc2.Width) / 2;
        return vecDistance < wDistance;
#else
        return Raymath.Vector2Distance(rc1.Center, rc2.Center) < (rc1.Width + rc2.Width) / 2;
#endif
    }

    
    public static double BossAppearCurve(double x, double pow)
    {
        return (Math.Pow(x/2 - 1, pow) + 1) / 2;
    } 
    
    public static float BossAppearCurveF(float x, float pow)
    {
        return (MathF.Pow(x/2 - 1, pow) + 1) / 2;
    }
    
    public static void PlaySound(Sound sound)
    {
        var soundCopy = LoadSoundAlias(sound);
        SetSoundVolume(soundCopy, Runtime.CurrentRuntime.SFXVolume);
        Raylib.PlaySound(soundCopy);
        SoundAlieases[AliasIndex] = sound;
        AliasIndex++;
        if (RequiresUnloading)
        {
            UnloadSoundAlias(SoundAlieases[AliasIndex-1]);
        }

        if (AliasIndex < AliasCount)
            return;
        RequiresUnloading = true;
        AliasIndex = 0;
    }

    private const int AliasCount = 4096;
    private static int AliasIndex = 0;
    private static bool RequiresUnloading = false;
    private static Sound[] SoundAlieases = new Sound[4096];
    
    static Dictionary<string, string> TransliterationDictionary = 
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText("Assets/Data/cyrilic-transliteration-table.json"));
    static Dictionary<string, string> TranslationDictionary = 
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText("Assets/Data/translation.json"));

    public static string Translate(string i18n)
    {
        if(TranslationDictionary.ContainsKey(i18n))
            return Transliterate(TranslationDictionary[i18n]);
        return Transliterate(i18n);
    }
    
    public static string Transliterate(string text)
    {
        string final = "";
        string[] chars;
        foreach (var c in text)
        {
            if (TransliterationDictionary.ContainsKey(c.ToString()))
            {
                chars = TransliterationDictionary[c.ToString()].Split(";;");
                final += chars[new Random().Next(chars.Length - 1)];
            }
            else
                final += c;
        }
        return final;
    }

    public static void UpdatePlayingMusic()
    {
        throw new NotImplementedException();
    }
}
