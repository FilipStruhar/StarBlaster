using Raylib_cs;

namespace StarBlaster;

/// <summary>Pixel-art sprites defined as text, uploaded to GPU textures once at start-up.</summary>
public sealed class Sprites : IDisposable
{
    private static readonly Dictionary<char, Color> Normal = new()
    {
        ['W'] = new Color(240, 240, 245, 255),
        ['R'] = new Color(235, 50, 50, 255),
        ['B'] = new Color(50, 90, 255, 255),
        ['Y'] = new Color(255, 215, 40, 255),
        ['G'] = new Color(60, 205, 80, 255),
    };

    private static Dictionary<char, Color> With(char key, Color c)
    {
        var d = new Dictionary<char, Color>(Normal) { [key] = c };
        return d;
    }

    private static readonly string[] PlayerRows =
    {
        "......W......",
        "......W......",
        ".....WWW.....",
        "..R..WBW..R..",
        "..R..WBW..R..",
        ".WR.WWBWW.RW.",
        ".WRWWWWWWWRW.",
        ".WWWWWWWWWWW.",
        "WWWWRWWWRWWWW",
        "WWW.RWWWR.WWW",
        "WW..RR.RR..WW",
        "R...........R",
    };

    private static readonly string[] BeeA =
    {
        "....Y...Y....",
        ".Y...YYY...Y.",
        "YY..YBYBY..YY",
        "YYYYYYYYYYYYY",
        ".YYYYRRRYYYY.",
        "..YY.YYY.YY..",
        "...Y.....Y...",
        "....Y...Y....",
    };

    private static readonly string[] BeeB =
    {
        "....Y...Y....",
        "....YYYYY....",
        "...YBYYYBY...",
        ".YYYYYYYYYYY.",
        "YYYYYRRRYYYYY",
        "YY..YYYYY..YY",
        "Y....Y.Y....Y",
        "......Y......",
    };

    private static readonly string[] ButterflyA =
    {
        "..R.......R..",
        ".RRR.....RRR.",
        "RRBRR.R.RRBRR",
        "RRRRRRWRRRRRR",
        "RBRRRRRRRRRBR",
        ".RRRRRRRRRRR.",
        "..RRR.R.RRR..",
        "...R..W..R...",
    };

    private static readonly string[] ButterflyB =
    {
        ".............",
        "RR.........RR",
        "RRRR..R..RRRR",
        "RRBRRRWRRRBRR",
        ".RRRRRRRRRRR.",
        "..RRR.R.RRR..",
        "...R..W..R...",
        ".............",
    };

    private static readonly string[] Boss =
    {
        "...Y.....Y...",
        "..YYY...YYY..",
        ".YGGGGGGGGGY.",
        "YGGWGGGGGWGGY",
        "YGGGGGGGGGGGY",
        ".GGGGRRRGGGG.",
        "..GGG.R.GGG..",
        "..G..GGG..G..",
        ".G...G.G...G.",
        "G...........G",
    };

    public Texture2D Player, CaptivePlayer;
    public Texture2D[] Bee = new Texture2D[2];
    public Texture2D[] Butterfly = new Texture2D[2];
    public Texture2D[] BossFull = new Texture2D[2];
    public Texture2D[] BossHurt = new Texture2D[2];

    public Sprites()
    {
        var captiveRed = new Dictionary<char, Color>(Normal)
        {
            ['W'] = new Color(255, 120, 120, 255), ['B'] = new Color(255, 60, 60, 255), ['R'] = new Color(255, 255, 255, 255),
        };

        Player = Make(PlayerRows, Normal);
        CaptivePlayer = Make(PlayerRows, captiveRed);
        Bee[0] = Make(BeeA, Normal); Bee[1] = Make(BeeB, Normal);
        Butterfly[0] = Make(ButterflyA, Normal); Butterfly[1] = Make(ButterflyB, Normal);

        // The boss flaps its horns by shifting one row of pixels; hurt state turns it blue-purple.
        BossFull[0] = Make(Boss, Normal);
        BossFull[1] = Make(FlapBoss(), Normal);
        var hurt = With('G', new Color(130, 110, 255, 255));
        BossHurt[0] = Make(Boss, hurt);
        BossHurt[1] = Make(FlapBoss(), hurt);
    }

    private static string[] FlapBoss()
    {
        var rows = (string[])Boss.Clone();
        rows[0] = "..Y.......Y..";
        rows[1] = ".YYY.....YYY.";
        return rows;
    }

    private static Texture2D Make(string[] rows, Dictionary<char, Color> palette)
    {
        int h = rows.Length, w = rows[0].Length;
        var img = Raylib.GenImageColor(w, h, new Color(0, 0, 0, 0));
        for (int y = 0; y < h; y++)
        {
            if (rows[y].Length != w) throw new InvalidOperationException($"sprite row {y} has width {rows[y].Length}, expected {w}");
            for (int x = 0; x < w; x++)
            {
                char c = rows[y][x];
                if (c != '.') Raylib.ImageDrawPixel(ref img, x, y, palette[c]);
            }
        }
        var tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(tex, TextureFilter.Point);
        return tex;
    }

    public void Dispose()
    {
        foreach (var t in new[] { Player, CaptivePlayer }.Concat(Bee).Concat(Butterfly).Concat(BossFull).Concat(BossHurt))
            Raylib.UnloadTexture(t);
    }
}
