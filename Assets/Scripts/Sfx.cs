using System.Threading.Tasks;
using UnityEngine;

// Sound effects and the background music, all synthesized at startup (no audio files), plus vibration.
// Recorded clips (a real "Bo Kata!" shout, wind, paper flutter) can replace them later; the call sites stay the same.
// Music: a looping Basant-style tune (raag Bhupali notes over a tanpura drone and a dholak beat), built on a worker
// thread so startup doesn't wait for it. Settings > Music turns it off.
public class Sfx : MonoBehaviour
{
    public enum Sound { Click, Free, Dock, ChhatDone, Wrong, Win, Lose, Coin, Crack, Wind, Cut, Undo, Freeze, BoKata, Purchase }

    static Sfx instance;
    AudioSource source, music;
    AudioClip[] clips;
    Task<float[]> musicTask;

    const int Rate = 44100;
    const int MusicRate = 22050;
    const float MusicVolume = 0.32f;

    static Sfx Instance
    {
        get
        {
            if (instance == null)
            {
                instance = new GameObject("Sfx").AddComponent<Sfx>();
                DontDestroyOnLoad(instance.gameObject);
            }
            return instance;
        }
    }

    public static void Play(Sound sound)
    {
        if (!Progress.Sound) return;
        Instance.source.PlayOneShot(Instance.clips[(int)sound]);
    }

    // Starts the music (once it is built) when Settings allow it; call again after the setting changes.
    public static void RefreshMusic() => Instance.UpdateMusic();

    public static void Buzz()
    {
#if UNITY_ANDROID || UNITY_IOS
        if (Progress.Vibration) Handheld.Vibrate();
#endif
    }

    void Awake()
    {
        // Without a listener Unity plays nothing; the scene's camera doesn't carry one.
        if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        music = gameObject.AddComponent<AudioSource>();
        music.playOnAwake = false;
        music.loop = true;
        music.volume = MusicVolume;
        clips = new[]
        {
            Tone("click", 0.05f, t => 900f, 0.25f),
            Tone("free", 0.18f, t => Mathf.Lerp(500f, 1100f, t), 0.3f),
            Tone("dock", 0.14f, t => 880f, 0.25f),
            Chord("chhat", new[] { 660f, 880f }, 0.12f),
            Tone("wrong", 0.25f, t => Mathf.Lerp(220f, 150f, t), 0.35f, square: true),
            Chord("win", new[] { 523f, 659f, 784f, 1047f }, 0.13f),
            Chord("lose", new[] { 392f, 330f, 262f }, 0.18f),
            Tone("coin", 0.06f, t => Mathf.Lerp(1500f, 1900f, t), 0.18f),
            Noise("crack", 0.16f, 0.45f, bright: true),
            Noise("wind", 0.9f, 0.3f, bright: false, swell: true),
            Snip(),
            Tone("undo", 0.22f, t => Mathf.Lerp(1000f, 450f, t), 0.28f),
            Chord("freeze", new[] { 1568f, 1319f, 1760f }, 0.07f),
            BoKata(),
            Chord("purchase", new[] { 784f, 988f, 1175f, 1568f }, 0.09f),
        };
        musicTask = Task.Run(BuildMusic);
    }

    void Update()
    {
        // The music clip is made off the main thread; AudioClip itself must be created here.
        if (musicTask != null && musicTask.IsCompleted)
        {
            if (!musicTask.IsFaulted)
            {
                var data = musicTask.Result;
                music.clip = AudioClip.Create("music", data.Length, 1, MusicRate, false);
                music.clip.SetData(data, 0);
            }
            musicTask = null;
            UpdateMusic();
        }
    }

    void UpdateMusic()
    {
        if (music.clip == null) return;
        if (Progress.Music && !music.isPlaying) music.Play();
        else if (!Progress.Music && music.isPlaying) music.Stop();
    }

