using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Gameplay.Effects;
using DmitryAndDemid.Gameplay.GameplayOverlays;
using DmitryAndDemid.Gameplay.RuntimeData;
using DmitryAndDemid.Utils;
using Microsoft.CodeAnalysis.Scripting;

namespace DmitryAndDemid.Gameplay;

public class RuntimeObject
{
    public const int
        FlagIsBullet = 0x0001,
        FlagIsGroupChild = 0x0002,
        FlagIsGroupParent = 0x0004,
        FlagOverrideColor = 0x0004,
        FlagUseCreateScript = 0x0008,
        FlagUseRemoveScript = 0x0010,
        FlagClearProtected = 0x0020,
        FlagDangerousRelatedToPlayer = 0x0040,
        FlagUseBadDropScenario = 0x0080,
        FlagDropWhenCleared = 0x0100,
        FlagIsBoss = 0x0200,
        FlagIsGrazed = 0x0201,
        FlagUseDieScript = 0x0400,
        FlagDangerousRelatedToEnemy = 0x0400,
        FlagIsCollectableBullet = 0x100001,
        FlagIsUsed = 0x0800,
        FlagApplyShader = 0x8000,
        FlagIsDied = 0x1000,
        FlagUseRenderRotation = 0x2000,
        FlagUseUpdateScript = 0x4000,
        FlagIsFinalBossChapter = 0x100000,
        FlagIsCollectable = 0x10000, 
        FlagIsMovingToTarget = 0x20000,
        FlagInvincible = 0x40000,
        FlagIsLaser = 0x80000,
        // Out-of-bounds "remove-proof": an object with this set is NOT culled when it leaves the playfield
        // (like lasers). For things that legitimately live off-screen — e.g. a bullet formation that spawns
        // outside the box and moves in. Bit 21, unused by any other flag / object kind.
        FlagPersistOffscreen = 0x200000,
        // A ray: a laser-like beam that widens into a cone from a large emitting dot and fades out along its
        // finite length. Shares the laser fields/phases but adds RaySpread, a cone-collision test, and a gradient
        // render (see MakeRay / GameBox.DrawRay). Bit 22, unused by any other flag / object kind.
        FlagIsRay = 0x400000,
        // A collectable the mystical toilet has spat back out: instead of drifting down it homes in on the
        // player from ANY distance (see UpdateCollectable), so the returned hoard always finds its way back
        // wherever the player happens to be standing. Bit 23, unused by any other flag / object kind.
        FlagHomingCollectable = 0x800000;

    public static FileEntityInfo[] CollectableFEIs = new FileEntityInfo[8];
    public static FileEntityInfo MagicalToilet;

    static RuntimeObject()
    {
        for (int i = 0; i < 8; i++)
        {
            CollectableFEIs[i] = new FileEntityInfo();
            CollectableFEIs[i].Header[0] = 0b0000_0000;
            CollectableFEIs[i].Header[0] |= 0x10000;
            CollectableFEIs[i].Header[4] = i;
            CollectableFEIs[i].Header[0] &= ~FlagUseUpdateScript;
        }
        CollectableFEIs[0].Visual = "power";
        CollectableFEIs[7].Visual = "bomb_piece";   // Type 7 = bomb piece (dropped by the mystical toilet)
        MagicalToilet = new FileEntityInfo()
        {
            UpdateScript = "MysticalToilet",
            DieScript = "MysticalToiletDie",
            Visual = "toilet"
        };
        MagicalToilet.UseDieScript = true;
    }
    
    private Rect Source = new();
    private Rect Target = new();
    public Vector2 Origin = new();
    /// <summary>Extra render-time scale about the object's centre, 1 = normal. Used for spawn/entrance
    /// animations (e.g. the Nikitab boss budging in) without touching the collision/target size.</summary>
    public float EntranceScale = 1f;
    /// <summary>Render-time opacity multiplier, 1 = fully opaque. Used for fade-in/out of entities (e.g. the
    /// Nikita Bukin boss and his pizza mount riding in semi-transparent) without affecting the sim. Applied as
    /// the draw tint's alpha; bullets leave this at 1 and control opacity through their shader instead.</summary>
    public float RenderAlpha = 1f;

