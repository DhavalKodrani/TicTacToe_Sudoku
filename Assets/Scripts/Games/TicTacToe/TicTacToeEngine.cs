// -----------------------------------------------------------------------------
//  TicTacToeEngine.cs
//  Pure C# game logic for 3x3 Tic-Tac-Toe. NO MonoBehaviour / no scene coupling
//  so it is unit-testable and reusable. The UI layer (UIManager) observes it via
//  plain C# events.
//
//  AI:
//   * Easy       -> random legal move.
//   * Medium     -> "win if you can, block if you must, else random" (heuristic).
//   * Unbeatable -> full MiniMax with alpha-beta pruning + depth-preferred scoring
//                   (wins sooner / loses later). Mathematically cannot be beaten.
//
//  Allocation notes:
//   * Board is a fixed 9-length byte[] allocated once. Moves mutate in place.
//   * MiniMax recurses on the same array (make-move / undo-move) => ZERO per-node
//     allocation, safe to run every frame if needed.
//   * Win detection uses a static readonly int[8][3] line table (no LINQ).
// -----------------------------------------------------------------------------
using System;
using TTLS.Core;

namespace TTLS.Games.TicTacToe
{
    public sealed class TicTacToeEngine
    {
        // The 8 winning lines expressed as flat indices 0..8 (row-major board).
        private static readonly int[][] Lines =
        {
            new[] {0,1,2}, new[] {3,4,5}, new[] {6,7,8}, // rows
            new[] {0,3,6}, new[] {1,4,7}, new[] {2,5,8}, // cols
            new[] {0,4,8}, new[] {2,4,6}                  // diagonals
        };

        private readonly Mark[] _board = new Mark[9];
        private readonly Random _rng = new Random();

        public TicTacToeMode Mode { get; private set; }
        public Mark HumanMark { get; private set; } = Mark.X;   // human is always X here
        public Mark AiMark => HumanMark == Mark.X ? Mark.O : Mark.X;
        public Mark CurrentTurn { get; private set; } = Mark.X;
        public bool IsGameOver { get; private set; }
        public GameOutcome Outcome { get; private set; } = GameOutcome.None;
        public int[] WinningLine { get; private set; } // 3 indices, or null

        /// <summary>Read-only accessor for the UI. Index 0..8, row-major.</summary>
        public Mark this[int index] => _board[index];

        // ---- Events (UI subscribes; engine never touches Unity) -----------------
        public event Action<int, Mark> OnMovePlaced;     // (cellIndex, mark)
        public event Action OnBoardChanged;              // any state change
        public event Action<GameOutcome, int[]> OnGameOver; // (outcome, winningLine)

        public void NewGame(TicTacToeMode mode, Mark humanMark = Mark.X)
        {
            Mode = mode;
            HumanMark = humanMark;
            Array.Clear(_board, 0, _board.Length);
            CurrentTurn = Mark.X;      // X always starts
            IsGameOver = false;
            Outcome = GameOutcome.None;
            WinningLine = null;
            OnBoardChanged?.Invoke();

            // If AI owns X (i.e. human chose O) it opens immediately.
            if (Mode != TicTacToeMode.PassAndPlay && CurrentTurn == AiMark)
                PlayAiTurn();
        }

        public bool CanPlace(int index) =>
            !IsGameOver && index >= 0 && index < 9 && _board[index] == Mark.Empty;

        /// <summary>
        /// Human (or pass-and-play) tap on a cell. Returns true if it was legal.
        /// After a human move against the AI, the AI's reply is played automatically.
        /// </summary>
        public bool PlaceHuman(int index)
        {
            if (!CanPlace(index)) return false;
            Place(index, CurrentTurn);
            if (IsGameOver) return true;

            if (Mode != TicTacToeMode.PassAndPlay && CurrentTurn == AiMark)
                PlayAiTurn();
            return true;
        }

        private void Place(int index, Mark mark)
        {
            _board[index] = mark;
            OnMovePlaced?.Invoke(index, mark);

            if (CheckVictory(_board, mark, out int[] line))
            {
                WinningLine = line;
                IsGameOver = true;
                Outcome = ResolveOutcome(mark);
                OnBoardChanged?.Invoke();
                OnGameOver?.Invoke(Outcome, WinningLine);
            }
            else if (IsBoardFull(_board))
            {
                IsGameOver = true;
                Outcome = GameOutcome.Draw;
                OnBoardChanged?.Invoke();
                OnGameOver?.Invoke(Outcome, null);
            }
            else
            {
                CurrentTurn = mark == Mark.X ? Mark.O : Mark.X;
                OnBoardChanged?.Invoke();
            }
        }

        private GameOutcome ResolveOutcome(Mark winner)
        {
            if (Mode == TicTacToeMode.PassAndPlay)
                return GameOutcome.Win; // caller inspects winner via WinningLine/marks
            return winner == HumanMark ? GameOutcome.Win : GameOutcome.Loss;
        }

