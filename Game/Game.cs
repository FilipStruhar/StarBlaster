using System.Numerics;

namespace StarBlaster;

/// <summary>
/// Galaga-style arcade shooter rules. Pure logic: no rendering, no audio device, no Raylib.
/// Coordinates are in a 224x288 virtual screen (the classic arcade resolution).
/// </summary>
public sealed class Game
{
    public const int W = 224;
    public const int H = 288;
    public const float PlayerY = 262f;
    private const float Cx = W / 2f;

    // ---- global state ----
    public Phase Phase = Phase.Title;
    public int Stage, Score, HighScore, Lives;     // Lives = reserve ships
    public bool Paused;
    public float Time;
    public float PhaseTimer;
    public bool Challenge;
    public int ChallengeHits, ChallengeBonus, ShotsFired, Hits;
    public bool StageDone;
    public float DoneTimer;
    public bool NewHighScore;
    public bool Invincible;          // dev/test switch: bombs and rams do no harm

    // stats for tests
    public int Captures, Rescues;

    // ---- world ----
    public readonly List<Enemy> Enemies = new();
    public readonly List<Shot> Shots = new();
    public readonly List<Shot> Bombs = new();
    public readonly List<Particle> Particles = new();
    public readonly List<RescueShip> RescueShips = new();
    public readonly Star[] Stars = new Star[90];

    // ---- player ----
    public float PX = Cx, PY = PlayerY;
    public PlayerState PState = PlayerState.Active;
    public bool Dual;
    public float Invuln, PTimer, CaptureSpin;
    private float _fireCd;
    private Enemy? _captor;

    // ---- formation ----
    public float Sway, Breath;
    private float _swayAmp = 6f, _formT;
    private float _diveTimer = 2f;
    private int _nextExtra = 20000;
    private int _enemyIdCounter;

    private Random _rng;

    public event Action<Sfx>? Sound;
    public event Action<int>? HighScoreChanged;

    public Game(int seed = 0)
    {
        _rng = seed == 0 ? new Random() : new Random(seed);
        for (int i = 0; i < Stars.Length; i++)
        {
            int layer = i % 3;
            Stars[i] = new Star
            {
                X = _rng.Next(W), Y = _rng.Next(H),
                Layer = layer, Speed = 10f + layer * 18f,
                Phase = (float)_rng.NextDouble() * 6.28f,
            };
        }
    }

    // ------------------------------------------------------------------ flow

    public bool AllEntered => !Challenge && Enemies.All(e => e.State is not (EnemyState.Waiting or EnemyState.Entering));

    public void NewGame(int startStage = 1)
    {
        Score = 0; Lives = 2; Stage = startStage; Dual = false;
        _nextExtra = 20000; ShotsFired = Hits = 0; NewHighScore = false;
        Captures = Rescues = 0;
        BeginReady();
        Sound?.Invoke(Sfx.Start);
    }

    private void BeginReady()
    {
        Phase = Phase.Ready;
        PhaseTimer = 2.2f;
        Challenge = Stage % 4 == 3;
        StageDone = false;
        Enemies.Clear(); Shots.Clear(); Bombs.Clear(); RescueShips.Clear();
        PState = PlayerState.Active; PX = Cx; PY = PlayerY; Invuln = 0; _captor = null;
    }

    private void BeginStage()
    {
        Phase = Phase.Playing;
        _formT = 0; Sway = 0; Breath = 0; _swayAmp = 6f;
        _diveTimer = 3f;
        ChallengeHits = 0;
        if (Challenge) BuildChallenge(); else BuildFormation();
    }

    public void Update(float dt, Input inp)
    {
        UpdateStars(dt);
        switch (Phase)
        {
            case Phase.Title:
                Time += dt;
                if (inp.Start) NewGame();
                break;

            case Phase.Ready:
                Time += dt;
                PhaseTimer -= dt;
                if (PhaseTimer <= 0) BeginStage();
                break;

            case Phase.Playing:
                if (inp.Pause) Paused = !Paused;
                if (Paused) return;
                Time += dt;
                UpdatePlaying(dt, inp);
                break;

            case Phase.GameOver:
                Time += dt;
                UpdateParticles(dt);
                PhaseTimer -= dt;
                if (PhaseTimer <= 0 || (inp.Start && PhaseTimer < 3f)) { Phase = Phase.Title; ClearWorld(); }
                break;
        }
    }

