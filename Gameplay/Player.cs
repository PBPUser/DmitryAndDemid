using System.Numerics;
using System.Reflection;
using DmitryAndDemid.Data;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay;

public class Player : RuntimeObject
{
    Action<Player, bool> ShootAction;
    Action<Player, bool> BombAction;
    public ProtogonistData ProtogonistData;

    public Player(Game game, ProtogonistData data) : base(game, new Vector2(192, 400), new Vector2(32, 32), new Vector2(8), 0)
    {
        ClearProtected = true;
        ProtogonistData = data;
        CollisionEnabled = false;
        if (Runtime.CurrentRuntime.Textures.ContainsKey(data.Sprite))
            SourceTexture = Runtime.CurrentRuntime.Textures[data.Sprite];
        else
            Console.WriteLine($"Player sprite ({data.Sprite}) not found!");
        SourceRect = new Rectangle(0, 0, 32, 32);
        // FieldInfo? field = typeof(ShootBombScripts).GetField(data.WeaponScriptName);
        // if (field == null)
        //     throw new Exception();
        // ShootAction = (Action<Player, bool>)field.GetRawConstantValue()!;
        // field = typeof(ShootBombScripts).GetField(data.BombScriptName);
        // if (field == null)
        //     throw new Exception();
        // BombAction = (Action<Player, bool>)field.GetRawConstantValue()!;
    }

    public override void Update()
    {
        if (RestoreTick > Game.CurrentTick)
        {
            int j = Game.CurrentTick - RestoreTick + RestoreInvincibilityLength;
            if (j < RestoreAnimationLength)
            {
                UpdateCollisionRender(new Vector2(192, 400) + new Vector2(0, 128) * (1-((float)j/(float)RestoreInvincibilityLength)), 0);
                return;
            }
        }
        else
            CollisionEnabled = true;
        Vector2 PositionChange = Vector2.Zero;
        if (Raylib.IsKeyDown(KeyboardKey.Left))
            PositionChange.X -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.Right))
            PositionChange.X += 1;
        if (Raylib.IsKeyDown(KeyboardKey.Up))
            PositionChange.Y -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.Down))
            PositionChange.Y += 1;
        UpdateCollisionRender(PositionTo + PositionChange, 0);
    }

    private const int RestoreInvincibilityLength = 300;
    private const int RestoreAnimationLength = 60;
    private int RestoreTick = 0;

    public void Die()
    {
        Game.SetDied();
        CollisionEnabled = false;
        RestoreTick = Game.CurrentTick + RestoreInvincibilityLength;
    }
}