    /// <summary>When true the object is exempt from the out-of-bounds cull (see GameBox), so it can live and move
    /// off-screen. Backed by <see cref="FlagPersistOffscreen"/> in <see cref="Header"/>[0].</summary>
    public bool PersistOffscreen
    {
        get => (Header[0] & FlagPersistOffscreen) == FlagPersistOffscreen;
        set { if (value) Header[0] |= FlagPersistOffscreen; else Header[0] &= ~FlagPersistOffscreen; }
    }

    public int[] Header = new int[128];
    public float[] FloatingPoints = new float[128];
    public TextureHandle Texture;
    public GameBox Box;
    public RuntimeObjectReferenceAction? CreateAction;
    public RuntimeObjectReferenceAction? UpdateAction;
    public RuntimeObjectReferenceAction? DieAction;
    public RuntimeObjectReferenceAction? RemoveAction;
    public ShaderHandle Shader;
    public Vector2 TexturePosition, TextureSize, TotalTextureSize;
    public GameplayScreenEffect? BackgroundDistortionEffect;
    public BossCircleScreenEffect? BossCircleEffect;

    /// <summary>
    /// Collectables the mystical toilet has swallowed (see <see cref="GameBox.SwallowIntoToilet"/>), held until
    /// it dies and its die script spits them back out in a ring. The item INSTANCES are kept rather than their
    /// types, so each comes back as exactly the item it was — only collectable types 0 and 7 have a visual
    /// preset wired up, so respawning them from <see cref="CollectableFEIs"/> would not survive the round trip.
    /// Null on every object but the toilet.
    /// </summary>
    public List<RuntimeObject>? SwallowedItems;

    RuntimeObject()
    {
    }
    