    public void AbortToTitle()
    {
        Phase = Phase.Title;
        if (Score > HighScore) { HighScore = Score; HighScoreChanged?.Invoke(HighScore); }
        ClearWorld();
    }

    private void ClearWorld()
    {
        Enemies.Clear(); Shots.Clear(); Bombs.Clear(); Particles.Clear(); RescueShips.Clear();
        Paused = false;
    }

    // ------------------------------------------------------------------ stage construction

    private static readonly (int row, int c0, int c1)[] FormationRows =
    {
        (0, 3, 6), (1, 1, 8), (2, 1, 8), (3, 0, 9), (4, 0, 9),
    };

    private void BuildFormation()
    {
        var slots = new List<(int col, int row)>();
        foreach (var (row, c0, c1) in FormationRows)
            for (int c = c0; c <= c1; c++) slots.Add((c, row));

        int d = Math.Min(Stage, 24);
        float speed = Math.Min(140f + 4f * d, 210f);
        int[] shapes = { 0, 1, 0, 1, 0 };
        float[] sides = { -1, 1, 1, -1, 1 };

        for (int i = 0; i < slots.Count; i++)
        {
            int g = i / 8, k = i % 8;
            var (col, row) = slots[i];
            var e = new Enemy
            {
                Id = _enemyIdCounter++,
                Col = col, Row = row,
                Kind = row == 0 ? EnemyKind.Boss : row <= 2 ? EnemyKind.Butterfly : EnemyKind.Bee,
                Delay = g * 2.3f + k * 0.13f,
                Speed = speed,
                Path = EntryPath(shapes[g], sides[g]),
            };
            e.Hp = e.Kind == EnemyKind.Boss ? 2 : 1;
            e.Pos = new Vector2(-50, -50);
            Enemies.Add(e);
        }
    }

    private readonly Dictionary<(int, float), PathCurve> _pathCache = new();

    private PathCurve EntryPath(int shape, float s)
    {
        if (_pathCache.TryGetValue((shape, s), out var cached)) return cached;
        Vector2[] c = shape == 0
            ? new[]
            {
                new Vector2(Cx + s * 20, -20), new Vector2(Cx + s * 20, 70), new Vector2(Cx + s * 72, 125),
                new Vector2(Cx + s * 72, 185), new Vector2(Cx + s * 24, 212), new Vector2(Cx - s * 14, 172),
                new Vector2(Cx - s * 2, 128), new Vector2(Cx, 100),
            }
            : new[]
            {
                new Vector2(Cx + s * 135, 150), new Vector2(Cx + s * 70, 152), new Vector2(Cx + s * 22, 200),
                new Vector2(Cx - s * 34, 226), new Vector2(Cx - s * 62, 186), new Vector2(Cx - s * 30, 140),
                new Vector2(Cx, 112),
            };
        var p = new PathCurve(c);
        _pathCache[(shape, s)] = p;
        return p;
    }

    private void BuildChallenge()
    {
        var shapeA = new Func<float, Vector2[]>(s => new[]
        {
            new Vector2(Cx + s * 30, -20), new Vector2(Cx + s * 30, 80), new Vector2(Cx + s * 74, 122),
            new Vector2(Cx + s * 52, 172), new Vector2(Cx + s * 8, 150), new Vector2(Cx + s * 22, 108),
            new Vector2(Cx - s * 30, 150), new Vector2(Cx - s * 72, 230), new Vector2(Cx - s * 40, 320),
        });
        var shapeB = new Func<float, Vector2[]>(s => new[]
        {
            new Vector2(Cx + s * 140, 60), new Vector2(Cx + s * 44, 70), new Vector2(Cx - s * 40, 104),
            new Vector2(Cx - s * 72, 156), new Vector2(Cx - s * 30, 204), new Vector2(Cx + s * 30, 172),
            new Vector2(Cx + s * 60, 232), new Vector2(Cx + s * 34, 320),
        });

        float speed = Math.Min(120f + 5f * Stage, 190f);
        for (int g = 0; g < 5; g++)
        {
            float s = g % 2 == 0 ? -1 : 1;
            var path = new PathCurve(g % 3 == 1 ? shapeB(s) : shapeA(s));
            var kind = g == 4 ? EnemyKind.Boss : g % 2 == 0 ? EnemyKind.Bee : EnemyKind.Butterfly;
            for (int k = 0; k < 8; k++)
            {
                Enemies.Add(new Enemy
                {
                    Id = _enemyIdCounter++,
                    Kind = kind,
                    Hp = 1,                      // bosses are one-hit in challenge stages
                    Delay = g * 1.9f + k * 0.14f,
                    Speed = speed,
                    Path = path,
                    Pos = new Vector2(-50, -50),
                });
            }
        }
    }

