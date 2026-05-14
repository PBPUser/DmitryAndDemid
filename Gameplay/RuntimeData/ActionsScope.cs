using System.Collections.Frozen;

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
        dictionary["_chapter!!!"] = (ref chapter) =>
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
        ObjectActions = dictionary.ToFrozenDictionary();
    }
}

public delegate void RuntimeChapterReferenceAction(ref RuntimeChapter chapter);
public delegate void RuntimeObjectReferenceAction(RuntimeObject obj);