    public static RuntimeObject LoadFromFile(FileEntityInfo info, GameBox box)
    {
        RuntimeObject entity = new RuntimeObject();
        entity.Box = box;
        Array.Copy(info.Header, entity.Header, info.Header.Length);
        Array.Copy(info.FloatingPoints, entity.FloatingPoints, info.FloatingPoints.Length);
        entity.Header[0] |= FlagUseUpdateScript;
        if ((entity.Header[0] & FlagUseUpdateScript) == FlagUseUpdateScript && (entity.Header[0] & FlagIsCollectable) != FlagIsCollectable)
            entity.UpdateAction = ActionsScope.ObjectActions[info.UpdateScript];
        entity.DieAction = info.UseDieScript ? ActionsScope.ObjectActions[info.DieScript] : null;
        if ((entity.Header[0] & FlagIsCollectable) == FlagIsCollectable)
        {
            BulletRenderingInfo bulletRenderInfo = Runtime.CurrentRuntime.BulletVisualPresets[info.Visual];
            var shader = Runtime.CurrentRuntime.Shaders["basic_bullet_shader"];
            var spPos = bulletRenderInfo.GetSpritePosition(info.Header[4]);
            entity.Source = new(
                spPos - new Vector2(32),
                bulletRenderInfo.SourceSize + new Vector2(64)
            );
            entity.FloatingPoints[0x13] = bulletRenderInfo.Collision;
            entity.Target.Size = entity.Source.Size;
            entity.Origin = entity.Source.Size / 2;
            entity.Header[0] |= FlagApplyShader;
            entity.Shader = shader;
            entity.Texture = bulletRenderInfo.GetTexture(info.Header[4]);
            entity.Header[0x40] = GetShaderLocation(shader, "created_at");
            entity.Header[0x41] = GetShaderLocation(shader, "time");
            entity.Header[0x42] = GetShaderLocation(shader, "position");
            entity.Header[0x43] = GetShaderLocation(shader, "resolution");
            entity.Header[0x44] = GetShaderLocation(shader, "output_resolution");
            entity.Header[0x45] = GetShaderLocation(shader, "opacity");
            entity.TexturePosition = spPos;
            entity.TextureSize = bulletRenderInfo.SourceSize;
            entity.TotalTextureSize = Helper.GetSize(entity.Texture);
            entity.Header[0] |= FlagIsCollectable;
        }
        else if ((entity.Header[0] & FlagIsBullet) == FlagIsBullet)
        {
            BulletRenderingInfo bulletRenderInfo = Runtime.CurrentRuntime.BulletVisualPresets[info.Visual];
            var shader = Runtime.CurrentRuntime.Shaders[bulletRenderInfo.Effect == "" ? "basic_bullet_shader" : bulletRenderInfo.Effect];
            entity.Texture = bulletRenderInfo.GetTexture(info.Header[4]);
            var spPos = bulletRenderInfo.GetSpritePosition(info.Header[4]);
            entity.Source = new(
                spPos - new Vector2(32),
                bulletRenderInfo.SourceSize + new Vector2(64)
            );
            entity.Target.Size = entity.Source.Size;
            entity.Origin = entity.Source.Size / 2;
            entity.Header[0] |= FlagApplyShader;
            entity.Shader = shader;
            entity.FloatingPoints[0x13] = bulletRenderInfo.Collision;
            if (bulletRenderInfo.Effect == "")
            {
                entity.Header[0x40] = GetShaderLocation(shader, "created_at");
                entity.Header[0x41] = GetShaderLocation(shader, "time");
                entity.Header[0x42] = GetShaderLocation(shader, "position");
                entity.Header[0x43] = GetShaderLocation(shader, "resolution");
                entity.Header[0x44] = GetShaderLocation(shader, "output_resolution");
                entity.Header[0x45] = GetShaderLocation(shader, "opacity");
            }
            else
            {
                entity.Header[0x40] = bulletRenderInfo.LocFXCreatedAt;
                entity.Header[0x41] = bulletRenderInfo.LocFXTime;
                entity.Header[0x42] = bulletRenderInfo.LocFXPosition;
                entity.Header[0x43] = bulletRenderInfo.LocFXResolution;
                entity.Header[0x44] = bulletRenderInfo.LocFXOutputResolution;
                entity.Header[0x45] = bulletRenderInfo.LocFXOpacity;
            }
            entity.TexturePosition = spPos;
            entity.TextureSize = bulletRenderInfo.SourceSize;
            entity.TotalTextureSize = Helper.GetSize(entity.Texture);
        }
        else
        {
            EntityVisual visual = EntityVisual.Visuals[info.Visual];
            if (!info.OverrideDeathColor)
            {
                entity.Header[0xB] = visual.DeathCircleColor;
                entity.Header[0xC] = visual.DeathParticleGlowColor;
            }
            entity.Texture = Runtime.CurrentRuntime.Textures[visual.Texture];
            entity.Source = new(
                visual.SourcePosition,
                visual.RenderSize
            );
            entity.Target.Size = entity.Source.Size;
            entity.Origin = entity.Source.Size / 2;
            entity.FloatingPoints[0x13] = visual.Collision;
            if (info.IsBoss)
            {
                entity.BackgroundDistortionEffect = new GameplayScreenEffect(box, 
                    new Vector2(192, 192), 
                    4, 
                    "boss_background_distortion", 
                    box.GetTime(), 
                    float.MaxValue)
                {
                    Layer = GameplayScreenEffect.EffectLayer.BackgroundOnly,
                    StepLength = 1,
                    UseSteps = true
                };
                entity.BossCircleEffect = new BossCircleScreenEffect(box, Vector2.Zero, 0, box.GetTime(), float.MaxValue, entity);
                box.AddScreenEffect(entity.BackgroundDistortionEffect);
                box.AddScreenEffect(entity.BossCircleEffect);
                // An invincible-boss chapter (e.g. a survival/pizza card) shows no health bar — there is nothing to
                // whittle down. Keyed on the chapter flag, which is known before the create script sets the flag on
                // the object itself.
                if (!(box.ChapterInfo?.BossInvincible ?? false))
                    box.AddOverlay(new BossHealthOverlay(box, entity));
                entity.FloatingPoints[0xa] = entity.FloatingPoints[0];
            }
        }
        entity.Health = MathF.Max(1, entity.Health);
        return entity;
    }

