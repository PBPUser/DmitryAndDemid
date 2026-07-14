using DmitryAndDemid.Rendering;
namespace DmitryAndDemid.Data;

public enum EnemyBitmask
{
    None = 0x0000,
    IsBullet = 0x0001,
    IsGroupChild = 0x0002,
    IsGroupParent = 0x0004,
    UseCreateScript = 0x0008,
    UseRemoveScript = 0x0010,
    ClearProtected = 0x0020,
    PlayerDanger = 0x0040,
    UseBadDropScenario = 0x0080,
    DropWhenCleared = 0x0100,
    IsBossOrIsGrazed = 0x0200,
    UseDieScriptOrDangerousForEnemy = 0x0400,
    ApplyShader = 0x0800,
    IsDied = 0x1000,
    UseRenderRotation = 0x2000,
    UseUpdateScript = 0x4000
}