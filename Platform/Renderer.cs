using System.Numerics;
using Raylib_cs;

namespace StarBlaster;

/// <summary>Draws the game into a 224x288 off-screen texture, then scales it to the window.</summary>
public sealed class Renderer : IDisposable
{
    private static readonly Color Black = new(0, 0, 0, 255);
    private static readonly Color White = new(240, 240, 245, 255);
    private static readonly Color Red = new(235, 60, 60, 255);
    private static readonly Color Cyan = new(80, 230, 255, 255);
    private static readonly Color Yellow = new(255, 215, 40, 255);

    private readonly RenderTexture2D _rt;
    private readonly Sprites _sprites;
    public bool Scanlines = true;

    public Renderer()
    {
        _rt = Raylib.LoadRenderTexture(Game.W, Game.H);
        Raylib.SetTextureFilter(_rt.Texture, TextureFilter.Point);
        _sprites = new Sprites();
    }

    public void Dispose()
    {
        _sprites.Dispose();
        Raylib.UnloadRenderTexture(_rt);
    }

    public void SaveFrame(string path)
    {
        var img = Raylib.LoadImageFromTexture(_rt.Texture);
        Raylib.ImageFlipVertical(ref img);
        Raylib.ExportImage(img, path);
        Raylib.UnloadImage(img);
    }

    // ------------------------------------------------------------------ frame

    public void Draw(Game g)
    {
        Raylib.BeginTextureMode(_rt);
        Raylib.ClearBackground(new Color(4, 4, 18, 255));

        DrawStars(g);
        switch (g.Phase)
        {
            case Phase.Title: DrawTitle(g); break;
            default: DrawWorld(g); DrawHud(g); DrawOverlay(g); break;
        }
        Raylib.EndTextureMode();
    }

    public void Present()
    {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        float scale = MathF.Min(sw / (float)Game.W, sh / (float)Game.H);
        if (scale >= 1f) scale = MathF.Floor(scale);        // crisp integer scaling when it fits
        float dw = Game.W * scale, dh = Game.H * scale;
        float dx = MathF.Floor((sw - dw) / 2f), dy = MathF.Floor((sh - dh) / 2f);

        Raylib.BeginDrawing();
        Raylib.ClearBackground(Black);
        Raylib.DrawTexturePro(_rt.Texture,
            new Rectangle(0, 0, Game.W, -Game.H),
            new Rectangle(dx, dy, dw, dh), Vector2.Zero, 0f, Color.White);

        if (Scanlines && scale >= 3f)
        {
            var line = new Color(0, 0, 0, 55);
            int step = (int)scale;
            for (int y = (int)dy; y < dy + dh; y += step)
                Raylib.DrawRectangle((int)dx, y + step - 1, (int)dw, 1, line);
        }
        Raylib.EndDrawing();
    }

    // ------------------------------------------------------------------ pieces

    private void DrawStars(Game g)
    {
        foreach (var s in g.Stars)
        {
            if (MathF.Sin(s.Phase) < -0.35f) continue;       // twinkle
            var c = s.Layer switch
            {
                0 => new Color(70, 80, 140, 255),
                1 => new Color(120, 190, 255, 255),
                _ => new Color(255, 255, 255, 255),
            };
            Raylib.DrawRectangle((int)s.X, (int)s.Y, 1, s.Layer == 2 ? 2 : 1, c);
        }
    }

    private void Sprite(Texture2D tex, Vector2 center, float angle = 0f)
    {
        float w = tex.Width, h = tex.Height;
        var snap = new Vector2(MathF.Round(center.X), MathF.Round(center.Y));
        Raylib.DrawTexturePro(tex, new Rectangle(0, 0, w, h), new Rectangle(snap.X, snap.Y, w, h),
            new Vector2(w / 2f, h / 2f), angle, Color.White);
    }