    // A single note whose pitch follows freq(t) (t = 0..1), with a quick attack and decay.
    static AudioClip Tone(string name, float seconds, System.Func<float, float> freq, float volume, bool square = false)
    {
        int n = (int)(Rate * seconds);
        var data = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            phase += 2f * Mathf.PI * freq(t) / Rate;
            float wave = Mathf.Sin(phase);
            if (square) wave = Mathf.Sign(wave) * 0.5f;
            data[i] = wave * volume * Envelope(t);
        }
        return Clip(name, data, Rate);
    }

    // Notes played one after another.
    static AudioClip Chord(string name, float[] notes, float noteSeconds)
    {
        int per = (int)(Rate * noteSeconds);
        var data = new float[per * notes.Length + per];
        for (int k = 0; k < notes.Length; k++)
        {
            int len = per * 2;
            for (int i = 0; i < len && k * per + i < data.Length; i++)
            {
                float t = i / (float)len;
                data[k * per + i] += Mathf.Sin(2f * Mathf.PI * notes[k] * i / Rate) * 0.22f * Envelope(t);
            }
        }
        return Clip(name, data, Rate);
    }

    // Filtered noise: a sharp crack (bright) or a soft gust that swells and fades (wind).
    static AudioClip Noise(string name, float seconds, float volume, bool bright, bool swell = false)
    {
        int n = (int)(Rate * seconds);
        var data = new float[n];
        var rng = new System.Random(7);
        float low = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float white = (float)rng.NextDouble() * 2f - 1f;
            low += (white - low) * (bright ? 0.6f : 0.04f + 0.03f * Mathf.Sin(t * 9f));
            float env = swell ? Mathf.Sin(t * Mathf.PI) * Mathf.Sin(t * Mathf.PI) : Mathf.Min(1f, t * 200f) * Mathf.Pow(1f - t, 4f);
            data[i] = (bright ? white - low : low * 3f) * volume * env;
        }
        return Clip(name, data, Rate);
    }

    // Scissors: two quick bright clicks.
    static AudioClip Snip()
    {
        int n = (int)(Rate * 0.16f);
        var data = new float[n];
        var rng = new System.Random(3);
        foreach (float at in new[] { 0f, 0.07f })
        {
            int start = (int)(at * Rate), len = (int)(0.035f * Rate);
            for (int i = 0; i < len && start + i < n; i++)
            {
                float t = i / (float)len;
                data[start + i] += ((float)rng.NextDouble() * 2f - 1f) * 0.4f * (1f - t) * (1f - t)
                                   + Mathf.Sin(2f * Mathf.PI * 3200f * i / Rate) * 0.15f * (1f - t);
            }
        }
        return Clip("cut", data, Rate);
    }

    // "Bo Ka-ta!": three voice-like syllables (a buzzy tone through two vowel formants), rising like a shout.
    static AudioClip BoKata()
    {
        var syllables = new (float start, float len, float pitch, float f1, float f2)[]
        {
            (0.00f, 0.20f, 330f, 450f, 800f),   // "Bo"
            (0.24f, 0.13f, 392f, 700f, 1200f),  // "Ka"
            (0.40f, 0.34f, 440f, 750f, 1250f),  // "ta!"
        };
        int n = (int)(Rate * 0.8f);
        var data = new float[n];
        foreach (var s in syllables)
        {
            int start = (int)(s.start * Rate), len = (int)(s.len * Rate);
            float phase = 0f;
            for (int i = 0; i < len && start + i < n; i++)
            {
                float t = i / (float)len;
                float pitch = s.pitch * (1f + 0.06f * t) * (1f + 0.012f * Mathf.Sin(i * 2f * Mathf.PI * 6f / Rate));
                phase += pitch / Rate;
                float saw = 2f * (phase - Mathf.Floor(phase)) - 1f;
                float time = i / (float)Rate;
                float formant = Mathf.Sin(2f * Mathf.PI * s.f1 * time) * 0.6f + Mathf.Sin(2f * Mathf.PI * s.f2 * time) * 0.3f;
                float env = Mathf.Min(1f, t * 25f) * Mathf.Min(1f, (1f - t) * 6f);
                data[start + i] += (saw * 0.35f + saw * formant * 0.5f) * 0.3f * env;
            }
        }
        return Clip("bokata", data, Rate);
    }

    // Raag Bhupali (Sa Re Ga Pa Dha) over a Sa-Pa drone with a dholak pattern: 8 bars of 4 beats at 96 bpm.
    static float[] BuildMusic()
    {
        const float Bpm = 96f, Sa = 220f;
        float beat = 60f / Bpm;
        int beats = 32;
        int n = (int)(MusicRate * beat * beats);
        var data = new float[n];
        float[] scale = { 1f, 9f / 8f, 5f / 4f, 3f / 2f, 5f / 3f, 2f, 9f / 4f, 5f / 2f };
        // One note per half beat (index into scale, -1 = rest): a simple tune that rises and settles on Sa.
        int[] tune =
        {
            0, 2, 3, 4, 3, -1, 2, 1,   2, 3, 5, 4, 3, 2, 0, -1,
            3, 4, 5, 6, 5, 4, 3, -1,   4, 3, 2, 3, 1, 2, 0, -1,
            0, 2, 3, 4, 5, -1, 4, 3,   5, 6, 7, 6, 5, 4, 3, -1,
            2, 3, 4, 3, 2, 1, 2, -1,   3, 2, 1, 2, 0, -1, 0, -1,
        };
        float half = beat / 2f;
        for (int i = 0; i < n; i++)
        {
            float time = i / (float)MusicRate;
            // Tanpura-like drone: Sa and Pa an octave down, slowly breathing.
            float drone = (Mathf.Sin(2f * Mathf.PI * Sa / 2f * time) + 0.6f * Mathf.Sin(2f * Mathf.PI * Sa * 0.75f * time)
                           + 0.25f * Mathf.Sin(2f * Mathf.PI * Sa * time)) * (0.05f + 0.015f * Mathf.Sin(time * 1.3f));

            // Melody: a plucked, slightly bright tone (like a flute/harmonium mix).
            int step = (int)(time / half) % tune.Length;
            float inNote = time - Mathf.Floor(time / half) * half;
            float melody = 0f;
            if (tune[step] >= 0)
            {
                float f = Sa * scale[tune[step]];
                float env = Mathf.Min(1f, inNote * 60f) * Mathf.Exp(-inNote * 3.2f);
                melody = (Mathf.Sin(2f * Mathf.PI * f * time) + 0.3f * Mathf.Sin(4f * Mathf.PI * f * time)) * 0.14f * env;
            }

            // Dholak: low "dhin" on beats 1 and 3, high "na" on the off-beats.
            float inBeat = time - Mathf.Floor(time / beat) * beat;
            int beatIndex = (int)(time / beat) % 4;
            float drum = 0f;
            if (beatIndex == 0 || beatIndex == 2)
                drum += Mathf.Sin(2f * Mathf.PI * (90f + 60f * Mathf.Exp(-inBeat * 30f)) * inBeat) * Mathf.Exp(-inBeat * 9f) * 0.22f;
            float off = inBeat - half;
            if (off >= 0f)
                drum += Mathf.Sin(2f * Mathf.PI * 480f * off) * Mathf.Exp(-off * 40f) * 0.07f;

            data[i] = drone + melody + drum;
        }
        // Short fades at both ends so the loop point doesn't click.
        int fade = MusicRate / 50;
        for (int i = 0; i < fade; i++)
        {
            float k = i / (float)fade;
            data[i] *= k;
            data[n - 1 - i] *= k;
        }
        return data;
    }

    static AudioClip Clip(string name, float[] data, int rate)
    {
        var clip = AudioClip.Create(name, data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static float Envelope(float t) => Mathf.Min(1f, t * 40f) * (1f - t) * (1f - t);
}
