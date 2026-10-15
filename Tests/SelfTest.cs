using System.Numerics;

namespace StarBlaster;

/// <summary>Headless checks of the game rules: <c>dotnet run -- --selftest</c>.</summary>
public static class SelfTest
{
    private const float Dt = 1f / 60f;
    private static int _failures;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine($"  [{(ok ? " ok " : "FAIL")}] {what}");
        if (!ok) _failures++;
    }

    private static void Run(Game g, float seconds, Func<Game, Input>? brain = null)
    {
        for (int i = 0; i < (int)(seconds / Dt); i++)
            g.Update(Dt, brain?.Invoke(g) ?? default);
    }

    public static int Run()
    {
        Console.WriteLine("Scenario: stage 1 enters the formation");
        {
            var g = new Game(1) { Invincible = true };
            g.NewGame();
            Run(g, 3f);
            Check(g.Phase == Phase.Playing, "ready screen ends and play starts");
            Run(g, 22f);
            Check(g.Enemies.Count == 40, "40 enemies spawned");
            Check(g.Enemies.All(e => e.State is EnemyState.Formation or EnemyState.Diving or EnemyState.Beaming or EnemyState.Settling),
                "every enemy reached the formation (or is attacking)");
            Check(g.Enemies.Count(e => e.Kind == EnemyKind.Boss) == 4, "4 bosses");
            Check(g.Enemies.Count(e => e.Kind == EnemyKind.Butterfly) == 16, "16 butterflies");
            Check(g.Enemies.Count(e => e.Kind == EnemyKind.Bee) == 20, "20 bees");
        }

        Console.WriteLine("Scenario: shooting scores points");
        {
            var g = new Game(2) { Invincible = true };
            g.NewGame();
            Run(g, 3f);
            Run(g, 22f);
            var bee = g.Enemies.First(e => e.Kind == EnemyKind.Bee && e.State == EnemyState.Formation);
            int before = g.Score;
            // park under the target and fire
            Run(g, 6f, gm => { var t = bee.Pos.X; return new Input { Fire = true, Left = gm.PX > t + 1, Right = gm.PX < t - 1 }; });
            Check(g.Score > before || !bee.Alive, "the bot's shots destroyed an enemy and scored");
        }

        Console.WriteLine("Scenario: tractor beam captures the ship, rescue gives dual fire");
        {
            var g = new Game(3) { Invincible = true };
            g.NewGame();
            Run(g, 3f);
            Run(g, 24f);                                  // formed, player idle (so no hits yet)
            Check(g.Phase == Phase.Playing, "still playing (bombs may have hit? lives left: " + g.Lives + ")");
            var boss = g.Enemies.First(e => e.Kind == EnemyKind.Boss && e.State == EnemyState.Formation);
            g.PX = boss.Pos.X;                            // stand where the beam will land
            g.StartDive(boss, tractor: true);
            Run(g, 12f);
            Check(g.Captures == 1, "player was captured");
            Check(boss.HasCaptive, "boss carries the captured fighter");
            Check(g.Lives == 1 || g.PState != PlayerState.Active, "capture cost a ship");
            Run(g, 4f);
            Check(g.PState == PlayerState.Active && !g.Dual, "next ship launched, single");

            // find the captor and make it dive again; kill it while diving
            if (boss.State == EnemyState.Formation) g.StartDive(boss, tractor: false);
            Check(boss.State == EnemyState.Diving, "captor dives normally");
            boss.Hp = 1;
            // shoot it directly
            g.PX = boss.Pos.X;
            g.Shots.Add(new Shot { Pos = boss.Pos + new Vector2(0, 6), Vel = new Vector2(0, -100) });
            Run(g, 0.1f);
            Check(!boss.Alive, "diving captor destroyed");
            Run(g, 4.5f);
            Check(g.Rescues == 1 && g.Dual, "rescued fighter joined: dual ship");
            Check(g.PlayerHalfWidth > 10, "dual ship is wider");
        }

        Console.WriteLine("Scenario: killing a captor in formation destroys the prisoner");
        {
            var g = new Game(4) { Invincible = true };
            g.NewGame();
            Run(g, 27f);
            var boss = g.Enemies.First(e => e.Kind == EnemyKind.Boss && e.State == EnemyState.Formation);
            boss.HasCaptive = true; boss.Hp = 1;
            g.Shots.Add(new Shot { Pos = boss.Pos + new Vector2(0, 4), Vel = new Vector2(0, -100) });
            Run(g, 0.1f);
            Check(!boss.Alive && g.RescueShips.Count == 0, "no rescue ship from a formation kill");
        }

        Console.WriteLine("Scenario: autoplay across seeds, challenge stage, no exceptions");
        for (int seed = 10; seed < 16; seed++)
        {
            var g = new Game(seed);
            g.NewGame();
            bool sawChallenge = false;
            int maxStage = 1;
            for (int f = 0; f < 60 * 60 * 6 && g.Phase != Phase.Title; f++)   // up to 6 minutes
            {
                g.Update(Dt, Bot.Think(g));
                sawChallenge |= g.Phase == Phase.Playing && g.Challenge;
                maxStage = Math.Max(maxStage, g.Stage);
                if (g.Phase == Phase.GameOver) break;
            }
            Console.WriteLine($"    seed {seed}: stage {maxStage}, score {g.Score}, captures {g.Captures}, rescues {g.Rescues}, " +
                              $"accuracy {(g.ShotsFired == 0 ? 0 : g.Hits * 100 / g.ShotsFired)}%, challenge seen: {sawChallenge}");
            Check(!float.IsNaN(g.PX) && g.Enemies.All(e => !float.IsNaN(e.Pos.X)), $"seed {seed}: state is finite");
        }

        Console.WriteLine("Scenario: perfect challenge stage awards 10000");
        {
            var g = new Game(7);
            g.NewGame(startStage: 3);
            Check(g.Challenge, "stage 3 is a challenge stage");
            Run(g, 2.5f);
            Check(g.Enemies.Count == 40, "40 challenge enemies");
            // Cheat: kill everything as soon as it appears
            int guard = 0;
            while (g.Phase == Phase.Playing && !g.StageDone && guard++ < 60 * 40)
            {
                foreach (var e in g.Enemies.Where(e => e.Visible && e.Alive).ToList())
                    g.Shots.Add(new Shot { Pos = e.Pos, Vel = new Vector2(0, -1) });
                g.Update(Dt, default);
            }
            Check(g.ChallengeHits == 40 && g.ChallengeBonus == 10000, $"perfect: hits={g.ChallengeHits} bonus={g.ChallengeBonus}");
        }

        Console.WriteLine(_failures == 0 ? "\nALL CHECKS PASSED" : $"\n{_failures} CHECK(S) FAILED");
        return _failures == 0 ? 0 : 1;
    }
}
