// -----------------------------------------------------------------------------
//  LocalAnalytics.cs
//  Device-local KPI tracker. Owns the "session" lifetime, accumulates playtime
//  per game mode, and records Sudoku / Tic-Tac-Toe outcomes into the ACTIVE
//  profile's ProfileStats. 100% offline; nothing here ever hits the network
//  (that is GoogleAnalyticsManager's job, which this class optionally forwards to).
//
//  Privacy: all data is written to the profile JSON under persistentDataPath.
//  A manual Export() produces a formatted JSON blob for debugging/support.
//
//  GC discipline: playtime is accumulated with doubles from Time.unscaledTime
//  deltas; no allocations occur in Update(). Only occasional saves allocate.
// -----------------------------------------------------------------------------
using System;
using System.Text;
using TTLS.Core;
using TTLS.Persistence;
using TTLS.Profiles;
using UnityEngine;

namespace TTLS.Analytics
{
    public class LocalAnalytics : MonoBehaviour
    {
        public static LocalAnalytics Instance { get; private set; }

        [Tooltip("Auto-save the active profile every N seconds while playing.")]
        public float autosaveInterval = 20f;

        // Optional forwarder to GA4. Assigned in the inspector or found at runtime.
        public GoogleAnalyticsManager ga4;

        private float _lastTick;
        private float _autosaveTimer;

        // Session bookkeeping
        private double _sessionStart;
        private bool _sessionActive;

        // Current-mode accumulation
        private GameType _activeMode = GameType.None;

        // Per-game timing
        private float _gameStartTime;
        private bool _gameTiming;

        private ProfileManager Profiles => ProfileManager.Instance;
        private ProfileStats Stats => Profiles?.ActiveProfile?.stats;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            if (ga4 == null) ga4 = GetComponent<GoogleAnalyticsManager>();
        }

        // ---- Session lifecycle --------------------------------------------------
        public void BeginSession()
        {
            if (_sessionActive) return;
            _sessionActive = true;
            _sessionStart = Time.unscaledTimeAsDouble;
            _lastTick = Time.unscaledTime;
            _activeMode = GameType.None;

            if (Stats != null) Stats.sessionCount++;
        }

        public void EndSession()
        {
            if (!_sessionActive) return;
            FlushPlaytime();
            if (Stats != null)
            {
                double len = Time.unscaledTimeAsDouble - _sessionStart;
                Stats.totalSessionSeconds += len;
            }
            _sessionActive = false;
            _activeMode = GameType.None;
            SaveNow();
        }

        /// <summary>Call when the player enters a board (Sudoku / TicTacToe / menu).</summary>
        public void SetActiveMode(GameType mode)
        {
            FlushPlaytime();       // bank whatever accrued under the previous mode
            _activeMode = mode;
        }

        private void Update()
        {
            if (!_sessionActive) return;
            _autosaveTimer += Time.unscaledDeltaTime;
            if (_autosaveTimer >= autosaveInterval)
            {
                _autosaveTimer = 0f;
                FlushPlaytime();
                SaveNow();
            }
        }

        // Move accrued wall-clock time into the right buckets. No allocations.
        private void FlushPlaytime()
        {
            float now = Time.unscaledTime;
            double delta = now - _lastTick;
            _lastTick = now;
            if (delta <= 0 || Stats == null) return;

            Stats.totalPlaytimeSeconds += delta;
            if (_activeMode == GameType.Sudoku) Stats.sudokuPlaytimeSeconds += delta;
            else if (_activeMode == GameType.TicTacToe) Stats.ticTacToePlaytimeSeconds += delta;
        }

        // ---- Per-game timing ----------------------------------------------------
        public void GameStarted(GameType type, string difficultyLabel)
        {
            _gameStartTime = Time.unscaledTime;
            _gameTiming = true;

            if (type == GameType.Sudoku && Stats != null) Stats.sudoku.started++;
            ga4?.LogGameStart(type, difficultyLabel);
        }

        public float CurrentGameSeconds =>
            _gameTiming ? Mathf.Max(0f, Time.unscaledTime - _gameStartTime) : 0f;