    // ------------------------------------------------------------------ main update

    private void UpdatePlaying(float dt, Input inp)
    {
        _formT += dt;
        UpdateFormation(dt);
        UpdatePlayer(dt, inp);
        UpdateEnemies(dt);
        if (!Challenge) UpdateDives(dt);
        UpdateShotsAndBombs(dt);
        UpdateRescue(dt);
        Collide();
        UpdateParticles(dt);
        Enemies.RemoveAll(e => !e.Alive);
        CheckStageEnd(dt);
    }

    private void UpdateFormation(float dt)
    {
        bool formed = AllEntered;
        float target = formed ? 0f : 6f;
        _swayAmp += (target - _swayAmp) * Math.Min(1f, dt * 1.5f);
        Sway = MathF.Sin(_formT * 0.9f) * _swayAmp;
        float breathTarget = formed ? (MathF.Sin(_formT * 1.4f) * 0.5f + 0.5f) * 0.16f : 0f;
        Breath += (breathTarget - Breath) * Math.Min(1f, dt * 3f);
    }

    public Vector2 SlotPos(Enemy e)
    {
        float x = Cx + Sway + (e.Col - 4.5f) * 15f * (1f + Breath);
        float y = 50f + e.Row * 14f * (1f + Breath * 0.7f);
        return new Vector2(x, y);
    }

    // ---- player ----

    public float PlayerHalfWidth => Dual ? 13f : 6.5f;

    private void UpdatePlayer(float dt, Input inp)
    {
        if (Invuln > 0) Invuln -= dt;
        if (_fireCd > 0) _fireCd -= dt;

        switch (PState)
        {
            case PlayerState.Active:
                float dir = (inp.Right ? 1f : 0f) - (inp.Left ? 1f : 0f);
                PX = Math.Clamp(PX + dir * 105f * dt, PlayerHalfWidth + 3f, W - PlayerHalfWidth - 3f);
                if (inp.Fire && _fireCd <= 0f) TryFire();
                break;

            case PlayerState.Captured:
                if (_captor is null || !_captor.Alive) { ReleaseCaptured(); break; }
                CaptureSpin += 540f * dt;
                var target = _captor.Pos + new Vector2(0, 15);
                var to = target - new Vector2(PX, PY);
                float step = 80f * dt;
                if (to.Length() <= step)
                {
                    _captor.HasCaptive = true;
                    _captor.State = EnemyState.Settling;     // fly home with the prize
                    _captor.Tractor = false;
                    _captor = null;
                    PState = PlayerState.Gone;
                    PTimer = 2.4f;
                }
                else
                {
                    var mv = Vector2.Normalize(to) * step;
                    PX += mv.X; PY += mv.Y;
                }
                break;

            case PlayerState.Gone:
                PTimer -= dt;
                if (PTimer <= 0)
                {
                    if (Lives > 0)
                    {
                        Lives--;
                        PState = PlayerState.Active;
                        PX = Cx; PY = PlayerY; Invuln = 2f;
                    }
                    else EndGame();
                }
                break;
        }
    }

    private void TryFire()
    {
        int count = Dual ? 2 : 1;
        int max = Dual ? 4 : 2;
        if (Shots.Count + count > max) return;
        for (int i = 0; i < count; i++)
        {
            float ox = Dual ? (i == 0 ? -8f : 8f) : 0f;
            Shots.Add(new Shot { Pos = new Vector2(PX + ox, PY - 9f), Vel = new Vector2(0, -280f) });
        }
        ShotsFired += count;
        _fireCd = 0.09f;
        Sound?.Invoke(Sfx.Shoot);
    }

