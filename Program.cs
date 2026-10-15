using Raylib_cs;

namespace StarBlaster;

internal static class Program
{
    private static string HighScorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarBlaster", "highscore.txt");

    private static int Main(string[] args)
    {
        if (args.Contains("--selftest")) return SelfTest.Run();

        string? shot = Arg(args, "--shot");
        return shot is null ? Play() : Screenshot(shot, args);
    }

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    // ------------------------------------------------------------------ normal play

    private static int Play()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint);
        Raylib.InitWindow(Game.W * 2, Game.H * 2, "Star Blaster");
        Raylib.SetExitKey(KeyboardKey.Null);
        Raylib.SetTargetFPS(60);
        Raylib.SetWindowMinSize(Game.W, Game.H);

        // pick the largest whole-number scale that fits comfortably on this monitor
        int monitorH = Raylib.GetMonitorHeight(Raylib.GetCurrentMonitor());
        int scale = monitorH > 0 ? Math.Clamp((monitorH - 140) / Game.H, 2, 4) : 3;
        Raylib.SetWindowSize(Game.W * scale, Game.H * scale);

        using var sounds = new SoundBank();
        using var renderer = new Renderer();
        var game = new Game { HighScore = LoadHighScore() };
        game.Sound += sounds.Play;
        game.HighScoreChanged += SaveHighScore;

        while (!Raylib.WindowShouldClose())
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
            {
                if (game.Phase == Phase.Title) break;
                game.AbortToTitle();
            }
            if (Raylib.IsKeyPressed(KeyboardKey.F11)) Raylib.ToggleBorderlessWindowed();
            if (Raylib.IsKeyPressed(KeyboardKey.F1)) renderer.Scanlines = !renderer.Scanlines;

            game.Update(Math.Min(Raylib.GetFrameTime(), 1f / 30f), ReadInput());
            renderer.Draw(game);
            renderer.Present();
        }

        SaveHighScore(game.HighScore);
        Raylib.CloseWindow();
        return 0;
    }

    private static Input ReadInput()
    {
        var i = new Input
        {
            Left = Raylib.IsKeyDown(KeyboardKey.Left) || Raylib.IsKeyDown(KeyboardKey.A),
            Right = Raylib.IsKeyDown(KeyboardKey.Right) || Raylib.IsKeyDown(KeyboardKey.D),
            Fire = Raylib.IsKeyDown(KeyboardKey.Space) || Raylib.IsKeyDown(KeyboardKey.Z) || Raylib.IsKeyDown(KeyboardKey.LeftControl),
            Start = Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space),
            Pause = Raylib.IsKeyPressed(KeyboardKey.P),
        };

        if (Raylib.IsGamepadAvailable(0))
        {
            float ax = Raylib.GetGamepadAxisMovement(0, GamepadAxis.LeftX);
            i.Left |= ax < -0.3f || Raylib.IsGamepadButtonDown(0, GamepadButton.LeftFaceLeft);
            i.Right |= ax > 0.3f || Raylib.IsGamepadButtonDown(0, GamepadButton.LeftFaceRight);
            i.Fire |= Raylib.IsGamepadButtonDown(0, GamepadButton.RightFaceDown);
            i.Start |= Raylib.IsGamepadButtonPressed(0, GamepadButton.MiddleRight)
                    || Raylib.IsGamepadButtonPressed(0, GamepadButton.RightFaceDown);
            i.Pause |= Raylib.IsGamepadButtonPressed(0, GamepadButton.MiddleLeft);
        }
        return i;
    }

    private static int LoadHighScore()
    {
        try { return int.TryParse(File.ReadAllText(HighScorePath), out int v) ? v : 0; }
        catch { return 0; }
    }

    private static void SaveHighScore(int score)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HighScorePath)!);
            File.WriteAllText(HighScorePath, score.ToString());
        }
        catch { /* read-only location: the high score just won't persist */ }
    }

    // ------------------------------------------------------------------ dev: bot-driven screenshot

    private static int Screenshot(string path, string[] args)
    {
        int frames = int.TryParse(Arg(args, "--frames"), out var f) ? f : 600;
        int seed = int.TryParse(Arg(args, "--seed"), out var s) ? s : 1;
        int stage = int.TryParse(Arg(args, "--stage"), out var st) ? st : 1;
        bool title = args.Contains("--title");

        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(Game.W * 3, Game.H * 3, "Star Blaster");
        using var renderer = new Renderer();
        var game = new Game(seed) { Invincible = args.Contains("--god"), HighScore = 12340 };
        if (!title) game.NewGame(stage);

        for (int n = 0; n < frames; n++)
            game.Update(1f / 60f, title ? default : Bot.Think(game));

        renderer.Draw(game);
        renderer.SaveFrame(path);
        Raylib.CloseWindow();
        Console.WriteLine($"saved {path} (phase {game.Phase}, stage {game.Stage}, score {game.Score})");
        return 0;
    }
}
