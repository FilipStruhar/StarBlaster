using System.Numerics;

namespace StarBlaster;

/// <summary>Simple autoplayer used by the headless self-test and screenshot mode.</summary>
public static class Bot
{
    public static Input Think(Game g)
    {
        var inp = new Input { Fire = true, Start = g.Phase is Phase.Title or Phase.GameOver };
        if (g.Phase != Phase.Playing || g.PState != PlayerState.Active) return inp;

        // dodge: any bomb about to land on us
        Shot? threat = null;
        foreach (var b in g.Bombs)
        {
            if (b.Pos.Y > 170 && b.Pos.Y < Game.PlayerY && MathF.Abs(b.Pos.X - g.PX) < g.PlayerHalfWidth + 8)
                if (threat is null || b.Pos.Y > threat.Pos.Y) threat = b;
        }
        float target = g.PX;
        if (threat is not null)
        {
            target = threat.Pos.X > g.PX ? g.PX - 25 : g.PX + 25;
        }
        else
        {
            // chase attackers first (rescue chances, safety), then the lowest enemy
            Enemy? best = null;
            foreach (var e in g.Enemies)
            {
                if (!e.Alive || !e.Visible) continue;
                float score = (e.IsAttacking ? 1000 : 0) + e.Pos.Y - MathF.Abs(e.Pos.X - g.PX) * 0.3f;
                if (best is null || score > (best.IsAttacking ? 1000 : 0) + best.Pos.Y - MathF.Abs(best.Pos.X - g.PX) * 0.3f)
                    best = e;
            }
            if (best is not null) target = best.Pos.X;
        }

        float dx = target - g.PX;
        inp.Left = dx < -2f;
        inp.Right = dx > 2f;
        return inp;
    }
}