        // ---- Sudoku completion --------------------------------------------------
        public void SudokuCompleted(SudokuDifficulty difficulty, float timeSeconds, int hintsUsed)
        {
            _gameTiming = false;
            var s = Stats?.sudoku;
            if (s == null) return;

            int d = (int)difficulty;
            s.completed++;
            s.completedByDiff[d]++;
            s.hintsUsedTotal += hintsUsed;
            s.currentWinStreak++;
            if (s.currentWinStreak > s.longestWinStreak) s.longestWinStreak = s.currentWinStreak;

            // Best time (0 == "unset")
            if (s.bestTimeSeconds[d] <= 0f || timeSeconds < s.bestTimeSeconds[d])
                s.bestTimeSeconds[d] = timeSeconds;

            // Running average for this difficulty
            int completedThisDiff = s.completedByDiff[d];
            float prevAvg = s.avgTimeSeconds[d];
            s.avgTimeSeconds[d] = prevAvg + (timeSeconds - prevAvg) / completedThisDiff;

            SaveNow();
            ga4?.LogGameComplete(GameType.Sudoku, timeSeconds, GameOutcome.Win, hintsUsed,
                                 difficulty.ToString());
        }

        /// <summary>Player abandoned / restarted a Sudoku without finishing.</summary>
        public void SudokuAbandoned()
        {
            _gameTiming = false;
            var s = Stats?.sudoku;
            if (s == null) return;
            s.currentWinStreak = 0; // breaking the streak on a give-up
            SaveNow();
        }

        // ---- Tic-Tac-Toe completion ---------------------------------------------
        public void TicTacToeCompleted(TicTacToeMode mode, GameOutcome outcome, float timeSeconds)
        {
            _gameTiming = false;
            var t = Stats?.ticTacToe;
            if (t == null) return;

            int m = (int)mode;
            t.matchesPlayed++;
            switch (outcome)
            {
                case GameOutcome.Win:  t.wins[m]++;   break;
                case GameOutcome.Loss: t.losses[m]++; break;
                case GameOutcome.Draw: t.draws[m]++;  break;
            }

            SaveNow();
            string diffLabel = mode == TicTacToeMode.VsAiUnbeatable ? "AI_Unbeatable" : mode.ToString();
            ga4?.LogGameComplete(GameType.TicTacToe, timeSeconds, outcome, 0, diffLabel);
        }

        // ---- Persistence + export ----------------------------------------------
        public void SaveNow() => Profiles?.SaveActive();

        /// <summary>
        /// Produce a formatted JSON export of the active profile's stats for
        /// debugging/support and write it to a shareable file. Returns the path.
        /// </summary>
        public string ExportStatsJson()
        {
            var p = Profiles?.ActiveProfile;
            if (p == null) return null;

            FlushPlaytime();
            string key = $"exports/stats_{p.profileId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            JsonDataStore.Save(key, p.stats, prettyPrint: true);
            string path = JsonDataStore.PathFor(key);
            Debug.Log($"[LocalAnalytics] Stats exported to {path}");
            return path;
        }

        /// <summary>Return a human-readable KPI summary (for the stats dashboard).</summary>
        public string BuildDashboardSummary()
        {
            var st = Stats;
            if (st == null) return "No active profile.";

            var sb = new StringBuilder(512);
            sb.AppendLine($"Total playtime: {FormatTime(st.totalPlaytimeSeconds)}");
            sb.AppendLine($"  • Sudoku: {FormatTime(st.sudokuPlaytimeSeconds)}");
            sb.AppendLine($"  • Tic-Tac-Toe: {FormatTime(st.ticTacToePlaytimeSeconds)}");
            sb.AppendLine($"Sessions: {st.sessionCount}  (avg {FormatTime(st.AverageSessionSeconds)})");
            sb.AppendLine();
            sb.AppendLine($"Sudoku solved: {st.sudoku.completed}  win rate {st.sudoku.WinRate:P0}");
            sb.AppendLine($"Sudoku streak: {st.sudoku.currentWinStreak} (best {st.sudoku.longestWinStreak})");
            sb.AppendLine($"Hints used: {st.sudoku.hintsUsedTotal}");
            sb.AppendLine();
            var tt = st.ticTacToe;
            sb.AppendLine($"TicTacToe matches: {tt.matchesPlayed}  W/L/D {tt.TotalWins}/{tt.TotalLosses}/{tt.TotalDraws}");
            return sb.ToString();
        }

        private static string FormatTime(double seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}h {t.Minutes}m"
                : $"{t.Minutes}m {t.Seconds}s";
        }

        private void OnApplicationPause(bool paused) { if (paused) EndSession(); else BeginSession(); }
        private void OnApplicationQuit() => EndSession();
    }
}
