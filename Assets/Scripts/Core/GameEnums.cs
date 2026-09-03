// -----------------------------------------------------------------------------
//  GameEnums.cs
//  Shared, allocation-free enumerations used across every subsystem.
//  Keeping these in one place avoids "magic strings" and lets the compiler
//  catch typos that would otherwise silently break analytics / persistence.
// -----------------------------------------------------------------------------
namespace TTLS.Core
{
    /// <summary>High-level application state machine. Drives UIManager screens.</summary>
    public enum AppScreen
    {
        Boot = 0,
        ProfileSelect = 1,
        MainMenu = 2,
        SudokuBoard = 3,
        TicTacToeBoard = 4,
        StatsDashboard = 5,
        Settings = 6
    }

    /// <summary>Which mini-game a session belongs to. Serialized as string in GA4.</summary>
    public enum GameType
    {
        None = 0,
        Sudoku = 1,
        TicTacToe = 2
    }

    /// <summary>Sudoku difficulty buckets. The int value == target puzzle clue tier.</summary>
    public enum SudokuDifficulty
    {
        Easy = 0,
        Medium = 1,
        Hard = 2,
        Expert = 3
    }

    /// <summary>Tic-Tac-Toe opponent configuration.</summary>
    public enum TicTacToeMode
    {
        VsAiEasy = 0,
        VsAiMedium = 1,
        VsAiUnbeatable = 2,
        PassAndPlay = 3
    }

    /// <summary>Marks on the Tic-Tac-Toe board. Empty == 0 for cheap default arrays.</summary>
    public enum Mark : byte
    {
        Empty = 0,
        X = 1,
        O = 2
    }

    /// <summary>Result of a completed game, from the active human profile's POV.</summary>
    public enum GameOutcome
    {
        None = 0,
        Win = 1,
        Loss = 2,
        Draw = 3
    }

    /// <summary>Input / interaction preference for VR.</summary>
    public enum ControlScheme
    {
        TouchControllers = 0,
        HandTracking = 1
    }

    /// <summary>Visual theme optimized for VR lens readability.</summary>
    public enum UiTheme
    {
        Dark = 0,
        Light = 1
    }
}
