using DmitryAndDemid.Common;
using static Raylib_cs.Raylib;
using DmitryAndDemid;
using DmitryAndDemid.Data;
using Raylib_cs;
using System.Numerics;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Screens;

public class GameplayScreen : Screen
{
    public GameplayScreen(ProtogonistData data, Stage stage, int difficulty)
    {
        SetBackground(Runtime.CurrentRuntime.Textures["gameplay_background.png"]);
        Game = new Game(data, stage, this, difficulty);
        Source = new Rectangle(0, 0, 384, -448);
        Dest = new Rectangle(32 * Runtime.CurrentRuntime.ScaleF, 16 * Runtime.CurrentRuntime.ScaleF, 384 * Runtime.CurrentRuntime.ScaleF, 448 * Runtime.CurrentRuntime.ScaleF);
        DialogDest = Helper.GetFullscreenSource();
        DialogSource = Helper.GetFullscreenSource();
        DialogSource.Height *= -1;
        DieShader = Runtime.CurrentRuntime.Shaders["die"];
        PauseMenu = new PauseMenu(this);
        SetShaderValue(
            DieShader, 
            GetShaderLocation(DieShader, "scale"),
            Runtime.CurrentRuntime.ScaleF,
            ShaderUniformDataType.Float
            );
        LocationDiePosition = GetShaderLocation(DieShader, "pos");
        LocationDieTime = GetShaderLocation(DieShader, "time");
        DifficultySource = new Rectangle(0, 160*difficulty, 1920, 160);
        DifficultyTargetStart = Helper.Scale(new Rectangle(152, 20, 144, 12), Runtime.CurrentRuntime.ScaleF);
        DifficultyTarget = Helper.Scale(new Rectangle(456, 24, 144, 12), Runtime.CurrentRuntime.ScaleF);

        LetterWidth = (int)(MeasureTextEx(Runtime.CurrentRuntime.Fonts["kodemono"],
            "j",
            (int)(24 * Runtime.CurrentRuntime.ScaleF),
            0).X+(int)(2 * Runtime.CurrentRuntime.ScaleF));
        
    }

    public int LetterWidth = 0;
    public PauseMenu PauseMenu;

    
    Rectangle Source;
    Rectangle Dest;
    Rectangle DialogSource;
    Rectangle DialogDest;

    private Rectangle DifficultySource;
    private Rectangle DifficultyTargetStart;
    private Rectangle DifficultyTarget;
    
    public Game? Game;

    public void Resume()
    {
        Game!.TogglePause(false);
    }

    private bool paused;
    
    public bool Paused
    {
        get => paused;
        set
        {
            if (paused == value)
                return;
            Game!.Playing = !value;
            paused = value;
            if (value)
                Runtime.CurrentRuntime.AddScreen(PauseMenu);
            else
                Runtime.CurrentRuntime.RemoveScreen(PauseMenu);
        }
    }
    
    public override void PreRender(double f)
    {
        if (IsKeyDown(KeyboardKey.Escape)  && !Game!.ForcedPause && GetTime() - MenuScreen.PreviousKeyTimestamp > MenuScreen.MenuSwitchCooldown)
        {
            MenuScreen.PreviousKeyTimestamp = GetTime();
            Paused = !Paused;
        }
        Game!.Update();
        Game!.ProcessInput();
    }

    private Shader DieShader;
    private int LocationDiePosition;
    private int LocationDieTime;
    
    public override void Render()
    {
        float time = (float)GetTime();
        DrawBackground();
        Game!.RenderGame();
        if (Game.IsDied)
        {
            SetShaderValue(DieShader, LocationDieTime, (time- Game.DiedTimestamp) / Game.DieAnimationLength, ShaderUniformDataType.Float);
            SetShaderValue(DieShader, LocationDiePosition, Game.DiePosition, ShaderUniformDataType.Vec2);
            BeginShaderMode(Runtime.CurrentRuntime.Shaders["die"]);
        }
        DrawTexturePro(Game.Background.Texture,
            Source, Dest,
            Vector2.Zero, 0, Color.White);
        DrawTexturePro(Game.Gameplay.Texture,
            Source, Dest,
            Vector2.Zero, 0, Color.White);
        if(Game.IsDied)
            EndShaderMode();
        DrawTexturePro(Game.Dialog.Texture,
            DialogSource, DialogDest,
            Vector2.Zero, 0, Color.White);
        DrawTexturePro(Runtime.CurrentRuntime.Textures["difficulties_ingame.png"],
            DifficultySource, DifficultyTarget with{ Height = (float)(Helper.ComputeObjectTimeStart(time,TimeAppear + 2f, .25f) * DifficultyTarget.Height) },
            Vector2.Zero, 0, Color.White);
        DrawTexturePro(Game.CurrentScoreTexture.Texture,
            Game.CurrentScoreSource, 
            Game.CurrentScoreTarget, 
            Vector2.Zero, 0, Color.White);
        DrawTexturePro(
            Game.UITexture.Texture,
            new Rectangle(0, Game.UITexture.Texture.Height, Game.UITexture.Texture.Width,
                -Game.UITexture.Texture.Height),
            new Rectangle(Game.UIPositionX, Game.UIPositionY, Game.UITexture.Texture.Width, Game.UITexture.Texture.Height),
            Vector2.Zero, 0, Color.White);
        
        if (time - TimeAppear > 2f)
            return;
        DrawTexturePro(Runtime.CurrentRuntime.Textures["difficulties_ingame.png"],
            DifficultySource, DifficultyTargetStart with{ Height = (float)((1-Helper.EaseInOutElasticF((float)Helper.ComputeObjectTimeStart(time,TimeAppear + 1.75f, .25f))) * DifficultyTarget.Height) },
            Vector2.Zero, 0, Color.White);
    }
    
    public override void Unload()
    {
        Runtime.CurrentRuntime.SetScreenRenderingFrom(0);
        Game = null;
    }

    public override void Created()
    {
        Game!.UpdateScoreFirstTime();
        Game!.UpdateUI();
        Runtime.CurrentRuntime.SetScreenRenderingFrom(Runtime.CurrentRuntime.GetScreenIndex(this));
        base.Created();
    }
    
    
}
