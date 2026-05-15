using System.Collections.Frozen;
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
        dictionary["nikitos#spell1#easy"] = c =>
        {
            if ((c.GameBox.CurrentTick - c.TickStart) % 6 == 0)
            {
                c.Header[0]++;
                var rain = c.GameBox.SpawnObject(0);
                rain.X = 72+48*(c.Header[0] % 5);
                rain.Y = -16;
                rain.Speed = 2f;
                rain.FacingRotation = rain.RenderRotation = (c.Header[0] % 5 - 3) * -10;
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
            var dir = Helper.GetDirection2(obj.FloatingPoints[6]);
            obj.FloatingPoints[0x7] *= 65f / 60f;
            obj.FloatingPoints[0x6] *= 59f / 60f;
            obj.FloatingPoints[0x5] = obj.FloatingPoints[0x6];
            obj.X += dir.X * obj.Speed;
            obj.Y += dir.Y * obj.Speed;
        };
        ObjectActions = dictionary.ToFrozenDictionary();
    }
}

public delegate void RuntimeChapterReferenceAction(RuntimeChapter chapter);
public delegate void RuntimeObjectReferenceAction(RuntimeObject obj);