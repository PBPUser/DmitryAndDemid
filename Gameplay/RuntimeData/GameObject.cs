using System.Numerics;
using System.Runtime.InteropServices;
using DmitryAndDemid.Data.Archive;
using GLib;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay.RuntimeData;

public struct GameObject
{
    private static Rectangle SourceTemp = new();
    private static Rectangle TargetTemp = new();
    private static float Delta = 0f;

    public int Pointer = 0;
    public Texture2D Texture;
    public int[] Variables = new int[72];
    public float[] FloatingPoints = new float[72];

    public Rectangle SourceRectangle
    {
        get
        {
            SourceTemp.X = Variables[3] * Variables[5];
            SourceTemp.Y = Variables[4]  * Variables[6];
            SourceTemp.Width = Variables[5];
            SourceTemp.Height = Variables[6];
            return SourceTemp;
        }
    }

    public Rectangle DestinationRectangle
    {
        get
        {
            TargetTemp.X = Variables[2] - Variables[5] * FloatingPoints[0] / 2;
            TargetTemp.Y = Variables[3] - Variables[6] * FloatingPoints[1] / 2;
            TargetTemp.Width = Variables[5];
            TargetTemp.Height = Variables[6];
            return TargetTemp;
        }
    }

    public Vector2 Position => new Vector2(Variables[1], Variables[2]);
    public Vector2 Origin => new Vector2(Variables[5],  Variables[6]) / 2;

    public bool CheckCollision(GameObject other)
    {
        Delta = Raymath.Vector2Distance(Position, other.Position);
        return Delta > Variables[7] * FloatingPoints[2];
    }

    public bool CheckPlayerCollision(Player player)
    {
        Delta = Raymath.Vector2Distance(Position, new Vector2(player.X, player.Y));
        return Delta > Variables[7] * FloatingPoints[2];
    }

    public GameObject(FileEntityInfo entity)
    {
        Texture = Runtime.CurrentRuntime.Textures[entity.Visual];
        Variables = new int[72];
        FloatingPoints = new float[72];
        Array.Copy(entity.Header, Variables, entity.Header.Length);
        Array.Copy(entity.FloatingPoints, FloatingPoints, entity.FloatingPoints.Length);
    }
}