    public void LoadAnotherFile(FileEntityInfo info)
    {
        CreateAction = info.UseCreateScript ? ActionsScope.ObjectActions[info.CreateScript] : null;
        UpdateAction = ActionsScope.ObjectActions[info.UpdateScript];
        CreatedAt = Box.CurrentTick;
        RemoveAction = info.UseRemoveScript ? ActionsScope.ObjectActions[info.RemoveScript] : null;
        if(!info.IsBullet)
            DieAction = info.UseDieScript ? ActionsScope.ObjectActions[info.DieScript] : null;
        Array.Copy(info.Header, Header, info.Header.Length);
        Array.Copy(info.FloatingPoints, FloatingPoints,info.FloatingPoints.Length);
        FloatingPoints[0xa] = info.FloatingPoints[0];
    }

    public RuntimeObject CloneWithPositionSpawnTick(int x, int y, int tick)
    {
        RuntimeObject entity = new RuntimeObject();
        entity.Header = Header;
        entity.Source = Source;
        entity.Texture = Texture;
        entity.Header[0x15] = tick;
        entity.Header[0x05] = x;
        entity.Header[0x06] = y;
        entity.UpdateOrigin();
        entity.UpdateTargetRectangle();
        return entity;
    }

    /// <summary>
    /// A fully independent copy — its own <see cref="Header"/>/<see cref="FloatingPoints"/> arrays plus the same
    /// behaviour delegates — used only by the benchmark to synthesise heavy bullet load. Unlike
    /// <see cref="CloneWithPositionSpawnTick"/> (which SHARES the Header array and drops the update action), this
    /// produces a bullet that moves and despawns on its own, so a crowd of them stresses the update + O(n²)
    /// collision loops the way a real danmaku scene does.
    /// </summary>
    public RuntimeObject DeepCloneForBench()
    {
        var c = new RuntimeObject
        {
            Header = (int[])Header.Clone(),
            FloatingPoints = (float[])FloatingPoints.Clone(),
            Source = Source,
            Texture = Texture,
            Box = Box,
            CreateAction = CreateAction,
            UpdateAction = UpdateAction,
            DieAction = DieAction,
            RemoveAction = RemoveAction,
        };
        c.UpdateOrigin();
        c.UpdateTargetRectangle();
        return c;
    }
    
    public Drop BadDrop => new(Header[4]);
    public Drop GoodDrop => new(Header[5]);
    
    public float RenderRotation
    {
        get => FloatingPoints[5] ;
        set => FloatingPoints[5] = value;
    }

    public Rect SourceRectangle => Source;
    public Rect TargetRectangle => Target with { X = FloatingPoints[0x10], Y = FloatingPoints[0x11] };
    
    public float FacingRotation
    {
        get => FloatingPoints[0x6];
        set => FloatingPoints[0x6] = value;
    }

    public float Health
    {
        get => FloatingPoints[0];
        set => FloatingPoints[0] = value;
    }

    public float MaxHealth
    {
        get => FloatingPoints[10];
        set => FloatingPoints[10] = value;
    }
    
    public float RenderScaleX
    {
        get => BitConverter.ToSingle(BitConverter.GetBytes(Header[0x09]), 0);
        set
        {
            Header[0x09] = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            UpdateTargetRectangle();
            UpdateOrigin();
        }
    }

    public float RenderScaleY
    {
        get => BitConverter.ToSingle(BitConverter.GetBytes(Header[0x0A]), 0);
        set
        {
            Header[0x0A] = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            UpdateTargetRectangle();
            UpdateOrigin();
        }
    }

    public float CollisionScale
    {
        get => FloatingPoints[2];
        set => FloatingPoints[2] = value;
    }

    public byte Transparency
    {
        get => (byte)Header[0x0C];
        set => Header[0x0C] = value;
    }

    public float Speed
    {
        get => FloatingPoints[7];
        set => FloatingPoints[7] = value;
    }

