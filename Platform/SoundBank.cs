using Raylib_cs;

namespace StarBlaster;

/// <summary>Chip-tune style effects synthesised at start-up, so the game ships with no asset files.</summary>
public sealed unsafe class SoundBank : IDisposable
{
    private const int Rate = 22050;
    private readonly Dictionary<Sfx, Sound> _sounds = new();
    private readonly bool _ready;

    public SoundBank()
    {
        try
        {
            Raylib.InitAudioDevice();
            _ready = Raylib.IsAudioDeviceReady();
        }
        catch { _ready = false; }
        if (!_ready) return;

        _sounds[Sfx.Shoot] = Build(0.09f, (t, d) => Square(Sweep(1300, 420, t, d)) * Decay(t, d) * 0.18f);
        _sounds[Sfx.EnemyDie] = Build(0.22f, (t, d) => (Noise(t) * 0.5f + Square(Sweep(300, 80, t, d)) * 0.5f) * Decay(t, d) * 0.28f);
        _sounds[Sfx.BossHit] = Build(0.12f, (t, d) => Square(Sweep(220, 160, t, d)) * Decay(t, d) * 0.28f);
        _sounds[Sfx.PlayerDie] = Build(0.8f, (t, d) => (Noise(t * 0.5f) * 0.7f + Square(Sweep(220, 40, t, d)) * 0.3f) * Decay(t, d) * 0.35f);
        _sounds[Sfx.Dive] = Build(0.28f, (t, d) => Square(Sweep(500, 1000, t, d) * (1f + 0.04f * MathF.Sin(t * 120f))) * Decay(t, d) * 0.12f);
        _sounds[Sfx.Beam] = Build(1.0f, (t, d) => Square(180f + 60f * MathF.Sin(t * 40f) + t * 120f) * 0.14f * (1f - t / d * 0.4f));
        _sounds[Sfx.Capture] = Build(0.7f, (t, d) => Square(Sweep(900, 120, t, d)) * Decay(t, d) * 0.2f);
        _sounds[Sfx.Rescue] = Build(0.5f, (t, d) => Square(Arp(t, 523, 659, 784, 1047)) * 0.18f * Decay(t, d));
        _sounds[Sfx.ExtraLife] = Build(0.6f, (t, d) => Square(Arp(t * 1.2f, 659, 784, 988, 1319, 1568)) * 0.16f * Decay(t, d));
        _sounds[Sfx.Start] = Build(1.0f, (t, d) => Square(Arp(t * 1.6f, 392, 523, 659, 784, 659, 784, 1047)) * 0.16f * Decay(t, d));
        _sounds[Sfx.Perfect] = Build(1.2f, (t, d) => Square(Arp(t * 1.8f, 523, 659, 784, 1047, 784, 1047, 1319, 1568)) * 0.16f * Decay(t, d));
    }

    public void Play(Sfx sfx)
    {
        if (_ready && _sounds.TryGetValue(sfx, out var s)) Raylib.PlaySound(s);
    }

    // ---- tiny synth helpers (phase is accumulated per sample via the closure in Build) ----

    private static float Decay(float t, float d) => MathF.Max(0f, 1f - t / d);
    private static float Sweep(float f0, float f1, float t, float d) => f0 + (f1 - f0) * (t / d);

    [ThreadStatic] private static float _phase;
    private static float Square(float freq)
    {
        _phase += freq / Rate;
        _phase -= MathF.Floor(_phase);
        return _phase < 0.5f ? 1f : -1f;
    }

    private static uint _seed = 12345;
    private static float Noise(float _)
    {
        _seed = _seed * 1664525u + 1013904223u;
        return ((_seed >> 16) & 0x7fff) / 16384f - 1f;
    }

    private static float Arp(float t, params float[] notes) => notes[Math.Min(notes.Length - 1, (int)(t * 12f))];

    private static Sound Build(float seconds, Func<float, float, float> fn)
    {
        int frames = (int)(seconds * Rate);
        var data = new short[frames];
        _phase = 0f;
        for (int i = 0; i < frames; i++)
        {
            float v = Math.Clamp(fn(i / (float)Rate, seconds), -1f, 1f);
            data[i] = (short)(v * short.MaxValue);
        }
        fixed (short* p = data)
        {
            var wave = new Wave
            {
                FrameCount = (uint)frames, SampleRate = Rate, SampleSize = 16, Channels = 1, Data = p,
            };
            return Raylib.LoadSoundFromWave(wave);   // copies the buffer
        }
    }

    public void Dispose()
    {
        if (!_ready) return;
        foreach (var s in _sounds.Values) Raylib.UnloadSound(s);
        Raylib.CloseAudioDevice();
    }
}
