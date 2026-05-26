using System.Collections.Frozen;
using System.Numerics;
using DmitryAndDemid.Gameplay.Effects;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Gameplay.RuntimeData;

public static class ActionsScope
{
    public static FrozenDictionary<string, RuntimeChapterReferenceAction> ChapterActions;
    public static FrozenDictionary<string, RuntimeObjectReferenceAction> ObjectActions;

    static ActionsScope()
    {
        RebuildObjectActionsList();
        RebuildChapterActionsList();
    }

    public static void RebuildChapterActionsList()
    {
        var dictionary = new Dictionary<string, RuntimeChapterReferenceAction>();
        dictionary["_chapter!!!"] = (chapter) =>
        {

        };
        dictionary["nikitos#spell1#easy_create"] = c =>
        {
            var nPerson = c.GameBox.SpawnObject(2);
            nPerson.X = 192;
            nPerson.Y = 192;
            nPerson.Header[0x50] = 15;
            nPerson.Header[0x51] = 120;
            nPerson.Header[0x5B] = 1;
        };
        dictionary["nikitos#spell2#easy_create"] = c =>
        {
            var nPerson = c.GameBox.SpawnObject(3);
            nPerson.X = 192;
            nPerson.Y = 192;
            nPerson.Header[0x50] = 15;
            nPerson.Header[0x51] = 120;
            nPerson.Header[0x5B] = 1;
        };
        dictionary["nikitos#spell1#easy"] = c =>
        {
            //if ((c.GameBox.CurrentTick - c.TickStart) % 3 == 0)
            //{
            //    c.Header[0]++;
            //    var rain = c.GameBox.SpawnObject(0);
            //    rain.X = 36+56*(c.Header[0] % 5);
            //    rain.Y = -16;
            //    rain.Speed = MathF.Abs(MathF.Sin((c.GameBox.CurrentTick - c.TickStart)));
            //    rain.FacingRotation = rain.RenderRotation = (c.Header[0] % 5 - 3) * -10;
            //}
            // if ((c.GameBox.CurrentTick - c.TickStart) % 15 == 0)
            // {
            //     var obj = c.GameBox.SpawnObject(1);
            //     obj.X = -16;
            //     obj.Y = 64;
            //     obj.FacingRotation = -MathF.PI / 2;
            //     obj.Speed = 2f;
            // }
        };
        dictionary["nikitos#spell2#easy"] = c =>
        {
            
        };
        ChapterActions = dictionary.ToFrozenDictionary();
    }
    
    public static void RebuildObjectActionsList()
    {
        var dictionary = new Dictionary<string, RuntimeObjectReferenceAction>();
        dictionary["__object!!!"] = (robj) =>
        {

        };
        dictionary["AkobShoot"] = (obj) =>
        {
            obj.Y -= obj.Speed;
        };
        dictionary["RainShoot"] = obj =>
        {
            var dir = Helper.GetDirection2(obj.FloatingPoints[6] * 180 / 3.14f);
            obj.FloatingPoints[0x7] *= 61f / 60f;
            obj.FloatingPoints[0x6] *= 45f / 60f;
            obj.FloatingPoints[0x5] = obj.FloatingPoints[0x6];
            obj.X += dir.X * obj.Speed;
            obj.Y += dir.Y * obj.Speed;
        };
        dictionary["MoveLinearByDirection"] = obj =>
        {
            var d = Helper.GetDirection2(obj.FloatingPoints[0x6]);
            obj.X += obj.Speed * d.X;
            obj.Y += obj.Speed * d.Y;
        };
        dictionary["nikitos#spell1"] = c =>
        {
            var time = c.Box.CurrentTick - c.CreatedAt + c.Header[0x5A];
            if (time % c.Header[0x50] == 0 && time > 0)
            {
                var angle = Helper.FindAngle(c.Position, new Vector2(c.Box.Player.X, c.Box.Player.Y));
                for (int i = 0; i < 3; i++)
                {
                    var d = c.Box.SpawnObject(0);
                    d.FacingRotation = d.RenderRotation = -2.5f + 2.5f * i;
                    d.X = c.X;
                    d.Y = c.Y;
                    d.Speed = 2f;
                }
            }

            if (time % c.Header[0x51] == 0 && time > 0)
            {
                
            }

            if (time == 300)
            {
                c.Header[0x5B]++;
                c.Header[0x5A] += 360;
                c.Box.AddScreenEffect(new StrengthScreenEffect(c.Box, c.Position, 50, c.Box.GetTime(), c.Box.GetTime()+1, 0x00FF34, 0x00EE69));
                Helper.PlaySound(Runtime.CurrentRuntime.Sounds["boss-appear"]);
            }
        };
        dictionary["nikitos#spell2"] = c =>
        {
            
        };
        ObjectActions = dictionary.ToFrozenDictionary();
    }
}

public delegate void RuntimeChapterReferenceAction(RuntimeChapter chapter);
public delegate void RuntimeObjectReferenceAction(RuntimeObject obj);