    private void ReleaseCaptured()
    {
        PState = PlayerState.Active;
        PX = Math.Clamp(PX, 12f, W - 12f); PY = PlayerY; Invuln = 1.5f; _captor = null;
    }

    private void HitPlayer()
    {
        if (PState != PlayerState.Active || Invuln > 0f || Invincible) return;
        Explode(new Vector2(PX, PY), 26, big: true);
        Sound?.Invoke(Sfx.PlayerDie);
        if (Dual)
        {
            Dual = false;           // lose one fighter, keep the other
            Invuln = 2f;
            return;
        }
        PState = PlayerState.Gone;
        PTimer = 2.2f;
    }

    private void EndGame()
    {
        Phase = Phase.GameOver;
        PhaseTimer = 5f;
        if (Score > HighScore) { HighScore = Score; NewHighScore = true; HighScoreChanged?.Invoke(HighScore); }
    }

    // ---- enemies ----

    private void UpdateEnemies(float dt)
    {
        foreach (var e in Enemies)
        {
            switch (e.State)
            {
                case EnemyState.Waiting:
                    e.Delay -= dt;
                    if (e.Delay <= 0f)
                    {
                        e.State = Challenge ? EnemyState.Leaving : EnemyState.Entering;
                        e.Dist = 0;
                        e.Pos = e.Path!.At(0, out _);
                    }
                    break;

                case EnemyState.Entering:
                    if (Follow(e, dt)) e.State = EnemyState.Settling;
                    break;

                case EnemyState.Leaving:
                    if (Follow(e, dt)) e.Alive = false;
                    break;

                case EnemyState.Settling:
                    {
                        var slot = SlotPos(e);
                        var to = slot - e.Pos;
                        float dist = to.Length();
                        float step = (125f + Stage * 3f) * dt;
                        if (dist <= step + 0.01f)
                        {
                            e.Pos = slot; e.Angle = 0; e.State = EnemyState.Formation;
                        }
                        else
                        {
                            var d = to / dist;
                            e.Pos += d * step;
                            float heading = HeadingDeg(d);
                            e.Angle = LerpAngle(0f, heading, Math.Clamp(dist / 30f, 0f, 1f));
                        }
                        break;
                    }

                case EnemyState.Formation:
                    e.Pos = SlotPos(e);
                    e.Angle = 0;
                    break;

                case EnemyState.Diving:
                    if (Follow(e, dt))
                    {
                        if (e.Tractor)
                        {
                            e.State = EnemyState.Beaming;
                            e.Timer = 0f;
                            e.Angle = 0f;
                            Sound?.Invoke(Sfx.Beam);
                        }
                        else
                        {
                            // left through the bottom: reappear at the top, fly home
                            e.Pos = new Vector2(SlotPos(e).X, -14f);
                            e.State = EnemyState.Settling;
                        }
                        break;
                    }
                    MaybeDropBomb(e, dt);
                    break;

                case EnemyState.Beaming:
                    e.Timer += dt;
                    if (e.Timer >= 3.2f && PState != PlayerState.Captured)
                    {
                        e.Tractor = false;
                        float s = e.Pos.X < Cx ? 1f : -1f;
                        e.Path = new PathCurve(new[]
                        {
                            e.Pos, e.Pos + new Vector2(s * 18, 36),
                            new Vector2(Math.Clamp(e.Pos.X + s * 30, 12, W - 12), 245),
                            new Vector2(Math.Clamp(e.Pos.X + s * 10, 12, W - 12), 320),
                        });
                        e.Dist = 0; e.State = EnemyState.Diving; e.Bombs = 0;
                    }
                    break;
            }
        }
    }

    /// <summary>Advance along the entry/dive path. Returns true when the end is reached.</summary>
    private static bool Follow(Enemy e, float dt)
    {
        e.Dist += e.Speed * dt;
        e.Pos = e.Path!.At(e.Dist, out var dir);
        e.Angle = HeadingDeg(dir);
        return e.Dist >= e.Path.Length;
    }

    private static float HeadingDeg(Vector2 d) => MathF.Atan2(-d.X, d.Y) * (180f / MathF.PI);

