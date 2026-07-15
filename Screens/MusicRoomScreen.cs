using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;
using System.Text.Json;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Screens;

public class MusicRoomScreen : MenuScreen
{
    private MusicInfo[] Infos;
    private int CurrentDescriptionIndex = 0;
    private TargetHandle[] Descriptions;
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
        SwitchDescription(0);
        SelectedIndex = 0;
        base.Activated();
    }
    
    public override void CreateMenu()
    {
        var font = Runtime.CurrentRuntime.Fonts["newsreader"];
        SetTitle(Runtime.CurrentRuntime.Textures["music_room.png"]);
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        string[] files = Assets.Files("Assets/Music/Descriptions");
        Infos = new MusicInfo[files.Length];
        Descriptions = new TargetHandle[files.Length];
        for (int i = 0; i < files.Length; i++)
            Infos[i] = JsonSerializer.Deserialize<MusicInfo>(File.ReadAllText(files[i])) ?? new MusicInfo();
        Infos = Infos.OrderBy(x => x.Number).ToArray();
        for (int i = 0; i < Infos.Length; i++)
        {
            Descriptions[i] = Helper.DrawText(Helper.Transliterate(Infos[i].Description), FontSize, 4, 2, 2, font, "gradient", Runtime.CurrentRuntime.ScaleF);
            MenuItems.Add(new MenuItem(Infos[i].Title, "", a => PlayMusic()));
        }
        CurrentX = (int)(Runtime.CurrentRuntime.Scale * 32);
        CurrentY = (int)(Runtime.CurrentRuntime.Scale * 64);
    }

    public override void Render()
    {
        float time = (float)GetTime();
        float s =
            Helper.EaseInOutElasticF(Helper.ComputeObjectTime(time, TimeAppear, 1f, TimeDisappear, 1f));
        CurrentY = (int)(Runtime.CurrentRuntime.Height*(1-s) + s*128*Runtime.CurrentRuntime.ScaleF);
        DrawBackground();
        DrawMenu();
        DrawTitle();
        float[] state = new float[4]; 
        var rc = Helper.GetFullSource(Descriptions[CurrentDescriptionIndex].Texture);
        for (int i = 0; i < 4; i++)
        {
            state[i] = (float)Helper.ComputeObjectTimeStart(time - (i * 0.05), 
                DescriptionSwitchTime, 0.25);
            DrawTexturePro(Descriptions[CurrentDescriptionIndex].Texture, 
                rc with { Height = rc.Height / 4, Y = rc.Height / 4 * i },
                rc with { X = 120 * Runtime.CurrentRuntime.ScaleF, 
                    Y = 360 * Runtime.CurrentRuntime.ScaleF + (i * rc.Height / 4),
                    Height = rc.Height / 4},
                Vector2.Zero, 0, 
                Rgba.White with {A = Helper.TimeToTransparency(state[i])});
        }
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
        DescriptionSwitchTime = GetTime();
        CurrentDescriptionIndex = index;
    }
}