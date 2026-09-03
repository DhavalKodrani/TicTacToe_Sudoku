// -----------------------------------------------------------------------------
//  SettingsManager.cs
//  Applies and mutates the ACTIVE profile's ProfileSettings, then fans the change
//  out to the systems that care (audio, theme, controls, telemetry) and logs the
//  change to GA4 as a "setting_changed" event.
//
//  Settings live inside the profile (so they are per-profile and isolated); this
//  class is just the runtime coordinator + change broadcaster.
// -----------------------------------------------------------------------------
using System;
using TTLS.Analytics;
using TTLS.Core;
using TTLS.Profiles;
using UnityEngine;

namespace TTLS.Settings
{
    public class SettingsManager : MonoBehaviour
    {
        public static SettingsManager Instance { get; private set; }

        public event Action<UiTheme> OnThemeChanged;
        public event Action<ControlScheme> OnControlSchemeChanged;
        public event Action OnAudioChanged;

        private ProfileManager Profiles => ProfileManager.Instance;
        private ProfileSettings S => Profiles?.ActiveProfile?.settings;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>Re-apply all settings when a profile becomes active.</summary>
        public void ApplyAll()
        {
            if (S == null) return;
            OnThemeChanged?.Invoke((UiTheme)S.theme);
            OnControlSchemeChanged?.Invoke((ControlScheme)S.controlScheme);
            OnAudioChanged?.Invoke();
        }

        // ---- Mutators (each persists + logs) ------------------------------------
        public void SetTheme(UiTheme theme)
        {
            if (S == null || S.theme == (int)theme) return;
            S.theme = (int)theme;
            Persist();
            OnThemeChanged?.Invoke(theme);
            Log("theme", theme.ToString());
        }

        public void SetControlScheme(ControlScheme scheme)
        {
            if (S == null || S.controlScheme == (int)scheme) return;
            S.controlScheme = (int)scheme;
            Persist();
            OnControlSchemeChanged?.Invoke(scheme);
            Log("control_scheme", scheme.ToString());
        }

        public void SetMasterVolume(float v) { if (S == null) return; S.masterVolume = Clamp01(v); Persist(); OnAudioChanged?.Invoke(); Log("master_volume", S.masterVolume.ToString("0.00")); }
        public void SetMusicVolume(float v)  { if (S == null) return; S.musicVolume  = Clamp01(v); Persist(); OnAudioChanged?.Invoke(); Log("music_volume", S.musicVolume.ToString("0.00")); }
        public void SetSfxVolume(float v)    { if (S == null) return; S.sfxVolume    = Clamp01(v); Persist(); OnAudioChanged?.Invoke(); Log("sfx_volume", S.sfxVolume.ToString("0.00")); }

        public void SetHaptics(bool on)      { if (S == null) return; S.hapticsEnabled = on; Persist(); Log("haptics", on ? "on" : "off"); }
        public void SetAutoCheck(bool on)    { if (S == null) return; S.sudokuAutoCheck = on; Persist(); Log("sudoku_autocheck", on ? "on" : "off"); }
        public void SetHintsEnabled(bool on) { if (S == null) return; S.sudokuHintsEnabled = on; Persist(); Log("sudoku_hints", on ? "on" : "off"); }

        public void SetTelemetry(bool on)
        {
            if (S == null) return;
            S.telemetryEnabled = on;
            Persist();
            // Log the change ONLY when enabling (respect opt-out immediately).
            if (on) Log("telemetry", "on");
        }

        // ---- Convenience readers ------------------------------------------------
        public UiTheme Theme => S != null ? (UiTheme)S.theme : UiTheme.Dark;
        public ControlScheme Control => S != null ? (ControlScheme)S.controlScheme : ControlScheme.TouchControllers;
        public bool Haptics => S?.hapticsEnabled ?? true;

        private void Persist() => Profiles?.SaveActive();
        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static void Log(string name, string value) =>
            GoogleAnalyticsManager.Instance?.LogSettingChanged(name, value);
    }
}