        // ---- AI dispatch --------------------------------------------------------
        public void PlayAiTurn()
        {
            if (IsGameOver) return;
            int move;
            switch (Mode)
            {
                case TicTacToeMode.VsAiEasy:   move = PickRandomMove(); break;
                case TicTacToeMode.VsAiMedium: move = PickMediumMove(); break;
                default:                       move = PickBestMove();   break; // Unbeatable
            }
            if (move >= 0) Place(move, CurrentTurn);
        }

        private int PickRandomMove()
        {
            Span<int> empties = stackalloc int[9];
            int n = CollectEmpties(empties);
            return n == 0 ? -1 : empties[_rng.Next(n)];
        }

        // Heuristic: take a winning move; else block opponent's winning move; else
        // prefer centre, then corners, then random. Beatable but feels "smart".
        private int PickMediumMove()
        {
            Mark me = CurrentTurn;
            Mark foe = me == Mark.X ? Mark.O : Mark.X;

            int win = FindImmediate(me);   if (win >= 0) return win;
            int block = FindImmediate(foe); if (block >= 0) return block;
            if (_board[4] == Mark.Empty) return 4;

            ReadOnlySpan<int> corners = stackalloc int[] { 0, 2, 6, 8 };
            for (int i = 0; i < corners.Length; i++)
                if (_board[corners[i]] == Mark.Empty) return corners[i];

            return PickRandomMove();
        }

        // Returns the cell that immediately completes a line for 'mark', or -1.
        private int FindImmediate(Mark mark)
        {
            for (int i = 0; i < 9; i++)
            {
                if (_board[i] != Mark.Empty) continue;
                _board[i] = mark;
                bool wins = CheckVictory(_board, mark, out _);
                _board[i] = Mark.Empty;
                if (wins) return i;
            }
            return -1;
        }

        // ---- MiniMax (Unbeatable) ----------------------------------------------
        private int PickBestMove()
        {
            Mark me = CurrentTurn;
            int bestScore = int.MinValue;
            int bestMove = -1;

            for (int i = 0; i < 9; i++)
            {
                if (_board[i] != Mark.Empty) continue;
                _board[i] = me;
                int score = MiniMax(_board, 0, false, me, int.MinValue, int.MaxValue);
                _board[i] = Mark.Empty;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMove = i;
                }
            }
            return bestMove;
        }

        // depth is added/subtracted so the AI wins in the fewest moves and, when
        // losing is unavoidable, delays it as long as possible.
        private int MiniMax(Mark[] b, int depth, bool maximizing, Mark me,
                            int alpha, int beta)
        {
            Mark foe = me == Mark.X ? Mark.O : Mark.X;

            if (CheckVictory(b, me, out _)) return 10 - depth;
            if (CheckVictory(b, foe, out _)) return depth - 10;
            if (IsBoardFull(b)) return 0;

            if (maximizing)
            {
                int best = int.MinValue;
                for (int i = 0; i < 9; i++)
                {
                    if (b[i] != Mark.Empty) continue;
                    b[i] = me;
                    best = Math.Max(best, MiniMax(b, depth + 1, false, me, alpha, beta));
                    b[i] = Mark.Empty;
                    alpha = Math.Max(alpha, best);
                    if (beta <= alpha) break; // prune
                }
                return best;
            }
            else
            {
                int best = int.MaxValue;
                for (int i = 0; i < 9; i++)
                {
                    if (b[i] != Mark.Empty) continue;
                    b[i] = foe;
                    best = Math.Min(best, MiniMax(b, depth + 1, true, me, alpha, beta));
                    b[i] = Mark.Empty;
                    beta = Math.Min(beta, best);
                    if (beta <= alpha) break; // prune
                }
                return best;
            }
        }

        // ---- Static helpers -----------------------------------------------------
        private static bool CheckVictory(Mark[] b, Mark mark, out int[] line)
        {
            for (int i = 0; i < Lines.Length; i++)
            {
                int[] l = Lines[i];
                if (b[l[0]] == mark && b[l[1]] == mark && b[l[2]] == mark)
                {
                    line = l;
                    return true;
                }
            }
            line = null;
            return false;
        }

        private static bool IsBoardFull(Mark[] b)
        {
            for (int i = 0; i < 9; i++) if (b[i] == Mark.Empty) return false;
            return true;
        }

        private int CollectEmpties(Span<int> buffer)
        {
            int n = 0;
            for (int i = 0; i < 9; i++)
                if (_board[i] == Mark.Empty) buffer[n++] = i;
            return n;
        }

        /// <summary>For pass-and-play scoreboards: who physically owns the win.</summary>
        public Mark Winner =>
            WinningLine == null ? Mark.Empty : _board[WinningLine[0]];
    }
}
