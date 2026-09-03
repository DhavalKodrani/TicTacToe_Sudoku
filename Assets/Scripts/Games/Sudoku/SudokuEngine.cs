// -----------------------------------------------------------------------------
//  SudokuEngine.cs
//  Pure C# Sudoku session logic: puzzle parsing, move placement/validation,
//  pencil (candidate) notes, undo/redo, erase, hints, auto-check error
//  highlighting, and completion detection.
//
//  No MonoBehaviour: the UI observes it through events. All per-move data lives
//  in preallocated arrays / a pooled command stack to avoid GC churn while the
//  player scrubs undo/redo repeatedly.
//
//  Cell model:
//   * _given[i]  : true if the cell is a fixed clue (never editable).
//   * _values[i] : current big number (0 == empty).
//   * _notes[i]  : bit mask of pencil marks; bit (v-1) set => candidate v shown.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using TTLS.Core;

namespace TTLS.Games.Sudoku
{
    public sealed class SudokuEngine
    {
        public const int N = 9;
        public const int Cells = 81;

        private readonly bool[] _given = new bool[Cells];
        private readonly int[]  _values = new int[Cells];
        private readonly int[]  _notes = new int[Cells];   // bitmask 1..9 -> bits 0..8
        private readonly int[]  _solution = new int[Cells];

        // Undo/redo as a command stack. Commands are structs (no heap per move).
        private readonly List<Command> _undo = new List<Command>(256);
        private readonly List<Command> _redo = new List<Command>(64);

        public SudokuDifficulty Difficulty { get; private set; }
        public int PuzzleId { get; private set; }
        public int HintsUsed { get; private set; }
        public bool IsSolved { get; private set; }

        public bool AutoCheckEnabled { get; set; } = true;   // dynamic error highlight
        public bool NotesMode { get; set; }                   // pencil vs pen input

        // ---- Events -------------------------------------------------------------
        public event Action<int> OnCellChanged;   // cell index
        public event Action OnBoardChanged;        // bulk change (new game / undo)
        public event Action OnSolved;

        // ---- Public read accessors (UI binds to these) --------------------------
        public bool IsGiven(int i) => _given[i];
        public int  GetValue(int i) => _values[i];
        public int  GetNotes(int i) => _notes[i];       // raw bitmask
        public bool HasNote(int i, int v) => (_notes[i] & (1 << (v - 1))) != 0;
        public int  GetSolution(int i) => _solution[i];

        // ---- Lifecycle ----------------------------------------------------------
        public void Load(SudokuPuzzleRecord record)
        {
            PuzzleId = record.id;
            Difficulty = (SudokuDifficulty)record.difficulty;
            HintsUsed = 0;
            IsSolved = false;
            _undo.Clear();
            _redo.Clear();

            for (int i = 0; i < Cells; i++)
            {
                int p = record.puzzle[i] - '0';
                _values[i] = p;
                _given[i] = p != 0;
                _notes[i] = 0;
                _solution[i] = record.solution[i] - '0';
            }
            OnBoardChanged?.Invoke();
        }

        /// <summary>Restore a mid-game state (from a profile save).</summary>
        public void LoadState(SudokuSaveState s)
        {
            PuzzleId = s.puzzleId;
            Difficulty = (SudokuDifficulty)s.difficulty;
            HintsUsed = s.hintsUsed;
            IsSolved = false;
            _undo.Clear();
            _redo.Clear();
            for (int i = 0; i < Cells; i++)
            {
                _given[i] = s.given[i];
                _values[i] = s.values[i];
                _notes[i] = s.notes[i];
                _solution[i] = s.solution[i];
            }
            OnBoardChanged?.Invoke();
            CheckSolved();
        }

        public SudokuSaveState CaptureState()
        {
            var s = new SudokuSaveState
            {
                puzzleId = PuzzleId,
                difficulty = (int)Difficulty,
                hintsUsed = HintsUsed,
                given = (bool[])_given.Clone(),
                values = (int[])_values.Clone(),
                notes = (int[])_notes.Clone(),
                solution = (int[])_solution.Clone()
            };
            return s;
        }

        // ---- Editing ------------------------------------------------------------
        public bool CanEdit(int cell) => cell >= 0 && cell < Cells && !_given[cell] && !IsSolved;

        /// <summary>
        /// Primary input. In NotesMode it toggles a pencil mark; otherwise it sets
        /// the big value (setting a value clears that cell's notes). All edits are
        /// recorded for undo.
        /// </summary>
        public void Input(int cell, int value)
        {
            if (!CanEdit(cell)) return;
            if (value < 1 || value > 9) return;

            if (NotesMode)
            {
                ToggleNote(cell, value);
            }
            else
            {
                if (_values[cell] == value) { Erase(cell); return; } // tap again = clear
                PushEdit(cell, _values[cell], value, _notes[cell], 0);
                _values[cell] = value;
                _notes[cell] = 0;
                AfterEdit(cell);
            }
        }