    public Vector2 CollectableVelocity
    {
        get => new(FloatingPoints[2],  FloatingPoints[6]);
        set
        {
            FloatingPoints[2] = value[0];
            FloatingPoints[6] = value[1];
        }
    }

    public float X
    {
        get => FloatingPoints[0x10];
        set
        {
            FloatingPoints[0x10] = value;
        }
    }

    public float Y
    {
        get =>  FloatingPoints[0x11];
        set
        {
            FloatingPoints[0x11] = value;
        }
    }

    public Vector2 MoveTarget
    {
        get => new (FloatingPoints[0x14],  FloatingPoints[0x15]);
        set
        {
            FloatingPoints[0x14] = value.X;
            FloatingPoints[0x15] = value.Y;
        }
    }

    public Vector2 Velocity
    {
        get => new Vector2(FloatingPoints[0x17],  FloatingPoints[0x18]);
        set
        {
            FloatingPoints[0x17] = value.X;
            FloatingPoints[0x18] = value.Y;
        }
    }

    public float Z
    {
        get => FloatingPoints[0x12];
        set => FloatingPoints[0x12] = value;
    }

    public int CreatedAt
    {
        get => Header[0x17];
        set => Header[0x17] = value;
    }

    public Vector2 Position
    {
        get => new(FloatingPoints[0x10],  FloatingPoints[0x11]);
        set
        {
            FloatingPoints[0x10] = value.X;
            FloatingPoints[0x11] = value.Y;
        }
    }

    public float Collision
    {
        get => FloatingPoints[0x13];
        set => FloatingPoints[0x13] = value;
    }

    // ---- Laser (a straight beam projectile). A laser reuses Position (0x10/0x11) as its emitter end and
    // RenderRotation (0x5) as its angle; the fields below live in otherwise-unused scratch slots. Its life runs
    // telegraph → fire → fade, measured in ticks from CreatedAt. See FlagIsLaser handling in GameBox.
    public float LaserLength { get => FloatingPoints[0x50]; set => FloatingPoints[0x50] = value; }
    public float LaserWidth  { get => FloatingPoints[0x51]; set => FloatingPoints[0x51] = value; }
    public int LaserTelegraphTicks { get => Header[0x50]; set => Header[0x50] = value; }
    public int LaserFireTicks      { get => Header[0x51]; set => Header[0x51] = value; }
    public int LaserFadeTicks      { get => Header[0x52]; set => Header[0x52] = value; }
    public int LaserLifetime => LaserTelegraphTicks + LaserFireTicks + LaserFadeTicks;

    /// <summary>Ray-only: extra half-width (px) added at the far tip per side, so the beam widens from LaserWidth
    /// at the emitter to LaserWidth + 2*RaySpread at the end. 0 = a parallel beam like a laser.</summary>
    public float RaySpread { get => FloatingPoints[0x52]; set => FloatingPoints[0x52] = value; }

    /// <summary>Builds a ray: a laser-like beam that widens into a cone (controlled by <paramref name="spread"/>)
    /// from a large emitting dot and fades along its finite length. Dangerous only during its fire phase, like a
    /// laser. Add it to the box with <c>AddObject</c>.</summary>
    public static RuntimeObject MakeRay(GameBox box, Vector2 origin, float angleRadians, float length,
        float width, float spread, int telegraphTicks, int fireTicks, int fadeTicks)
    {
        var ray = new RuntimeObject { Box = box };
        ray.Header[0] = FlagIsRay | FlagDangerousRelatedToPlayer;
        ray.Health = 1;   // keep it out of the Health<=0 entity-death path
        ray.Position = origin;
        ray.RenderRotation = angleRadians;
        ray.LaserLength = length;
        ray.LaserWidth = width;
        ray.RaySpread = spread;
        ray.LaserTelegraphTicks = telegraphTicks;
        ray.LaserFireTicks = fireTicks;
        ray.LaserFadeTicks = fadeTicks;
        ray.CreatedAt = box.CurrentTick;
        ray.UpdateAction = o =>
        {
            if (o.Box.CurrentTick - o.CreatedAt >= o.LaserLifetime)
                o.Box.RemoveObject(o);
        };
        return ray;
    }