    private void DrawWorld(Game g)
    {
        // tractor beams sit behind the ships
        foreach (var e in g.Enemies)
        {
            if (e.State != EnemyState.Beaming) continue;
            float top = e.Pos.Y + 8f, len = g.BeamLengthFor(e);
            int tick = (int)(g.Time * 24f);
            for (float off = 0; off < len; off += 3f)
            {
                float half = Game.BeamHalfWidth(off);
                int band = ((int)(off / 3f) + tick) % 3;
                var c = band switch
                {
                    0 => new Color(60, 120, 255, 230),
                    1 => new Color(120, 220, 255, 200),
                    _ => new Color(30, 60, 170, 120),
                };
                Raylib.DrawRectangle((int)(e.Pos.X - half), (int)(top + off), (int)(half * 2), 2, c);
            }
        }

        foreach (var e in g.Enemies)
        {
            if (!e.Alive || !e.Visible) continue;
            int frame = ((int)(g.Time * 3f) + e.Id) & 1;
            var tex = e.Kind switch
            {
                EnemyKind.Bee => _sprites.Bee[frame],
                EnemyKind.Butterfly => _sprites.Butterfly[frame],
                _ => (e.Hp < 2 && !g.Challenge ? _sprites.BossHurt : _sprites.BossFull)[frame],
            };
            Sprite(tex, e.Pos, e.Angle);
            if (e.HasCaptive)
            {
                float a = e.Angle * MathF.PI / 180f;
                // the prisoner rides 15px "above" the boss, rotated with its heading
                var off = new Vector2(15f * MathF.Sin(a), -15f * MathF.Cos(a));
                Sprite(_sprites.CaptivePlayer, e.Pos + off, e.Angle);
            }
        }

        foreach (var r in g.RescueShips) Sprite(_sprites.CaptivePlayer, r.Pos, r.Angle);

        // player
        if (g.PState == PlayerState.Active)
        {
            bool blink = g.Invuln > 0f && ((int)(g.Time * 14f) & 1) == 0;
            if (!blink)
            {
                if (g.Dual)
                {
                    Sprite(_sprites.Player, new Vector2(g.PX - 8f, g.PY));
                    Sprite(_sprites.Player, new Vector2(g.PX + 8f, g.PY));
                }
                else Sprite(_sprites.Player, new Vector2(g.PX, g.PY));
            }
        }
        else if (g.PState == PlayerState.Captured)
        {
            Sprite(_sprites.CaptivePlayer, new Vector2(g.PX, g.PY), g.CaptureSpin);
        }

        foreach (var s in g.Shots)
        {
            Raylib.DrawRectangle((int)s.Pos.X, (int)s.Pos.Y - 3, 1, 6, White);
            Raylib.DrawRectangle((int)s.Pos.X - 1, (int)s.Pos.Y - 1, 3, 3, new Color(255, 80, 80, 255));
            Raylib.DrawRectangle((int)s.Pos.X, (int)s.Pos.Y - 2, 1, 4, White);
        }

        foreach (var b in g.Bombs)
        {
            bool alt = ((int)(g.Time * 20f) & 1) == 0;
            Raylib.DrawRectangle((int)b.Pos.X - 1, (int)b.Pos.Y - 3, 3, 6, alt ? Yellow : Red);
            Raylib.DrawRectangle((int)b.Pos.X, (int)b.Pos.Y - 3, 1, 6, White);
        }

        foreach (var p in g.Particles)
        {
            float k = Math.Clamp(p.Life / p.MaxLife, 0f, 1f);
            var c = new Color(p.R, (int)(p.G * (0.4f + 0.6f * k)), p.B, (int)(255 * Math.Min(1f, k * 1.6f)));
            Raylib.DrawRectangle((int)p.Pos.X, (int)p.Pos.Y, p.Size, p.Size, c);
        }
    }

    private void DrawHud(Game g)
    {
        bool on = ((int)(g.Time * 2f) & 1) == 0 || g.Phase != Phase.Playing;
        if (on) Raylib.DrawText("1UP", 16, 2, 10, Red);
        Raylib.DrawText(g.Score.ToString(), 16, 12, 10, White);

        TextCentered("HIGH SCORE", 2, Red, 112);
        TextCentered(g.HighScore.ToString(), 12, White, 112);

        for (int i = 0; i < Math.Min(g.Lives, 7); i++)
            Sprite(_sprites.Player, new Vector2(10 + i * 15, 279));

        string stage = $"STAGE {g.Stage}";
        Raylib.DrawText(stage, Game.W - 4 - Raylib.MeasureText(stage, 10), 275, 10, Cyan);
    }

