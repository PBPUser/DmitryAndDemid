namespace DmitryAndDemid.Gameplay;

public abstract class PlayerWeapon(Player player)
{
    protected Player Player = player;
    public int FocusTimestamp = 0;
    public int DefocusTimestamp = 0;

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
}