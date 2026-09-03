// -----------------------------------------------------------------------------
//  UIManager.cs
//  The presentation + input hub. It connects VR interactions (VRButton /
//  Meta Interaction SDK events routed through the cell views) to the pure game
//  engines, manages screen visibility, profile switching, the stats dashboard,
//  and settings. It is the ONLY place that touches both "logic" and "Unity UI",
//  keeping the engines headless and testable.
//
//  Structure:
//   * One root panel GameObject per AppScreen, toggled by GameManager events.
//   * Board cell views are pre-placed in the scene and bound once in Awake.
//   * All button callbacks are plain public methods so they can be wired to
//     VRButton.OnPressed (poke / raycast / hand pinch) in the inspector.
//
//  Performance: rendering is event-driven (only changed cells re-render); the
//  Sudoku timer text updates at most a few times/sec, not every frame.
// -----------------------------------------------------------------------------
using System.Collections.Generic;
using TTLS.Analytics;
using TTLS.Audio;
using TTLS.Core;
using TTLS.Games.Sudoku;
using TTLS.Games.TicTacToe;
using TTLS.Profiles;
using TTLS.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace TTLS.UI
{
    public class UIManager : MonoBehaviour
    {
        [Header("Screen roots (one per AppScreen)")]
        [SerializeField] private GameObject bootPanel;
        [SerializeField] private GameObject profileSelectPanel;
        [SerializeField] private GameObject mainMenuPanel;
        [SerializeField] private GameObject sudokuPanel;
        [SerializeField] private GameObject ticTacToePanel;
        [SerializeField] private GameObject statsPanel;
        [SerializeField] private GameObject settingsPanel;

        [Header("Boot")]
        [SerializeField] private Slider bootProgressBar;
        [SerializeField] private Text bootStatusLabel;

        [Header("Themes")]
        [SerializeField] private UIThemePalette darkPalette;
        [SerializeField] private UIThemePalette lightPalette;
        private UIThemePalette _palette;

        [Header("Tic-Tac-Toe")]
        [SerializeField] private TicTacToeCellView[] tttCells = new TicTacToeCellView[9];
        [SerializeField] private Text tttStatusLabel;

        [Header("Sudoku")]
        [SerializeField] private SudokuCellView[] sudokuCells = new SudokuCellView[81];
        [SerializeField] private Text sudokuTimerLabel;
        [SerializeField] private Text sudokuDifficultyLabel;
        [SerializeField] private Text sudokuHintsLabel;
        [SerializeField] private VR.VRButton notesToggleButton;
        [SerializeField] private Text notesToggleLabel;

        [Header("Profiles")]
        [SerializeField] private Transform profileListRoot;   // holds profile slot buttons
        [SerializeField] private Text[] profileSlotLabels;    // length up to 4
        [SerializeField] private InputField newProfileNameField;

        [Header("Stats")]
        [SerializeField] private Text statsBodyLabel;

        [Header("Settings widgets")]
        [SerializeField] private Slider masterVolumeSlider;
        [SerializeField] private Slider musicVolumeSlider;
        [SerializeField] private Slider sfxVolumeSlider;
        [SerializeField] private Toggle hapticsToggle;
        [SerializeField] private Toggle telemetryToggle;
        [SerializeField] private Toggle autoCheckToggle;
        [SerializeField] private Toggle themeToggle;         // on == Light
        [SerializeField] private Toggle handTrackingToggle;  // on == HandTracking

        // ---- Engines (headless logic) ------------------------------------------
        private readonly TicTacToeEngine _ttt = new TicTacToeEngine();
        private readonly SudokuEngine _sudoku = new SudokuEngine();

        // ---- Runtime state ------------------------------------------------------
        private int _selectedSudokuCell = -1;
        private float _sudokuElapsed;
        private bool _sudokuRunning;
        private float _timerUiAccum;
        private TicTacToeMode _pendingTttMode = TicTacToeMode.VsAiEasy;
        private SudokuDifficulty _pendingSudokuDiff = SudokuDifficulty.Easy;
        private readonly List<int> _sudokuDeck = new List<int>(); // shuffled indices

        private GameManager GM => GameManager.Instance;

        // =========================================================================
        //  Lifecycle
        // =========================================================================
        private void Awake()
        {
            _palette = darkPalette;

            // Bind cell view presses -> engine input, once.
            for (int i = 0; i < tttCells.Length; i++)
            {
                if (tttCells[i] == null) continue;
                tttCells[i].SetIndex(i);
                tttCells[i].OnPressed += OnTttCellPressed;
            }
            for (int i = 0; i < sudokuCells.Length; i++)
            {
                if (sudokuCells[i] == null) continue;
                sudokuCells[i].SetIndex(i);
                sudokuCells[i].OnPressed += OnSudokuCellPressed;
            }

            // Engine -> UI event subscriptions.
            _ttt.OnBoardChanged += RenderTttBoard;
            _ttt.OnGameOver += HandleTttGameOver;
            _sudoku.OnCellChanged += RenderSudokuCell;
            _sudoku.OnBoardChanged += RenderSudokuBoardAll;
            _sudoku.OnSolved += HandleSudokuSolved;
        }

        private void OnEnable()
        {
            if (GM != null)
            {
                GM.OnScreenChanged += ShowScreen;
                GM.OnGameReady += HandleGameReady;
            }
            if (SettingsManager.Instance != null)
                SettingsManager.Instance.OnThemeChanged += HandleThemeChanged;
        }

        private void OnDisable()
        {
            if (GM != null)
            {
                GM.OnScreenChanged -= ShowScreen;
                GM.OnGameReady -= HandleGameReady;
            }
            if (SettingsManager.Instance != null)
                SettingsManager.Instance.OnThemeChanged -= HandleThemeChanged;
        }

        private void Update()
        {
            // Boot progress feedback while the 500-puzzle bank builds.
            if (GM != null && !GM.GameReady && SudokuPuzzleBank.Instance != null)
            {
                float p = SudokuPuzzleBank.Instance.BuildProgress01;
                if (bootProgressBar != null) bootProgressBar.value = p;
                if (bootStatusLabel != null)
                    bootStatusLabel.text = $"Preparing puzzles… {Mathf.RoundToInt(p * 100)}%";
            }

            // Sudoku timer (throttled UI update, ~4 Hz).
            if (_sudokuRunning)
            {
                _sudokuElapsed += Time.unscaledDeltaTime;
                _timerUiAccum += Time.unscaledDeltaTime;
                if (_timerUiAccum >= 0.25f)
                {
                    _timerUiAccum = 0f;
                    if (sudokuTimerLabel != null)
                        sudokuTimerLabel.text = FormatClock(_sudokuElapsed);
                }
            }
        }

        // =========================================================================
        //  Screen management
        // =========================================================================
        private void HandleGameReady()
        {
            RefreshProfileList();
            // If a profile was active last session, offer quick-resume by pre-selecting.
        }

        private void ShowScreen(AppScreen screen)
        {
            SetActive(bootPanel, screen == AppScreen.Boot);
            SetActive(profileSelectPanel, screen == AppScreen.ProfileSelect);
            SetActive(mainMenuPanel, screen == AppScreen.MainMenu);
            SetActive(sudokuPanel, screen == AppScreen.SudokuBoard);
            SetActive(ticTacToePanel, screen == AppScreen.TicTacToeBoard);
            SetActive(statsPanel, screen == AppScreen.StatsDashboard);
            SetActive(settingsPanel, screen == AppScreen.Settings);

            // Mode tracking for playtime buckets.
            switch (screen)
            {
                case AppScreen.SudokuBoard: LocalAnalytics.Instance?.SetActiveMode(GameType.Sudoku); break;
                case AppScreen.TicTacToeBoard: LocalAnalytics.Instance?.SetActiveMode(GameType.TicTacToe); break;
                default: LocalAnalytics.Instance?.SetActiveMode(GameType.None); break;
            }

            if (screen == AppScreen.StatsDashboard) RefreshStats();
            if (screen == AppScreen.Settings) PullSettingsIntoWidgets();
            if (screen == AppScreen.ProfileSelect) RefreshProfileList();
        }

        private static void SetActive(GameObject go, bool on) { if (go != null) go.SetActive(on); }

        // Public nav hooks for menu VRButtons -------------------------------------
        public void NavMainMenu() => GM?.GoTo(AppScreen.MainMenu);
        public void NavProfileSelect() => GM?.GoTo(AppScreen.ProfileSelect);
        public void NavStats() => GM?.GoTo(AppScreen.StatsDashboard);
        public void NavSettings() => GM?.GoTo(AppScreen.Settings);

        // =========================================================================
        //  Profiles
        // =========================================================================
        public void RefreshProfileList()
        {
            var pm = ProfileManager.Instance;
            if (pm == null || profileSlotLabels == null) return;

            var profiles = pm.GetAllProfiles();
            var bySlot = new Dictionary<int, PlayerProfile>();
            foreach (var p in profiles)
            {
                int slot = int.Parse(p.profileId.Substring(1));
                bySlot[slot] = p;
            }

            for (int i = 0; i < profileSlotLabels.Length; i++)
            {
                if (profileSlotLabels[i] == null) continue;
                profileSlotLabels[i].text = bySlot.TryGetValue(i, out var pr)
                    ? $"{pr.displayName}"
                    : "<empty>";
            }
        }

        // Wire these to per-slot VRButtons (pass the slot index via a small wrapper
        // or four dedicated methods). Convenience 0..3 methods provided:
        public void SelectProfileSlot0() => ActivateSlot(0);
        public void SelectProfileSlot1() => ActivateSlot(1);
        public void SelectProfileSlot2() => ActivateSlot(2);
        public void SelectProfileSlot3() => ActivateSlot(3);

        private void ActivateSlot(int slot)
        {
            var pm = ProfileManager.Instance;
            if (pm == null) return;
            string id = $"p{slot}";
            if (pm.SlotExists(slot))
            {
                if (pm.SetActive(id)) GM?.OnProfileActivated();
            }
            else
            {
                // Empty slot tapped -> create using the name field (or a default).
                string name = newProfileNameField != null && !string.IsNullOrWhiteSpace(newProfileNameField.text)
                    ? newProfileNameField.text : $"Player {slot + 1}";
                var created = pm.CreateProfile(name, slot); // avatarIndex == slot placeholder
                if (created != null && pm.SetActive(created.profileId))
                    GM?.OnProfileActivated();
            }
        }

        public void DeleteProfileSlot0() => DeleteSlot(0);
        public void DeleteProfileSlot1() => DeleteSlot(1);
        public void DeleteProfileSlot2() => DeleteSlot(2);
        public void DeleteProfileSlot3() => DeleteSlot(3);
        private void DeleteSlot(int slot)
        {
            ProfileManager.Instance?.DeleteProfile($"p{slot}");
            RefreshProfileList();
        }

        // =========================================================================
        //  Tic-Tac-Toe
        // =========================================================================
        // Difficulty pickers (wire to menu buttons):
        public void StartTttEasy()       => StartTtt(TicTacToeMode.VsAiEasy);
        public void StartTttMedium()     => StartTtt(TicTacToeMode.VsAiMedium);
        public void StartTttUnbeatable() => StartTtt(TicTacToeMode.VsAiUnbeatable);
        public void StartTttPassPlay()   => StartTtt(TicTacToeMode.PassAndPlay);

        private void StartTtt(TicTacToeMode mode)
        {
            _pendingTttMode = mode;
            GM?.GoTo(AppScreen.TicTacToeBoard);
            _ttt.NewGame(mode, Mark.X);

            string label = mode == TicTacToeMode.VsAiUnbeatable ? "AI_Unbeatable" : mode.ToString();
            LocalAnalytics.Instance?.GameStarted(GameType.TicTacToe, label);
            RenderTttBoard();
            UpdateTttStatus();
        }

        public void RestartTtt() => StartTtt(_pendingTttMode);

        private void OnTttCellPressed(int index)
        {
            if (!_ttt.CanPlace(index)) return;
            AudioManager.Instance?.Play(Sfx.Move);
            _ttt.PlaceHuman(index);
        }

        private void RenderTttBoard()
        {
            for (int i = 0; i < tttCells.Length; i++)
            {
                if (tttCells[i] == null) continue;
                tttCells[i].Render(_ttt[i], _palette.markX, _palette.markO, _palette.textPrimary);
                tttCells[i].SetBackground(_palette.cell);
                tttCells[i].SetWinning(false, _palette.winGlow);
            }
            UpdateTttStatus();
        }

        private void UpdateTttStatus()
        {
            if (tttStatusLabel == null) return;
            if (_ttt.IsGameOver)
            {
                tttStatusLabel.text = _ttt.Outcome switch
                {
                    GameOutcome.Win => _ttt.Mode == TicTacToeMode.PassAndPlay
                        ? $"{_ttt.Winner} wins!" : "You win! 🎉",
                    GameOutcome.Loss => "AI wins.",
                    _ => "Draw."
                };
            }
            else
            {
                tttStatusLabel.text = _ttt.Mode == TicTacToeMode.PassAndPlay
                    ? $"{_ttt.CurrentTurn}'s turn"
                    : (_ttt.CurrentTurn == _ttt.HumanMark ? "Your turn" : "AI thinking…");
            }
        }

        private void HandleTttGameOver(GameOutcome outcome, int[] winningLine)
        {
            if (winningLine != null)
                for (int i = 0; i < winningLine.Length; i++)
                    tttCells[winningLine[i]]?.SetWinning(true, _palette.winGlow);

            switch (outcome)
            {
                case GameOutcome.Win:  AudioManager.Instance?.Play(Sfx.Win);  break;
                case GameOutcome.Loss: AudioManager.Instance?.Play(Sfx.Lose); break;
                default:               AudioManager.Instance?.Play(Sfx.Draw); break;
            }

            // For pass-and-play, outcome is always "Win" for whoever completed a line;
            // record it against the PassAndPlay bucket. For AI modes, outcome is POV.
            LocalAnalytics.Instance?.TicTacToeCompleted(_ttt.Mode, outcome,
                                                        LocalAnalytics.Instance.CurrentGameSeconds);
            UpdateTttStatus();
        }

        // =========================================================================
        //  Sudoku
        // =========================================================================
        public void StartSudokuEasy()   => StartSudoku(SudokuDifficulty.Easy);
        public void StartSudokuMedium() => StartSudoku(SudokuDifficulty.Medium);
        public void StartSudokuHard()   => StartSudoku(SudokuDifficulty.Hard);
        public void StartSudokuExpert() => StartSudoku(SudokuDifficulty.Expert);

        private void StartSudoku(SudokuDifficulty diff)
        {
            var bank = SudokuPuzzleBank.Instance;
            if (bank == null || !bank.IsReady) return;

            _pendingSudokuDiff = diff;
            int idx = NextPuzzleIndex(diff);
            SudokuPuzzleRecord rec = bank.Get(diff, idx);

            _sudoku.AutoCheckEnabled = ProfileManager.Instance?.ActiveProfile?.settings?.sudokuAutoCheck ?? true;
            _sudoku.NotesMode = false;
            _sudoku.Load(rec);

            _selectedSudokuCell = -1;
            _sudokuElapsed = 0f;
            _sudokuRunning = true;

            GM?.GoTo(AppScreen.SudokuBoard);
            LocalAnalytics.Instance?.GameStarted(GameType.Sudoku, diff.ToString());

            if (sudokuDifficultyLabel != null) sudokuDifficultyLabel.text = diff.ToString();
            UpdateNotesToggleLabel();
            RenderSudokuBoardAll();
        }

        // Draws puzzles without repeats until the 125-deck for a difficulty is used.
        private int NextPuzzleIndex(SudokuDifficulty diff)
        {
            if (_sudokuDeck.Count == 0)
            {
                for (int i = 0; i < SudokuPuzzleBank.PerDifficulty; i++) _sudokuDeck.Add(i);
                for (int i = _sudokuDeck.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (_sudokuDeck[i], _sudokuDeck[j]) = (_sudokuDeck[j], _sudokuDeck[i]);
                }
            }
            int idx = _sudokuDeck[_sudokuDeck.Count - 1];
            _sudokuDeck.RemoveAt(_sudokuDeck.Count - 1);
            return idx;
        }

        private void OnSudokuCellPressed(int index)
        {
            _selectedSudokuCell = index;
            RenderSudokuBoardAll(); // refresh selection ring + peer highlight
        }

        // Number pad buttons (wire 1..9). Convenience methods:
        public void SudokuInput1() => SudokuInput(1);
        public void SudokuInput2() => SudokuInput(2);
        public void SudokuInput3() => SudokuInput(3);
        public void SudokuInput4() => SudokuInput(4);
        public void SudokuInput5() => SudokuInput(5);
        public void SudokuInput6() => SudokuInput(6);
        public void SudokuInput7() => SudokuInput(7);
        public void SudokuInput8() => SudokuInput(8);
        public void SudokuInput9() => SudokuInput(9);

        private void SudokuInput(int value)
        {
            if (_selectedSudokuCell < 0) return;
            _sudoku.Input(_selectedSudokuCell, value);
            AudioManager.Instance?.Play(_sudoku.IsError(_selectedSudokuCell) ? Sfx.Error : Sfx.Place);
        }

        public void SudokuErase()
        {
            if (_selectedSudokuCell < 0) return;
            _sudoku.Erase(_selectedSudokuCell);
            AudioManager.Instance?.Play(Sfx.Undo);
        }

        public void SudokuUndo() { _sudoku.Undo(); AudioManager.Instance?.Play(Sfx.Undo); }
        public void SudokuRedo() { _sudoku.Redo(); }

        public void SudokuToggleNotes()
        {
            _sudoku.NotesMode = !_sudoku.NotesMode;
            UpdateNotesToggleLabel();
        }

        private void UpdateNotesToggleLabel()
        {
            if (notesToggleLabel != null)
                notesToggleLabel.text = _sudoku.NotesMode ? "Notes: ON" : "Notes: OFF";
        }

        public void SudokuHint()
        {
            bool allowed = ProfileManager.Instance?.ActiveProfile?.settings?.sudokuHintsEnabled ?? true;
            if (!allowed) return;
            int cell = _sudoku.Hint(_selectedSudokuCell);
            if (cell >= 0)
            {
                AudioManager.Instance?.Play(Sfx.Hint);
                if (sudokuHintsLabel != null) sudokuHintsLabel.text = $"Hints: {_sudoku.HintsUsed}";
            }
        }

        public void SudokuBackToMenu()
        {
            SaveSudokuInProgress();
            _sudokuRunning = false;
            GM?.GoTo(AppScreen.MainMenu);
        }

        private void SaveSudokuInProgress()
        {
            var p = ProfileManager.Instance?.ActiveProfile;
            if (p == null || _sudoku.IsSolved) return;
            var state = _sudoku.CaptureState();
            state.elapsedSeconds = _sudokuElapsed;
            p.inProgress.hasSudoku = true;
            p.inProgress.sudoku = state;
            ProfileManager.Instance.SaveActive();
        }

        public void ResumeSudoku()
        {
            var p = ProfileManager.Instance?.ActiveProfile;
            if (p == null || !p.inProgress.hasSudoku || p.inProgress.sudoku == null) return;

            _sudoku.LoadState(p.inProgress.sudoku);
            _sudokuElapsed = p.inProgress.sudoku.elapsedSeconds;
            _sudokuRunning = true;
            _selectedSudokuCell = -1;
            GM?.GoTo(AppScreen.SudokuBoard);
            if (sudokuDifficultyLabel != null) sudokuDifficultyLabel.text = _sudoku.Difficulty.ToString();
            RenderSudokuBoardAll();
        }

        private void RenderSudokuCell(int cell)
        {
            var view = sudokuCells[cell];
            if (view == null) return;

            int val = _sudoku.GetValue(cell);
            if (val != 0)
            {
                Color c = _sudoku.IsGiven(cell) ? _palette.textGiven
                        : _sudoku.IsError(cell) ? _palette.textError
                        : _palette.textEntered;
                view.RenderValue(val, c);
            }
            else
            {
                view.RenderNotes(_sudoku.GetNotes(cell), _palette.textNote);
            }

            // 3x3 box shading for readability.
            int box = (cell / 27) * 3 + ((cell % 9) / 3);
            view.SetBackground(box % 2 == 0 ? _palette.cell : _palette.cellAlt);
            view.SetSelected(cell == _selectedSudokuCell, _palette.selectionRing);
        }

        private void RenderSudokuBoardAll()
        {
            for (int i = 0; i < sudokuCells.Length; i++) RenderSudokuCell(i);
            if (sudokuHintsLabel != null) sudokuHintsLabel.text = $"Hints: {_sudoku.HintsUsed}";
        }

        private void HandleSudokuSolved()
        {
            _sudokuRunning = false;
            AudioManager.Instance?.Play(Sfx.Win);

            LocalAnalytics.Instance?.SudokuCompleted(_sudoku.Difficulty, _sudokuElapsed, _sudoku.HintsUsed);

            // Clear the in-progress save now that it's done.
            var p = ProfileManager.Instance?.ActiveProfile;
            if (p != null) { p.inProgress.hasSudoku = false; p.inProgress.sudoku = null; ProfileManager.Instance.SaveActive(); }
        }

        /// <summary>"New Puzzle" button: deal another puzzle at the same difficulty.</summary>
        public void NewSudokuPuzzle() => StartSudoku(_pendingSudokuDiff);

        // =========================================================================
        //  Stats dashboard
        // =========================================================================
        public void RefreshStats()
        {
            if (statsBodyLabel != null)
                statsBodyLabel.text = LocalAnalytics.Instance?.BuildDashboardSummary() ?? "";
        }

        public void ExportStats()
        {
            string path = LocalAnalytics.Instance?.ExportStatsJson();
            if (statsBodyLabel != null && path != null)
                statsBodyLabel.text += $"\n\nExported to:\n{path}";
        }

        // =========================================================================
        //  Settings
        // =========================================================================
        private void PullSettingsIntoWidgets()
        {
            var s = ProfileManager.Instance?.ActiveProfile?.settings;
            if (s == null) return;
            if (masterVolumeSlider != null) masterVolumeSlider.SetValueWithoutNotify(s.masterVolume);
            if (musicVolumeSlider != null)  musicVolumeSlider.SetValueWithoutNotify(s.musicVolume);
            if (sfxVolumeSlider != null)    sfxVolumeSlider.SetValueWithoutNotify(s.sfxVolume);
            if (hapticsToggle != null)      hapticsToggle.SetIsOnWithoutNotify(s.hapticsEnabled);
            if (telemetryToggle != null)    telemetryToggle.SetIsOnWithoutNotify(s.telemetryEnabled);
            if (autoCheckToggle != null)    autoCheckToggle.SetIsOnWithoutNotify(s.sudokuAutoCheck);
            if (themeToggle != null)        themeToggle.SetIsOnWithoutNotify(s.theme == (int)UiTheme.Light);
            if (handTrackingToggle != null) handTrackingToggle.SetIsOnWithoutNotify(s.controlScheme == (int)ControlScheme.HandTracking);
        }

        // Wire these to the widgets' OnValueChanged events:
        public void OnMasterVolume(float v) => SettingsManager.Instance?.SetMasterVolume(v);
        public void OnMusicVolume(float v)  => SettingsManager.Instance?.SetMusicVolume(v);
        public void OnSfxVolume(float v)    => SettingsManager.Instance?.SetSfxVolume(v);
        public void OnHaptics(bool on)      => SettingsManager.Instance?.SetHaptics(on);
        public void OnTelemetry(bool on)    => SettingsManager.Instance?.SetTelemetry(on);
        public void OnAutoCheck(bool on)
        {
            SettingsManager.Instance?.SetAutoCheck(on);
            _sudoku.AutoCheckEnabled = on;
            RenderSudokuBoardAll();
        }
        public void OnThemeToggle(bool light) =>
            SettingsManager.Instance?.SetTheme(light ? UiTheme.Light : UiTheme.Dark);
        public void OnHandTrackingToggle(bool hands) =>
            SettingsManager.Instance?.SetControlScheme(hands ? ControlScheme.HandTracking : ControlScheme.TouchControllers);

        private void HandleThemeChanged(UiTheme theme)
        {
            _palette = theme == UiTheme.Light ? lightPalette : darkPalette;
            if (_palette == null) _palette = darkPalette;
            RenderTttBoard();
            RenderSudokuBoardAll();
        }

        // =========================================================================
        //  Helpers
        // =========================================================================
        private static string FormatClock(float seconds)
        {
            int s = Mathf.FloorToInt(seconds);
            return $"{s / 60:00}:{s % 60:00}";
        }
    }
}
