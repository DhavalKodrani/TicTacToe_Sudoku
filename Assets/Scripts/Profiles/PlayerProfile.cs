// -----------------------------------------------------------------------------
//  PlayerProfile.cs
//  Serializable data records for a single local profile. Split into small
//  [Serializable] classes so JsonUtility handles them cleanly and so each concern
//  (identity / settings / stats / in-progress game) can evolve independently.
//
//  Everything here is device-local. The only "identifier" is an anonymized GUID
//  (analyticsClientId) generated on the device -> used as GA4 client_id. It is
//  NOT a hardware id and contains no PII (VRC / privacy compliant).
// -----------------------------------------------------------------------------
using System;
using TTLS.Core;
using TTLS.Games.Sudoku;

namespace TTLS.Profiles
{
    [Serializable]
    public class PlayerProfile
    {
        // --- Identity ---
        public string profileId;        // internal stable id ("p0".."p3")
        public string displayName = "Player";
        public int avatarIndex;         // index into a curated avatar sprite set
        public string analyticsClientId; // anonymized GUID for GA4 client_id
        public long createdUtcTicks;
        public long lastPlayedUtcTicks;

        // --- Sub-records ---
        public ProfileSettings settings = new ProfileSettings();
        public ProfileStats stats = new ProfileStats();
        public InProgressGames inProgress = new InProgressGames();

        public static PlayerProfile CreateNew(string id, string name, int avatar)
        {
            long now = DateTime.UtcNow.Ticks;
            return new PlayerProfile
            {
                profileId = id,
                displayName = string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim(),
                avatarIndex = avatar,
                analyticsClientId = Guid.NewGuid().ToString("N"),
                createdUtcTicks = now,
                lastPlayedUtcTicks = now
            };
        }
    }

    [Serializable]
    public class ProfileSettings
    {
        public int theme = (int)UiTheme.Dark;
        public int controlScheme = (int)ControlScheme.TouchControllers;
        public float masterVolume = 0.8f;
        public float musicVolume = 0.5f;
        public float sfxVolume = 0.9f;
        public bool hapticsEnabled = true;
        public bool telemetryEnabled = true;   // GA4 opt-in toggle (default on, changeable)
        public bool sudokuAutoCheck = true;
        public bool sudokuHintsEnabled = true;
    }

    // ---- KPI / stats -----------------------------------------------------------
    [Serializable]
    public class ProfileStats
    {
        // Session + playtime (seconds)
        public double totalPlaytimeSeconds;
        public double sudokuPlaytimeSeconds;
        public double ticTacToePlaytimeSeconds;
        public int sessionCount;
        public double totalSessionSeconds; // for average session length

        public SudokuStats sudoku = new SudokuStats();
        public TicTacToeStats ticTacToe = new TicTacToeStats();

        public double AverageSessionSeconds =>
            sessionCount > 0 ? totalSessionSeconds / sessionCount : 0.0;
    }

    [Serializable]
    public class SudokuStats
    {
        public int completed;                 // total puzzles solved
        public int started;                   // total puzzles begun
        public int hintsUsedTotal;
        public int currentWinStreak;
        public int longestWinStreak;

        // Per-difficulty best/avg completion times (seconds). Index == difficulty.
        public float[] bestTimeSeconds = { 0, 0, 0, 0 };
        public float[] avgTimeSeconds  = { 0, 0, 0, 0 };
        public int[]   completedByDiff = { 0, 0, 0, 0 };

        public float WinRate => started > 0 ? (float)completed / started : 0f;
    }

    [Serializable]
    public class TicTacToeStats
    {
        public int matchesPlayed;

        // Index by TicTacToeMode: [Easy, Medium, Unbeatable, PassAndPlay]
        public int[] wins   = { 0, 0, 0, 0 };
        public int[] losses = { 0, 0, 0, 0 };
        public int[] draws  = { 0, 0, 0, 0 };

        public int TotalWins => wins[0] + wins[1] + wins[2] + wins[3];
        public int TotalLosses => losses[0] + losses[1] + losses[2] + losses[3];
        public int TotalDraws => draws[0] + draws[1] + draws[2] + draws[3];
    }

    // ---- Resume-in-progress ----------------------------------------------------
    [Serializable]
    public class InProgressGames
    {
        public bool hasSudoku;
        public SudokuSaveState sudoku;   // null unless hasSudoku
        // Tic-Tac-Toe is short; we intentionally do not persist a half match.
    }
}