    private void DrawOverlay(Game g)
    {
        if (g.Phase == Phase.Ready)
        {
            if (g.Challenge)
            {
                TextCentered("CHALLENGING STAGE", 135, Cyan);
                TextCentered("GET A PERFECT FOR 10000", 150, Yellow);
            }
            else
            {
                TextCentered($"STAGE {g.Stage}", 135, Cyan);
                if (g.Stage == 1) TextCentered("GET READY", 150, Yellow);
            }
        }
        else if (g.Phase == Phase.Playing)
        {
            if (g.Paused) TextCentered("PAUSED", 135, Yellow);
            if (g.StageDone && g.Challenge)
            {
                TextCentered("NUMBER OF HITS", 120, Cyan);
                TextCentered(g.ChallengeHits.ToString(), 134, White);
                if (g.ChallengeBonus > 0)
                {
                    TextCentered("PERFECT !", 154, Red);
                    TextCentered($"SPECIAL BONUS {g.ChallengeBonus}", 168, Yellow);
                }
                else TextCentered($"BONUS {g.ChallengeHits * 100}", 154, Yellow);
            }
            else if (g.StageDone) TextCentered("STAGE CLEAR", 135, Yellow);
        }
        else if (g.Phase == Phase.GameOver)
        {
            TextCentered("GAME OVER", 100, Red, size: 20);
            int ratio = g.ShotsFired == 0 ? 0 : g.Hits * 100 / g.ShotsFired;
            TextCentered("- RESULTS -", 140, Cyan);
            TextCentered($"SHOTS FIRED   {g.ShotsFired}", 158, White);
            TextCentered($"NUMBER OF HITS   {g.Hits}", 172, White);
            TextCentered($"HIT-MISS RATIO   {ratio}%", 186, Yellow);
            if (g.NewHighScore) TextCentered("NEW HIGH SCORE!", 210, Red);
        }
    }

    private void DrawTitle(Game g)
    {
        float t = g.Time;
        var c1 = Hue((t * 0.35f) % 1f);
        var c2 = Hue((t * 0.35f + 0.5f) % 1f);
        TextCentered("STAR", 30, c1, size: 30);
        TextCentered("BLASTER", 62, c2, size: 30);

        TextCentered("HIGH SCORE", 112, Red);
        TextCentered(g.HighScore.ToString(), 124, White);

        // score table
        TextCentered("- SCORE ADVANCE TABLE -", 150, Cyan);
        DrawTableRow(_sprites.BossFull[(int)(t * 3) & 1], 0, "150 / 400");
        DrawTableRow(_sprites.Butterfly[(int)(t * 3) & 1], 1, "80 / 160");
        DrawTableRow(_sprites.Bee[(int)(t * 3) & 1], 2, "50 / 100");

        if (((int)(t * 2f) & 1) == 0) TextCentered("PUSH SPACE OR ENTER", 214, Yellow);
        var dim = new Color(150, 160, 190, 255);
        TextCentered("ARROWS / A-D MOVE   SPACE FIRE", 236, White);
        TextCentered("P PAUSE  F11 FULLSCREEN  ESC QUIT", 249, dim);
        TextCentered("SHOOT A BOSS CARRYING YOUR SHIP", 264, Yellow);
        TextCentered("TO GET DUAL FIGHTERS", 275, Yellow);
    }

    private void DrawTableRow(Texture2D tex, int row, string pts)
    {
        int y = 170 + row * 14;
        Sprite(tex, new Vector2(Game.W / 2f - 42, y + 4));
        Raylib.DrawText(pts, Game.W / 2 - 26, y - 1, 10, White);
    }

    // ------------------------------------------------------------------ text

    private static void TextCentered(string s, int y, Color c, int cx = Game.W / 2, int size = 10)
    {
        int w = Raylib.MeasureText(s, size);
        Raylib.DrawText(s, cx - w / 2, y, size, c);
    }

    private static Color Hue(float h)
    {
        float r = Math.Clamp(MathF.Abs(h * 6f - 3f) - 1f, 0f, 1f);
        float g = Math.Clamp(2f - MathF.Abs(h * 6f - 2f), 0f, 1f);
        float b = Math.Clamp(2f - MathF.Abs(h * 6f - 4f), 0f, 1f);
        return new Color((int)(80 + r * 175), (int)(80 + g * 175), (int)(80 + b * 175), 255);
    }
}