    private static float LerpAngle(float a, float b, float t)
    {
        float diff = ((b - a + 540f) % 360f) - 180f;
        return a + diff * t;
    }

    private void MaybeDropBomb(Enemy e, float dt)
    {
        if (e.Bombs <= 0 || PState != PlayerState.Active) return;
        e.BombTimer -= dt;
        if (e.BombTimer > 0f) return;
        if (e.Pos.Y < 40f || e.Pos.Y > PlayerY - 55f) return;

        int cap = Math.Min(2 + Stage / 2, 8);
        if (Bombs.Count >= cap) return;

        float vy = 88f + Math.Min(Stage, 20) * 3f;
        float vx = Math.Clamp((PX - e.Pos.X) * 0.3f, -26f, 26f);
        Bombs.Add(new Shot { Pos = e.Pos + new Vector2(0, 6), Vel = new Vector2(vx, vy) });
        e.Bombs--;
        e.BombTimer = 0.4f + (float)_rng.NextDouble() * 0.5f;
    }

    private void UpdateDives(float dt)
    {
        if (!AllEntered || StageDone || PState != PlayerState.Active) return;
        _diveTimer -= dt;
        if (_diveTimer > 0f) return;

        int remaining = Enemies.Count(e => e.Alive);
        float interval = Math.Clamp(2.3f - 0.11f * Stage, 0.6f, 2.3f);
        if (remaining <= 8) interval *= 0.6f;
        _diveTimer = interval * (0.7f + (float)_rng.NextDouble() * 0.6f);

        int attackers = Enemies.Count(e => e.IsAttacking);
        int maxAttackers = Math.Min(2 + Stage / 3, 6);
        if (attackers >= maxAttackers) return;

        var candidates = Enemies.Where(e => e.State == EnemyState.Formation).ToList();
        if (candidates.Count == 0) return;

        int wave = Stage >= 3 && _rng.NextDouble() < 0.4 ? 2 : 1;
        for (int i = 0; i < wave && candidates.Count > 0 && attackers < maxAttackers; i++)
        {
            var e = candidates[_rng.Next(candidates.Count)];
            candidates.Remove(e);
            bool tractor = e.Kind == EnemyKind.Boss && !e.HasCaptive && !Dual
                           && !Enemies.Any(o => o.HasCaptive) && RescueShips.Count == 0
                           && !Enemies.Any(o => o.State == EnemyState.Beaming || o.Tractor)
                           && _rng.NextDouble() < 0.4;
            StartDive(e, tractor);
            attackers++;
        }
    }

