// -----------------------------------------------------------------------------
//  GameManager.cs
//  Application bootstrap + navigation authority. Add ONE to a persistent
//  "Bootstrap" GameObject alongside the other managers. It:
//
//   * Ensures the puzzle bank is built/loaded before gameplay is offered.
//   * Owns the current AppScreen and raises OnScreenChanged (UIManager listens).
//   * Emits GA4 screen_view telemetry on every navigation.
//   * Coordinates session begin/end with LocalAnalytics.
//
//  It deliberately holds no UI references — the UIManager binds visuals to these
//  events, keeping logic and presentation cleanly separated.
// -----------------------------------------------------------------------------
using System;
using TTLS.Analytics;
using TTLS.Games.Sudoku;
using TTLS.Profiles;
using TTLS.Settings;
using UnityEngine;

namespace TTLS.Core
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public AppScreen CurrentScreen { get; private set; } = AppScreen.Boot;
        public bool GameReady { get; private set; }

        public event Action<AppScreen> OnScreenChanged;
        public event Action OnGameReady;

        // Maps screens to GA4 screen_name strings required by the spec.
        private static string ScreenName(AppScreen s)
        {
            switch (s)
            {
                case AppScreen.MainMenu:       return "main_menu";
                case AppScreen.ProfileSelect:  return "profile_select";
                case AppScreen.SudokuBoard:    return "sudoku_board";
                case AppScreen.TicTacToeBoard: return "tictactoe_board";
                case AppScreen.StatsDashboard: return "stats_dashboard";
                case AppScreen.Settings:       return "settings";
                default:                       return "boot";
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            // Wait for the 500-puzzle bank, then open the profile-select flow.
            if (SudokuPuzzleBank.Instance != null && SudokuPuzzleBank.Instance.IsReady)
                HandleBankReady();
            else if (SudokuPuzzleBank.Instance != null)
                SudokuPuzzleBank.Instance.OnBankReady += HandleBankReady;
            else
                HandleBankReady(); // no bank component (e.g. TicTacToe-only test scene)

            GoTo(AppScreen.Boot);
        }

        private void HandleBankReady()
        {
            if (SudokuPuzzleBank.Instance != null)
                SudokuPuzzleBank.Instance.OnBankReady -= HandleBankReady;

            GameReady = true;
            OnGameReady?.Invoke();

            LocalAnalytics.Instance?.BeginSession();

            // Open the profile-select flow. UIManager can auto-select
            // ProfileManager.LastActiveId here if you prefer skipping the screen.
            GoTo(AppScreen.ProfileSelect);
        }

        // ---- Navigation ---------------------------------------------------------
        public void GoTo(AppScreen screen)
        {
            CurrentScreen = screen;
            OnScreenChanged?.Invoke(screen);

            // screen_view telemetry (skips Boot; no profile/creds yet).
            if (screen != AppScreen.Boot)
            {
                GoogleAnalyticsManager.Instance?.LogScreenView(
                    ScreenName(screen), ScreenName(screen));
            }
        }

        /// <summary>Called by UIManager after a profile is chosen/created.</summary>
        public void OnProfileActivated()
        {
            SettingsManager.Instance?.ApplyAll();
            GoTo(AppScreen.MainMenu);
        }

        private void OnApplicationQuit()
        {
            LocalAnalytics.Instance?.EndSession();
        }
    }
}
