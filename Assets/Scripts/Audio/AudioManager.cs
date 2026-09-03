// -----------------------------------------------------------------------------
//  AudioManager.cs
//  Ambient music + one-shot SFX with per-profile volume mixing. SFX are played
//  through a small pool of AudioSources so overlapping cues never allocate or cut
//  each other off (GC-friendly, no PlayOneShot garbage on a fresh source each time).
//
//  Wire the AudioClips in the inspector. Volume is driven by SettingsManager.
// -----------------------------------------------------------------------------
using TTLS.Settings;
using UnityEngine;

namespace TTLS.Audio
{
    public enum Sfx { Move, Place, Error, Win, Lose, Draw, ButtonClick, Hint, Undo }

    [RequireComponent(typeof(AudioSource))]
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Music")]
        [SerializeField] private AudioClip ambientMusic;
        [SerializeField] private AudioSource musicSource;

        [Header("SFX clips (index matches Sfx enum order)")]
        [SerializeField] private AudioClip move;
        [SerializeField] private AudioClip place;
        [SerializeField] private AudioClip error;
        [SerializeField] private AudioClip win;
        [SerializeField] private AudioClip lose;
        [SerializeField] private AudioClip draw;
        [SerializeField] private AudioClip buttonClick;
        [SerializeField] private AudioClip hint;
        [SerializeField] private AudioClip undo;

        [Header("SFX pool")]
        [SerializeField] private int sfxVoices = 6;

        private AudioSource[] _sfxPool;
        private int _next;
        private AudioClip[] _clips;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (musicSource == null) musicSource = GetComponent<AudioSource>();
            musicSource.clip = ambientMusic;
            musicSource.loop = true;
            musicSource.playOnAwake = false;

            _sfxPool = new AudioSource[sfxVoices];
            for (int i = 0; i < sfxVoices; i++)
            {
                var go = new GameObject($"SFXVoice_{i}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f; // UI SFX are 2D by default
                _sfxPool[i] = src;
            }

            _clips = new[] { move, place, error, win, lose, draw, buttonClick, hint, undo };
        }

        private void Start()
        {
            if (SettingsManager.Instance != null)
                SettingsManager.Instance.OnAudioChanged += ApplyVolumes;
            ApplyVolumes();
            PlayMusic();
        }

        private void OnDestroy()
        {
            if (SettingsManager.Instance != null)
                SettingsManager.Instance.OnAudioChanged -= ApplyVolumes;
        }

        public void PlayMusic()
        {
            if (ambientMusic != null && !musicSource.isPlaying) musicSource.Play();
        }

        public void Play(Sfx sfx)
        {
            int idx = (int)sfx;
            if (_clips == null || idx < 0 || idx >= _clips.Length) return;
            AudioClip clip = _clips[idx];
            if (clip == null) return;

            // Round-robin the pool so overlapping cues coexist.
            AudioSource src = _sfxPool[_next];
            _next = (_next + 1) % _sfxPool.Length;
            src.clip = clip;
            src.Play();
        }

        private void ApplyVolumes()
        {
            var s = TTLS.Profiles.ProfileManager.Instance?.ActiveProfile?.settings;
            float master = s?.masterVolume ?? 0.8f;
            float music = s?.musicVolume ?? 0.5f;
            float sfx = s?.sfxVolume ?? 0.9f;

            if (musicSource != null) musicSource.volume = master * music;
            if (_sfxPool != null)
                for (int i = 0; i < _sfxPool.Length; i++)
                    _sfxPool[i].volume = master * sfx;
        }
    }
}
