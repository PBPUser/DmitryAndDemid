using System.Linq;
using System.Xml;

namespace DmitryAndDemid.Data;

public class Chapter : StageElement
{
    public Chapter(Game g, ChapterInfo info)
    {
        ChapterLength = info.ChapterLength;
        Index = info.Index;
        Bullets = info.BulletSpawnInfos.Select(x => new Bullet(g, x)).OrderBy(x => x.SpawnTick).ToArray();
    }

    public int ChapterLength;
    public double ChapterStartedAt = 0;
    public Bullet[] Bullets;
}
