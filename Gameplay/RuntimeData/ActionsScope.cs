using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Collections.Frozen;
using System.Numerics;
using DmitryAndDemid.Gameplay.Effects;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Gameplay.RuntimeData;

public static class ActionsScope
{
    public static FrozenDictionary<string, RuntimeChapterReferenceAction> ChapterActions;
    public static FrozenDictionary<string, RuntimeObjectReferenceAction> ObjectActions;

    // Yellow, brown, green-yellow — the palette for the two-toilet colour-spam spell (Hard & Max only).
    private static readonly int[] SpamColors = { 0xFFFF00, 0x8B4513, 0xADFF2F };

    /// <summary>Toilet behaviour for that spell: fires a slowly-rotating ring of bullets, cycling the palette.</summary>
    private static readonly RuntimeObjectReferenceAction ColorSpamToilet = obj =>
    {
        if (obj.Box.ChapterTick < 30 || obj.Box.ChapterTick % 20 != 0)
            return;
        const int ring = 10;
        int color = SpamColors[(obj.Box.ChapterTick / 20) % SpamColors.Length];
        float baseAngle = obj.Box.ChapterTick * 0.12f;
        for (int k = 0; k < ring; k++)
        {
            var b = obj.Box.SpawnObject(0, color);
            b.X = obj.X;
            b.Y = obj.Y;
            b.FacingRotation = b.RenderRotation = baseAngle + k * (MathF.PI * 2f / ring);
            b.Speed = 2.2f;
        }
    };

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
        // Two toilets that spew a bunch of yellow / brown / green-yellow bullets. This spell exists only on Hard
        // and Max — on Easy/Normal it spawns nothing (the card simply times out).
        dictionary["toilets#colorspam#create"] = c =>
        {
            if (c.GameBox.Difficulty < 2)
                return;
            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? 112f : 272f;
                var toilet = c.GameBox.SpawnObject(7);
                toilet.X = x;
                toilet.Y = -16;
                toilet.SetMoveToTarget(4, new Vector2(x, 96));
                toilet.UpdateAction = ColorSpamToilet;
            }
        };
        dictionary["nikitos#spell1#easy_create"] = c =>
        {
            var nPerson = c.GameBox.SpawnObject(2);
            nPerson.X = -8;
            nPerson.Y = -8;
            int diff = Math.Clamp(c.GameBox.Difficulty, 0, 3);
            nPerson.Header[0x50] = Math.Max(2, 4 - diff);   // faster bullet cadence on harder difficulties
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
            int diff = Math.Clamp(c.GameBox.Difficulty, 0, 3);
            nPerson.Header[0x50] = Math.Max(2, 4 - diff);   // faster bullet cadence on harder difficulties
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
        dictionary["MysticalToilet"] = obj =>
        {
            if (obj.Box.ChapterTick % obj.Header[0x55] == 0)
            {
                var rnd = new Random(obj.Box.CurrentTick);
                obj.SetMoveToTarget(4, new Vector2(rnd.Next(64, 320), rnd.Next(64, 128)));
            }
            obj.RenderRotation = MathF.Sin(obj.Box.ChapterTick * .125f) * .5f;
        };
        dictionary["MysticalToiletDie"] = obj =>
        {
            obj.Box.MysticalToilet = null;
            // TODO: Play toilet die sound
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
                c.Speed = 2 + Math.Clamp(obj.Box.Difficulty, 0, 3) * 0.5f;   // faster on harder difficulties
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
            obj.Velocity = MathUtil. Vector2MoveTowards(obj.Velocity, Helper.GetDirection(obj.Position, obj.Box.Player.Position), 0.01f);
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
                int diff = Math.Clamp(c.Box.Difficulty, 0, 3);
                float baseAngle = (float)(c.FloatingPoints[0x5c] + (Math.PI / 2) * (Math.Abs(c.FloatingPoints[0x5D] % 10) - 5) / 5);
                int count = 1 + diff;                       // 1..4 bullets, fanned out with difficulty
                for (int k = 0; k < count; k++)
                {
                    var d = c.Box.SpawnObject(0);
                    d.FacingRotation = d.RenderRotation = baseAngle + (k - (count - 1) / 2f) * 0.18f;
                    d.X = c.X;
                    d.Y = c.Y;
                    d.Speed = 6f + diff * 0.5f;             // and a touch faster
                }
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