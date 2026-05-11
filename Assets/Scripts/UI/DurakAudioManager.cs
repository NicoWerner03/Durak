using UnityEngine;

namespace DurakGame.UI
{
    public enum SfxKind
    {
        ButtonClick = 0,
        CardSelect  = 1,
        CardPlay    = 2,
        CardDefend  = 3,
        CardTake    = 4,
        TurnStart   = 5,
        MatchWin    = 6,
        MatchLose   = 7,
    }

    // Procedurally synthesizes short sound effects so the project does not
    // need any audio asset files. One persistent AudioSource plays clips via
    // PlayOneShot so concurrent SFX overlap cleanly.
    public class DurakAudioManager : MonoBehaviour
    {
        public static DurakAudioManager Instance { get; private set; }

        private const int    SampleRate    = 44100;
        private const string VolumePrefKey = "durak_sfx_volume";
        private const string MutedPrefKey  = "durak_sfx_muted";
        private const float  DefaultVolume = 0.55f;

        private AudioSource _source;
        private AudioClip _click;
        private AudioClip _select;
        private AudioClip _play;
        private AudioClip _defend;
        private AudioClip _take;
        private AudioClip _turn;
        private AudioClip _win;
        private AudioClip _lose;

        private bool _muted;

        public bool Muted
        {
            get => _muted;
            set
            {
                _muted = value;
                PlayerPrefs.SetInt(MutedPrefKey, value ? 1 : 0);
            }
        }

        public float Volume
        {
            get => _source != null ? _source.volume : DefaultVolume;
            set
            {
                var v = Mathf.Clamp01(value);
                if (_source != null) _source.volume = v;
                PlayerPrefs.SetFloat(VolumePrefKey, v);
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.volume = PlayerPrefs.GetFloat(VolumePrefKey, DefaultVolume);
            _muted         = PlayerPrefs.GetInt(MutedPrefKey, 0) == 1;

            _click  = SynthClick (0.06f, 880f, decay: 0.020f, noise: 0.20f);
            _select = SynthClick (0.08f, 1320f, decay: 0.025f, noise: 0.12f);
            _play   = SynthSweep (0.18f, 750f, 220f, decay: 0.10f, noise: 0.55f);
            _defend = SynthClick (0.12f, 520f, decay: 0.040f, noise: 0.30f);
            _take   = SynthSweep (0.32f, 600f, 180f, decay: 0.18f, noise: 0.65f);
            _turn   = SynthChord (0.28f, new[] { 660f, 880f }, decay: 0.14f);
            _win    = SynthChord (0.65f, new[] { 523.25f, 659.25f, 783.99f }, decay: 0.28f);
            _lose   = SynthChord (0.60f, new[] { 261.63f, 246.94f, 207.65f }, decay: 0.28f);
        }

        public static void PlaySfx(SfxKind kind)
        {
            var instance = Instance;
            if (instance == null || instance._muted || instance._source == null) return;
            var clip = instance.ClipFor(kind);
            if (clip != null) instance._source.PlayOneShot(clip);
        }

        private AudioClip ClipFor(SfxKind kind)
        {
            switch (kind)
            {
                case SfxKind.ButtonClick: return _click;
                case SfxKind.CardSelect:  return _select;
                case SfxKind.CardPlay:    return _play;
                case SfxKind.CardDefend:  return _defend;
                case SfxKind.CardTake:    return _take;
                case SfxKind.TurnStart:   return _turn;
                case SfxKind.MatchWin:    return _win;
                case SfxKind.MatchLose:   return _lose;
                default: return null;
            }
        }

        // Short percussive blip with exponential decay and a touch of noise.
        private static AudioClip SynthClick(float duration, float freq, float decay, float noise)
        {
            var samples = Mathf.CeilToInt(duration * SampleRate);
            var data    = new float[samples];
            var rng     = new System.Random(unchecked((int)(freq * 31f)));
            for (var i = 0; i < samples; i++)
            {
                var t   = (float)i / SampleRate;
                var env = Mathf.Exp(-t / Mathf.Max(decay, 0.001f));
                var s   = Mathf.Sin(2f * Mathf.PI * freq * t);
                var n   = ((float)rng.NextDouble() * 2f - 1f) * noise;
                data[i] = (s * (1f - noise) + n) * env * 0.55f;
            }
            var clip = AudioClip.Create("sfx_click_" + Mathf.RoundToInt(freq), samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // Pitch sweep + noise — used for card movement (whoosh / shuffle).
        private static AudioClip SynthSweep(float duration, float startFreq, float endFreq,
            float decay, float noise)
        {
            var samples = Mathf.CeilToInt(duration * SampleRate);
            var data    = new float[samples];
            var rng     = new System.Random(unchecked((int)(startFreq * 17f + endFreq * 11f)));
            for (var i = 0; i < samples; i++)
            {
                var t    = (float)i / SampleRate;
                var u    = duration > 0f ? t / duration : 0f;
                var freq = Mathf.Lerp(startFreq, endFreq, u);
                var env  = Mathf.Exp(-t / Mathf.Max(decay, 0.001f));
                var tone = Mathf.Sin(2f * Mathf.PI * freq * t);
                var n    = ((float)rng.NextDouble() * 2f - 1f) * noise;
                data[i]  = (tone * (1f - noise) + n) * env * 0.50f;
            }
            var clip = AudioClip.Create("sfx_sweep", samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // Stacked sine chord with shared envelope — used for jingles.
        private static AudioClip SynthChord(float duration, float[] freqs, float decay)
        {
            var samples = Mathf.CeilToInt(duration * SampleRate);
            var data    = new float[samples];
            for (var i = 0; i < samples; i++)
            {
                var t   = (float)i / SampleRate;
                var env = Mathf.Exp(-t / Mathf.Max(decay, 0.001f));
                var s   = 0f;
                for (var f = 0; f < freqs.Length; f++)
                    s += Mathf.Sin(2f * Mathf.PI * freqs[f] * t);
                data[i] = (s / Mathf.Max(freqs.Length, 1)) * env * 0.45f;
            }
            var clip = AudioClip.Create("sfx_chord", samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