        public void ToggleNote(int cell, int value)
        {
            if (!CanEdit(cell)) return;
            int before = _notes[cell];
            int after = before ^ (1 << (value - 1));
            PushEdit(cell, _values[cell], _values[cell], before, after);
            _notes[cell] = after;
            AfterEdit(cell);
        }

        public void Erase(int cell)
        {
            if (!CanEdit(cell)) return;
            if (_values[cell] == 0 && _notes[cell] == 0) return;
            PushEdit(cell, _values[cell], 0, _notes[cell], 0);
            _values[cell] = 0;
            _notes[cell] = 0;
            AfterEdit(cell);
        }

        private void AfterEdit(int cell)
        {
            _redo.Clear();          // any new edit invalidates the redo branch
            OnCellChanged?.Invoke(cell);
            CheckSolved();
        }

        // ---- Undo / Redo --------------------------------------------------------
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public void Undo()
        {
            if (_undo.Count == 0) return;
            Command c = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);

            _values[c.cell] = c.valueBefore;
            _notes[c.cell] = c.notesBefore;
            _redo.Add(c);
            OnCellChanged?.Invoke(c.cell);
            IsSolved = false;
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            Command c = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);

            _values[c.cell] = c.valueAfter;
            _notes[c.cell] = c.notesAfter;
            _undo.Add(c);
            OnCellChanged?.Invoke(c.cell);
            CheckSolved();
        }

        private void PushEdit(int cell, int vBefore, int vAfter, int nBefore, int nAfter)
        {
            _undo.Add(new Command
            {
                cell = cell,
                valueBefore = vBefore, valueAfter = vAfter,
                notesBefore = nBefore, notesAfter = nAfter
            });
        }

        // ---- Hints --------------------------------------------------------------
        /// <summary>
        /// Reveal the correct value for one cell. Prefers the currently selected
        /// cell if editable+empty/wrong; else fills the first incorrect/empty cell.
        /// Counts toward HintsUsed (tracked in analytics). Returns the cell filled.
        /// </summary>
        public int Hint(int preferredCell = -1)
        {
            int target = -1;
            if (preferredCell >= 0 && CanEdit(preferredCell) &&
                _values[preferredCell] != _solution[preferredCell])
            {
                target = preferredCell;
            }
            else
            {
                for (int i = 0; i < Cells; i++)
                {
                    if (!_given[i] && _values[i] != _solution[i]) { target = i; break; }
                }
            }
            if (target < 0) return -1;

            PushEdit(target, _values[target], _solution[target], _notes[target], 0);
            _values[target] = _solution[target];
            _notes[target] = 0;
            HintsUsed++;
            _redo.Clear();
            OnCellChanged?.Invoke(target);
            CheckSolved();
            return target;
        }

        // ---- Validation / error highlight --------------------------------------
        /// <summary>
        /// Is this filled cell currently in conflict? Used for dynamic red
        /// highlighting when AutoCheckEnabled. A given or empty cell is never an
        /// error. O(27) per cell, allocation-free.
        /// </summary>
        public bool IsError(int cell)
        {
            if (!AutoCheckEnabled) return false;
            int v = _values[cell];
            if (v == 0) return false;

            int row = cell / N, col = cell % N;
            for (int i = 0; i < N; i++)
            {
                int rIdx = row * N + i;
                if (rIdx != cell && _values[rIdx] == v) return true;
                int cIdx = i * N + col;
                if (cIdx != cell && _values[cIdx] == v) return true;
            }
            int br = (row / 3) * 3, bc = (col / 3) * 3;
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                {
                    int idx = (br + r) * N + (bc + c);
                    if (idx != cell && _values[idx] == v) return true;
                }
            return false;
        }

        /// <summary>True if every filled cell matches the known solution.</summary>
        public bool IsCorrectSoFar()
        {
            for (int i = 0; i < Cells; i++)
                if (_values[i] != 0 && _values[i] != _solution[i]) return false;
            return true;
        }

        public int FilledCount()
        {
            int n = 0;
            for (int i = 0; i < Cells; i++) if (_values[i] != 0) n++;
            return n;
        }

        private void CheckSolved()
        {
            for (int i = 0; i < Cells; i++)
                if (_values[i] != _solution[i]) { IsSolved = false; return; }
            IsSolved = true;
            OnSolved?.Invoke();
        }

        // ---- Command struct (value type -> no per-move heap allocation) ---------
        private struct Command
        {
            public int cell;
            public int valueBefore, valueAfter;
            public int notesBefore, notesAfter;
        }
    }

    /// <summary>Serializable snapshot for resume-in-progress persistence.</summary>
    [Serializable]
    public class SudokuSaveState
    {
        public int puzzleId;
        public int difficulty;
        public int hintsUsed;
        public bool[] given;
        public int[] values;
        public int[] notes;
        public int[] solution;
        public float elapsedSeconds; // filled in by UIManager on save
    }
}
