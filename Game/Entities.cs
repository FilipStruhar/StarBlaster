using System.Numerics;

namespace StarBlaster;

public enum Phase { Title, Ready, Playing, GameOver }
public enum EnemyKind { Bee, Butterfly, Boss }
public enum EnemyState { Waiting, Entering, Settling, Formation, Diving, Beaming, Leaving }
public enum PlayerState { Active, Captured, Gone }

public enum Sfx { Shoot, EnemyDie, BossHit, PlayerDie, Dive, Beam, Capture, Rescue, ExtraLife, Start, Perfect }

public struct Input
{
    public bool Left, Right, Fire;   // held
    public bool Start, Pause;        // pressed this frame
}

public sealed class Enemy
{
    public EnemyKind Kind;
    public EnemyState State = EnemyState.Waiting;
    public Vector2 Pos;
    public float Angle;              // degrees, 0 = sprite facing down, clockwise positive
    public int Col, Row, Hp = 1, Id;
    public float Delay;
    public PathCurve? Path;
    public float Dist, Speed;
    public bool HasCaptive;
    public bool Tractor;             // dive that ends in a tractor beam
    public float Timer;              // beam timer
    public int Bombs;
    public float BombTimer;
    public bool Alive = true;

    public bool IsAttacking => State is EnemyState.Diving or EnemyState.Beaming;
    public bool Visible => State != EnemyState.Waiting;
}

public sealed class Shot
{
    public Vector2 Pos, Vel;
}

public sealed class Particle
{
    public Vector2 Pos, Vel;
    public float Life, MaxLife;
    public byte R, G, B;
    public int Size = 1;
}

public sealed class RescueShip
{
    public Vector2 Pos;
    public float Angle;
}

public struct Star
{
    public float X, Y, Speed, Phase;
    public int Layer;
}
