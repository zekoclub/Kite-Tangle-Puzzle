using UnityEngine;

// Placeholder sound effects synthesized at startup, plus vibration. Replace the clips with recorded sounds
// (the "Bo Kata!" shout, wind, paper flutter) when they exist; the call sites stay the same.
public class Sfx : MonoBehaviour
{
    public enum Sound { Click, Free, Dock, ChhatDone, Wrong, Win, Lose, Coin }

    static Sfx instance;
    AudioSource source;
    AudioClip[] clips;

    public static void Play(Sound sound)
    {
        if (!Progress.Sound) return;
        if (instance == null)
        {
            instance = new GameObject("Sfx").AddComponent<Sfx>();
            DontDestroyOnLoad(instance.gameObject);
        }
        instance.source.PlayOneShot(instance.clips[(int)sound]);
    }

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
        };
    }

    const int Rate = 44100;

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
        var clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
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
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static float Envelope(float t) => Mathf.Min(1f, t * 40f) * (1f - t) * (1f - t);
}
