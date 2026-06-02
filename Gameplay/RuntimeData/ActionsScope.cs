using System.Collections.Frozen;
using System.Numerics;
using DmitryAndDemid.Gameplay.Effects;
using DmitryAndDemid.Utils;
using Raylib_cs;

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
            nPerson.X = -8;
            nPerson.Y = -8;
            nPerson.Header[0x50] = 4;
            nPerson.Header[0x51] = 120;
            nPerson.Header[0x5B] = 1;
            var rnd = new Random((int)(c.GameBox.Player.X + c.GameBox.Player.Y));
            var pos = new Vector2(rnd.Next(32, 352), rnd.Next(48, 96));
            nPerson.SetMoveToTarget(2, pos);
            var direction = Helper.FindAngle(pos, new Vector2(c.GameBox.Player.X, c.GameBox.Player.Y));
            nPerson.FloatingPoints[0x5C] = direction;
        };
        dictionary["nikitos#spell2#easy_create"] = c =>
        {
            var nPerson = c.GameBox.SpawnObject(3);
            nPerson.SetMoveToTarget(2, new Vector2(192, 80));
            nPerson.Header[0x50] = 4;
            nPerson.Header[0x51] = 120;
            nPerson.Header[0x5B] = 1;
        };
        dictionary["nikitos#spell2#easy"] = c =>
        {

        };
        dictionary["nikitos#spell2#easy"] = c =>
        {
            if (c.GameBox.CurrentTick + c.GameBox.TickOffset - c.TickStart == 30)
            {
                var toilet1 = c.GameBox.SpawnObject(7);
                var toilet2 = c.GameBox.SpawnObject(7);
                toilet1.X = 64;
                toilet2.X = 320;
                toilet1.Y = toilet2.Y = -16;
                toilet1.SetMoveToTarget(4, new Vector2(64, 96));
                toilet2.SetMoveToTarget(4, new Vector2(320, 96));
            }
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
            var d = Helper.GetDirection(obj.FloatingPoints[0x6]);
            obj.X += obj.Speed * d.X;
            obj.Y += obj.Speed * d.Y;
        };
        dictionary["toilet#spell2#easy"] = obj =>
        {
            if (obj.Box.ChapterTick % 120 == 7)
            {
                var c = obj.Box.SpawnObject(6);
                c.X = obj.X;
                c.Y = obj.Y;
                c.Speed = 2;
                c.FacingRotation = c.RenderRotation = MathF.PI / 2;
                c.Velocity = new Vector2(0, -1);
                obj.SetMoveToTarget(4, new Vector2(obj.X, 128));
            }
            else if (obj.Box.ChapterTick % 120 == 15)
            {
                obj.SetMoveToTarget(4, new Vector2(obj.X, 96));
            }
        };
        dictionary["toilet_bullet#spell2#easy"] = obj =>
        {
            obj.Velocity = Raymath. Vector2MoveTowards(obj.Velocity, Helper.GetDirection(obj.Position, obj.Box.Player.Position), 0.01f);
            obj.Position += obj.Velocity * obj.Speed;
            obj.RenderRotation = Helper.FindAngle(Vector2.Zero, obj.Velocity);
        };
        dictionary["DirectionShoot"] = obj =>
        {
            
        };
        dictionary["nikitos#spell1"] = c =>
        {
            var time = c.Box.CurrentTick - c.CreatedAt + c.Header[0x5A];
            if (time % c.Header[0x50] == 0 && time > 0)
            {
                var angle = Helper.FindAngle(c.Position, new Vector2(c.Box.Player.X, c.Box.Player.Y));
                var d = c.Box.SpawnObject(0);
                d.FacingRotation = d.RenderRotation =
                    (float)(c.FloatingPoints[0x5c] + (Math.PI / 2) * (Math.Abs(c.FloatingPoints[0x5D] % 10) - 5) / 5);
                d.X = c.X;
                d.Y = c.Y;
                d.Speed = 6f;
                c.FloatingPoints[0x5D]++;
            }

            if (time % c.Header[0x51] == 0 && time > 0)
            {
                
            }

            if (time % 300 == 250)
            {
                var rnd = new Random((int)(c.Box.Player.X + c.Box.Player.Y + c.Box.CurrentTick));
                var pos = new Vector2(rnd.Next(32, 352), rnd.Next(48, 96));
                c.SetMoveToTarget(3, pos);
            }
            if (time % 300 == 0)
            {
                c.Header[0x5B]++;
                c.Header[0x5A] += 360;
                c.Box.AddScreenEffect(new StrengthScreenEffect(c.Box, c.Position, 50, c.Box.GetTime(), c.Box.GetTime()+1, 0x00FF34, 0x00EE69));
                Helper.PlaySound(Runtime.CurrentRuntime.Sounds["boss-appear"]);
                var direction = Helper.FindAngle(c.Position, new Vector2(c.Box.Player.X, c.Box.Player.Y));
                c.FloatingPoints[0x5C] = direction;
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