    /// <summary>Builds a straight-beam laser and wires its self-removal at end of life. The beam is dangerous
    /// only during its fire phase (handled by the collision code). Add it to the box with <c>AddObject</c>.</summary>
    public static RuntimeObject MakeLaser(GameBox box, Vector2 origin, float angleRadians, float length,
        float width, int telegraphTicks, int fireTicks, int fadeTicks)
    {
        var laser = new RuntimeObject { Box = box };
        laser.Header[0] = FlagIsLaser | FlagDangerousRelatedToPlayer;
        laser.Health = 1;   // keep it out of the Health<=0 entity-death path (LoadFromFile does this for bullets)
        laser.Position = origin;
        laser.RenderRotation = angleRadians;
        laser.LaserLength = length;
        laser.LaserWidth = width;
        laser.LaserTelegraphTicks = telegraphTicks;
        laser.LaserFireTicks = fireTicks;
        laser.LaserFadeTicks = fadeTicks;
        laser.CreatedAt = box.CurrentTick;
        laser.UpdateAction = o =>
        {
            if (o.Box.CurrentTick - o.CreatedAt >= o.LaserLifetime)
                o.Box.RemoveObject(o);
        };
        return laser;
    }

    public int SpawnId => Header[2];
    public int BossId => Header[7];

    void UpdateSourceRectangle()
    {
        Source = new Rect(Header[3], Header[4], Header[1], Header[2]);
    }

    void UpdateTargetRectangle()
    {
        Target.Width = RenderScaleX * Header[1];
        Target.Height = RenderScaleY * Header[2];
        Target.X = Header[5] - (Target.Width / 2);
        Target.Y = Header[6] - (Target.Height / 2);
    }

    void UpdateOrigin()
    {
        //Origin = new Vector2(RenderScaleX * Header[1] / 2,  RenderScaleY * Header[2] / 2);
    }

    /// <param name="runAction">When false, the per-tick behaviour script (<see cref="UpdateAction"/>) is skipped
    /// but movement still applies — used to hold a boss's attack during a dialog while it can still slide into
    /// place.</param>
    public void Update(bool runAction = true)
    {
        if ((Header[0] & FlagIsMovingToTarget) == FlagIsMovingToTarget)
        {
            Position = MathUtil.Vector2MoveTowards(Position, MoveTarget, FloatingPoints[0x16]);
            if (MathUtil.Vector2Distance(Position, MoveTarget) < 1)
                Header[0] &= ~FlagIsMovingToTarget;
        }
        if (runAction)
            UpdateAction?.Invoke(this);
    }

    public void SetMoveToTarget(float speed, Vector2 target)
    {
        MoveTarget = target;
        FloatingPoints[0x16] = speed;
        Header[0] |= FlagIsMovingToTarget;
    }

    public void UpdateCollectableBullet()
    {
        var direction = Helper.GetDirection(Position, new Vector2(Box.Player.X, Box.Player.Y));
        FloatingPoints[2] = MathUtil.MoveTowards(FloatingPoints[2], direction.X * 100000, 0.2f);
        FloatingPoints[6] = MathUtil.MoveTowards(FloatingPoints[6], direction.Y * 100000, 0.2f);
        X += FloatingPoints[2];
        Y += FloatingPoints[6];
        RenderRotation += Helper.FindAngle(Position, new Vector2(Box.Player.X, Box.Player.Y)) * 0.09f;
        if (Helper.IsCollied(TargetRectangle, Box.Player.Collision))
        {
            Box.ScoreTarget += (int)Math.Pow(10, (448-Y)/10) * Header[5];
            Box.RemoveObject(this);
        }
    }