    /// <summary>Test hook as well as the AI's dive launcher.</summary>
    public void StartDive(Enemy e, bool tractor)
    {
        float s = e.Pos.X < Cx ? -1f : 1f;
        var c = new List<Vector2> { e.Pos };
        if (tractor)
        {
            float tx = Math.Clamp(PX + (float)(_rng.NextDouble() * 60 - 30), 40f, W - 40f);
            c.Add(e.Pos + new Vector2(s * 12, -10));
            c.Add(e.Pos + new Vector2(s * 22, 12));
            c.Add(new Vector2((e.Pos.X + tx) / 2f, 100f));
            c.Add(new Vector2(tx, 150f));
            c.Add(new Vector2(tx, 176f));
        }
        else
        {
            float px = PX;
            c.Add(e.Pos + new Vector2(s * 12, -10));
            c.Add(e.Pos + new Vector2(s * 26, 6));
            c.Add(new Vector2(Lerp(e.Pos.X, px, 0.4f), 110f));
            c.Add(new Vector2(px - s * 30f, 165f));
            c.Add(new Vector2(px + s * 26f, 220f));
            c.Add(new Vector2(px + s * 4f, 270f));
            c.Add(new Vector2(px - s * 10f, 320f));
        }
        for (int i = 0; i < c.Count; i++) c[i] = new Vector2(Math.Clamp(c[i].X, 8f, W - 8f), c[i].Y);

        e.Path = new PathCurve(c);
        e.Dist = 0;
        e.State = EnemyState.Diving;
        e.Speed = Math.Min(105f + Stage * 5f, 185f);
        e.Tractor = tractor;
        e.Bombs = tractor ? 0 : 1 + (Stage >= 3 ? 1 : 0) + (Stage >= 8 ? 1 : 0);
        e.BombTimer = 0.2f + (float)_rng.NextDouble() * 0.5f;
        Sound?.Invoke(Sfx.Dive);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    // ---- shots, bombs, rescue ship ----

    private void UpdateShotsAndBombs(float dt)
    {
        for (int i = Shots.Count - 1; i >= 0; i--)
        {
            Shots[i].Pos += Shots[i].Vel * dt;
            if (Shots[i].Pos.Y < -8) Shots.RemoveAt(i);
        }
        for (int i = Bombs.Count - 1; i >= 0; i--)
        {
            Bombs[i].Pos += Bombs[i].Vel * dt;
            if (Bombs[i].Pos.Y > H + 8 || Bombs[i].Pos.X < -8 || Bombs[i].Pos.X > W + 8) Bombs.RemoveAt(i);
        }
    }

    private void UpdateRescue(float dt)
    {
        for (int i = RescueShips.Count - 1; i >= 0; i--)
        {
            var r = RescueShips[i];
            r.Angle += 420f * dt;
            r.Pos.Y += 75f * dt;
            float tx = PState == PlayerState.Active ? PX : Cx;
            r.Pos.X += (tx - r.Pos.X) * Math.Min(1f, dt * 2f);
            if (r.Pos.Y >= PlayerY)
            {
                if (PState == PlayerState.Active)
                {
                    Dual = true;
                    Rescues++;
                    AddScore(1000);
                    Sound?.Invoke(Sfx.Rescue);
                    RescueShips.RemoveAt(i);
                }
                else r.Pos.Y = PlayerY;     // wait for the next ship
            }
        }
    }

    // ---- collisions ----

    private void Collide()
    {
        // player shots vs enemies
        for (int i = Shots.Count - 1; i >= 0; i--)
        {
            var s = Shots[i];
            foreach (var e in Enemies)
            {
                if (!e.Alive || !e.Visible) continue;
                if (MathF.Abs(s.Pos.X - e.Pos.X) < 7f && MathF.Abs(s.Pos.Y - e.Pos.Y) < 8f)
                {
                    Shots.RemoveAt(i);
                    Hits++;
                    DamageEnemy(e);
                    break;
                }
            }
        }

        if (PState == PlayerState.Active)
        {
            // enemy bombs
            for (int i = Bombs.Count - 1; i >= 0; i--)
            {
                var b = Bombs[i];
                if (MathF.Abs(b.Pos.X - PX) < PlayerHalfWidth + 1.5f && MathF.Abs(b.Pos.Y - PY) < 8f && Invuln <= 0f)
                {
                    Bombs.RemoveAt(i);
                    HitPlayer();
                    if (PState != PlayerState.Active) break;
                }
            }

            // ramming
            if (PState == PlayerState.Active && Invuln <= 0f && !Invincible)
            {
                foreach (var e in Enemies)
                {
                    if (!e.Alive || e.State is EnemyState.Waiting or EnemyState.Formation) continue;
                    if (MathF.Abs(e.Pos.X - PX) < PlayerHalfWidth + 6f && MathF.Abs(e.Pos.Y - PY) < 11f)
                    {
                        KillEnemy(e);
                        HitPlayer();
                        break;
                    }
                }
            }

            // tractor beam
            foreach (var e in Enemies)
            {
                if (e.State != EnemyState.Beaming || PState != PlayerState.Active) continue;
                float len = BeamLength(e), top = e.Pos.Y + 8f;
                if (PY < top || PY > top + len) continue;
                if (MathF.Abs(PX - e.Pos.X) < BeamHalfWidth(PY - top))
                {
                    PState = PlayerState.Captured;
                    Dual = false;
                    _captor = e;
                    CaptureSpin = 0;
                    Captures++;
                    Sound?.Invoke(Sfx.Capture);
                }
            }
        }
    }

    public static float BeamHalfWidth(float distFromBoss) => 5f + distFromBoss / 90f * 21f;
    public float BeamLengthFor(Enemy e) => BeamLength(e);
    private static float BeamLength(Enemy e) => 92f * Math.Min(1f, e.Timer / 0.6f);

    private void DamageEnemy(Enemy e)
    {
        if (e.Hp > 1)
        {
            e.Hp--;
            Sound?.Invoke(Sfx.BossHit);
            SpawnSparks(e.Pos, 5);
            return;
        }
        KillEnemy(e);
    }

    private void KillEnemy(Enemy e)
    {
        e.Alive = false;
        bool diving = e.IsAttacking;
        int pts = e.Kind switch
        {
            EnemyKind.Bee => diving ? 100 : 50,
            EnemyKind.Butterfly => diving ? 160 : 80,
            _ => diving ? 400 : 150,
        };
        if (Challenge) { pts = 100; ChallengeHits++; }
        AddScore(pts);
        Explode(e.Pos, 12, big: false);
        Sound?.Invoke(Sfx.EnemyDie);

        if (e.HasCaptive)
        {
            e.HasCaptive = false;
            if (diving) RescueShips.Add(new RescueShip { Pos = e.Pos + new Vector2(0, 14) });
            else Explode(e.Pos + new Vector2(0, -13), 10, big: false);   // prisoner lost with its guard
        }
        if (_captor == e) ReleaseCaptured();
    }

    private void AddScore(int pts)
    {
        Score += pts;
        if (Score > HighScore) { HighScore = Score; NewHighScore = true; }
        while (Score >= _nextExtra)
        {
            Lives++;
            _nextExtra = _nextExtra == 20000 ? 70000 : _nextExtra + 70000;
            Sound?.Invoke(Sfx.ExtraLife);
        }
    }

    // ---- stage end ----

    private void CheckStageEnd(float dt)
    {
        if (!StageDone)
        {
            if (Enemies.Count == 0 && Phase == Phase.Playing)
            {
                StageDone = true;
                DoneTimer = Challenge ? 4.0f : 2.2f;
                if (Challenge)
                {
                    ChallengeBonus = ChallengeHits >= 40 ? 10000 : 0;
                    if (ChallengeBonus > 0) { AddScore(ChallengeBonus); Sound?.Invoke(Sfx.Perfect); }
                }
            }
            return;
        }
        DoneTimer -= dt;
        if (DoneTimer <= 0f && PState != PlayerState.Gone)
        {
            Stage++;
            BeginReady();
        }
    }

    // ---- particles & stars ----

    private void Explode(Vector2 at, int count, bool big)
    {
        for (int i = 0; i < count; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2;
            float sp = (big ? 30f : 18f) + (float)_rng.NextDouble() * (big ? 80f : 50f);
            var palette = _rng.Next(4);
            Particles.Add(new Particle
            {
                Pos = at,
                Vel = new Vector2(MathF.Cos((float)a), MathF.Sin((float)a)) * sp,
                MaxLife = 0.35f + (float)_rng.NextDouble() * (big ? 0.7f : 0.35f),
                R = 255,
                G = palette switch { 0 => (byte)255, 1 => (byte)200, 2 => (byte)120, _ => (byte)60 },
                B = palette == 0 ? (byte)200 : (byte)30,
                Size = big && i % 3 == 0 ? 2 : 1,
            });
            Particles[^1].Life = Particles[^1].MaxLife;
        }
    }

    private void SpawnSparks(Vector2 at, int count)
    {
        for (int i = 0; i < count; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2;
            Particles.Add(new Particle
            {
                Pos = at,
                Vel = new Vector2(MathF.Cos((float)a), MathF.Sin((float)a)) * 40f,
                Life = 0.25f, MaxLife = 0.25f, R = 255, G = 255, B = 255,
            });
        }
    }

    private void UpdateParticles(float dt)
    {
        for (int i = Particles.Count - 1; i >= 0; i--)
        {
            var p = Particles[i];
            p.Life -= dt;
            if (p.Life <= 0f) { Particles.RemoveAt(i); continue; }
            p.Pos += p.Vel * dt;
            p.Vel *= 1f - Math.Min(1f, dt * 2f);
        }
    }

    private void UpdateStars(float dt)
    {
        for (int i = 0; i < Stars.Length; i++)
        {
            ref var s = ref Stars[i];
            s.Y += s.Speed * dt;
            s.Phase += dt * (2f + s.Layer);
            if (s.Y > H) { s.Y -= H; s.X = _rng.Next(W); }
        }
    }
}
