namespace DmitryAndDemid.Gameplay;

public abstract class PlayerWeapon(Player player)
{
    protected Player Player = player;
    public int FocusTimestamp = 0;
    public int DefocusTimestamp = 0;
    public bool IsBombActive = false;
    public int BombActivationTick = 0;

    public virtual void Update()
    {
        
    }
    
    public virtual void UpdatePower()
    {
        
    }
    
    public virtual void DrawBottomLayer()
    {
        
    }

    public virtual void DrawTopLayer()
    {
        
    }

    public virtual void Shoot()
    {
        
    }

    public virtual void SpawnDistortionEffect(int x, int y)
    {
        
    }

    public virtual void AddShootTargetScore()
    {
        
    }

    public void Bomb()
    {
        if (IsBombActive)
            return;
        if (Player.Bombs == 0)
            return;
        IsBombActive = true;
        Player.CollisionEnabled = false;
        BombActivationTick = Player.GameBox.CurrentTickWithOffset;
        StartBombing();
        Player.Bombs--;
        Player.GameBox.IsFailed = true;
    }

    protected virtual void StartBombing()
    {
        
    } 
}