    /// <summary>
    /// Accelerates this collectable toward <paramref name="target"/>. Unlike a collectable bullet — which aims at
    /// a target 100000 units away and so just keeps gaining speed — this converges on a bounded cruising speed.
    /// That matters both ways round: an item cannot build up enough speed per tick to jump straight over the
    /// player (or the toilet's mouth) between two frames and sail off to be culled at the box edge.
    /// </summary>
    private void Steer(Vector2 target, float acceleration, float maxSpeed)
    {
        Vector2 direction = Helper.GetDirection(Position, target);
        FloatingPoints[2] = MathUtil.MoveTowards(FloatingPoints[2], direction.X * maxSpeed, acceleration);
        FloatingPoints[6] = MathUtil.MoveTowards(FloatingPoints[6], direction.Y * maxSpeed, acceleration);
        FloatingPoints[0x5] = Box.CurrentTick;   // spins while it flies, as a falling item does when it drifts
        X += FloatingPoints[2];
        Y += FloatingPoints[6];
    }

    /// <summary>
    /// How far (playfield px) the mystical toilet reaches for an item the player is not pulling in. Wide enough
    /// to cover most of the box from its perch near the top, so it visibly competes for what drops — but short
    /// of the 448px it would take to reach everything, so items falling down near the player still get away.
    /// </summary>
    private const float ToiletMagnetRadius = 176f;

    /// <summary>How close (playfield px) an item has to get to the toilet before it is swallowed.</summary>
    private const float ToiletSwallowDistance = 14f;

    /// <summary>Per-tick acceleration of a magneted item, and the speed it settles at (px/tick). Both are kept
    /// well under the collision radii either end so a fast item cannot skip past its destination in one tick.</summary>
    private const float ToiletPullAcceleration = 0.12f, ToiletPullSpeed = 5f;
    private const float HomingAcceleration = 0.18f, HomingSpeed = 6f;

    public void UpdateCollectable()
    {
        if ((Header[0] & FlagHomingCollectable) == FlagHomingCollectable)
        {
            // Loot the toilet spat back out. It homes in on the player from anywhere on the playfield — the
            // whole point of the reward is that you get the hoard back without having to chase it down.
            Steer(new Vector2(Box.Player.X, Box.Player.Y), HomingAcceleration, HomingSpeed);
        }
        else if (Box.MysticalToilet is { } toilet && !Box.Player.IsMagneting(Position) &&
                 MathUtil.Vector2Distance(Position, toilet.Position) < ToiletMagnetRadius)
        {
            // The toilet takes what the player does not: anything inside its reach but outside the player's own
            // magnet gets dragged in. While the player sits above the item line their magnet covers the whole
            // box (Player.PointMagnetRadius), so the toilet can never steal from under them there.
            if (MathUtil.Vector2Distance(Position, toilet.Position) < ToiletSwallowDistance)
            {
                Box.SwallowIntoToilet(this);
                return;
            }
            Steer(toilet.Position, ToiletPullAcceleration, ToiletPullSpeed);
        }
        else
        {
            // Untouched: what sideways travel it has bleeds off, and it falls.
            FloatingPoints[0x5] = MathF.Abs(FloatingPoints[2]) > 0 ? Box.CurrentTick : 0;
            FloatingPoints[2] = MathUtil.MoveTowards(FloatingPoints[2], 0, 0.1f);
            FloatingPoints[6] = MathUtil.MoveTowards(FloatingPoints[6], float.MaxValue, 0.1f);
            X += FloatingPoints[2];
            Y += FloatingPoints[6];
        }
        if (Helper.IsCollied(TargetRectangle, Box.Player.Collision))
        {
            switch (Header[4])
            {
                case 0:
                    Box.Player.Power += 1;
                    break;
                case 1:
                    Box.Player.Power += 100;
                    break;
                case 2:
                    Box.Player.Power = 400;
                    break;
                case 3:
                    Box.ScoreTarget += (int)(MathF.Floor(MathF.Pow(2, 14 * (464-Box.Player.Y) / 464) / 16384 * 30) / 30 * 100000);
                    break;
                case 4:
                    Box.Player.HeartPoints++;
                    break;
                case 5:
                    Box.Player.HeartSpices++;
                    break;
                case 6:
                    Box.Player.Bombs++;
                    break;
                case 7:
                    Box.Player.BombsSpices++;
                    break;
            }
            Box.RemoveObject(this);
        }
    }
}