using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;

namespace DmitryAndDemid.Backgrounds;

/// <summary>
/// Which <see cref="StageBackground"/> plays behind which stage. <see cref="GameBox.LoadStage"/> asks here
/// every time a stage starts (the first one and every swap), so a campaign run changes scenery as it goes:
/// the approach to Drogichin on stage 1, the rainy house field on stage 2, the empty fill on stage 3, and the
/// city flight on Extra. Keyed on the stage's number (<c>Header[1]</c> — 0-based, the same value the stage
/// title and the unlock use) with Extra mode forced to the city whatever its file says.
/// </summary>
public static class StageBackgrounds
{
    public static StageBackground ForStage(FileStageInfo stage, GameType mode)
    {
        if (mode == GameType.Extra)
            return new CityFlyoverBackground();
        return stage.Header[1] switch
        {
            0 => new DrogichinFlyoverBackground(),
            1 => new HousesBackground(),
            2 => new SillyBackground(),
            3 => new CityFlyoverBackground(),   // extra1.sid played outside Extra mode (a replay, practice)
            _ => new HousesBackground(),
        };
    }
}
