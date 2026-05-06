namespace DmitryAndDemid.Gameplay;

public abstract class PlayerWeapon(Player player)
{
    protected Player Player = player;
    public float FocusTimestamp = 0;
    public float DefocusTimestamp = 0;

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
}