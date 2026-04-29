using System.Numerics;
using System.Text.Json;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Screens;

public class MusicRoomScreen : MenuScreen
{
    private MusicInfo[] Infos;
    private int CurrentDescriptionIndex = 0;
    private RenderTexture2D[] Descriptions;
    public int FontSize;
    
    public MusicRoomScreen()
    {
        FontSize = (int)(14 * Runtime.CurrentRuntime.ScaleF);
    }

    public override void Exiting()
    {
        base.Exiting();
    }

    public override void Activated()
    {
        
        base.Activated();
    }
    
    public override void CreateMenu()
    {
        var font = Runtime.CurrentRuntime.Fonts["newsreader"];
        SetTitle(Runtime.CurrentRuntime.Textures["music_room.png"]);
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        string[] files = Directory.GetFiles("Assets/Music/Descriptions");
        Infos = new MusicInfo[files.Length];
        Descriptions = new RenderTexture2D[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            Infos[i] = JsonSerializer.Deserialize<MusicInfo>(File.ReadAllText(files[i]));
            Descriptions[i] = Helper.DrawText(Helper.Transliterate(Infos[i].Description), FontSize, 2, 2, 2, font, "gradient", Runtime.CurrentRuntime.ScaleF);
            Menu[Infos[i].Title] = (j, a) => PlayMusic();
        }
        CurrentX = (int)(Runtime.CurrentRuntime.Scale * 32);
        CurrentY = (int)(Runtime.CurrentRuntime.Scale * 256);
    }

    public override void Render()
    {
        float time = (float)Raylib.GetTime();
        CurrentY = (int)(Runtime.CurrentRuntime.Height*(1 - Helper.EaseInOutElasticF((float)(Helper.ComputeObjectTime(time, TimeAppear, 1f, TimeDisappear, 1f)*0.5))));
        DrawBackground();
        DrawMenu();
        DrawTitle();
        var rc = Helper.GetFullSource(Descriptions[CurrentDescriptionIndex].Texture);
        Raylib.DrawTexturePro(Descriptions[CurrentDescriptionIndex].Texture, 
            rc,
            rc with { X = 120 * Runtime.CurrentRuntime.ScaleF, Y = 360 * Runtime.CurrentRuntime.ScaleF  },
            Vector2.Zero, 0, Color.White);
    }

    void PlayMusic()
    {
        SwitchDescription(SelectedIndex);
    }

    private double DescriptionSwitchTime = 0;
    
    void SwitchDescription(int index)
    {
        if (index >= Infos.Length)
            return;
        DescriptionSwitchTime = Raylib.GetTime();
        CurrentDescriptionIndex = index;
    }
}