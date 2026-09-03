// -----------------------------------------------------------------------------
//  SudokuGenerator.cs
//  Deterministic, offline generator of *validated, unique-solution* Sudoku
//  puzzles. Used to build the 500-puzzle bank (see SudokuPuzzleBank) on first
//  launch, then cached to persistentDataPath so subsequent launches are instant.
//
//  Algorithm:
//   1. Build a full valid solution via randomized back-tracking fill.
//   2. Dig holes one clue at a time, each time verifying the puzzle STILL has
//      exactly one solution (uniqueness enforced -> "validated"). This is the
//      standard, correct way to guarantee solvable + unique puzzles.
//   3. Difficulty is governed by the target number of remaining clues plus a
//      symmetry option, matching common Sudoku conventions.
//
//  A fixed RNG seed per (difficulty, index) makes the whole bank reproducible,
//  so "puzzle #237 Hard" is identical for every player and every reinstall.
// -----------------------------------------------------------------------------
using System;
using TTLS.Core;

namespace TTLS.Games.Sudoku
{
    public static class SudokuGenerator
    {
        public const int N = 9;
        public const int Cells = 81;

        // Target clue counts (givens) per difficulty. Fewer clues == harder.
        private static int TargetClues(SudokuDifficulty d)
        {
            switch (d)
            {
                case SudokuDifficulty.Easy:   return 40; // 41 holes
                case SudokuDifficulty.Medium: return 34;
                case SudokuDifficulty.Hard:   return 28;
                default:                      return 24; // Expert (minimum ~17, keep fair)
            }
        }

        /// <summary>
        /// Generate one puzzle. Returns givens (0 == blank) and the full solution,
        /// both as flat length-81 int arrays.
        /// </summary>
        public static void Generate(SudokuDifficulty difficulty, int seed,
                                    out int[] puzzle, out int[] solution)
        {
            var rng = new Random(seed);
            solution = new int[Cells];
            FillFull(solution, rng);

            puzzle = (int[])solution.Clone();
            DigHoles(puzzle, TargetClues(difficulty), rng);
        }

        // ---- Step 1: fully solved grid via randomized backtracking --------------
        private static bool FillFull(int[] grid, Random rng)
        {
            int idx = FindEmpty(grid);
            if (idx < 0) return true; // solved

            Span<int> candidates = stackalloc int[N];
            for (int i = 0; i < N; i++) candidates[i] = i + 1;
            Shuffle(candidates, rng);

            int r = idx / N, c = idx % N;
            for (int i = 0; i < N; i++)
            {
                int v = candidates[i];
                if (IsSafe(grid, r, c, v))
                {
                    grid[idx] = v;
                    if (FillFull(grid, rng)) return true;
                    grid[idx] = 0;
                }
            }
            return false;
        }

        // ---- Step 2: remove clues while preserving a UNIQUE solution ------------
        private static void DigHoles(int[] grid, int targetClues, Random rng)
        {
            // Randomized cell order; use 180-degree symmetric pairs where possible
            // for aesthetic, "proper" puzzles.
            Span<int> order = stackalloc int[Cells];
            for (int i = 0; i < Cells; i++) order[i] = i;
            Shuffle(order, rng);

            int clues = Cells;
            for (int k = 0; k < Cells && clues > targetClues; k++)
            {
                int cell = order[k];
                if (grid[cell] == 0) continue;

                int mirror = Cells - 1 - cell; // symmetric partner
                int backupA = grid[cell];
                int backupB = grid[mirror];

                grid[cell] = 0;
                if (mirror != cell) grid[mirror] = 0;

                // Verify uniqueness with the holes applied.
                if (CountSolutions(grid, 2) != 1)
                {
                    grid[cell] = backupA;                    // revert
                    if (mirror != cell) grid[mirror] = backupB;
                }
                else
                {
                    clues -= (mirror != cell) ? 2 : 1;
                }
            }
        }

        // ---- Uniqueness / solving -----------------------------------------------
        // Counts solutions up to 'cap' (early-out). cap==2 is enough to test
        // uniqueness cheaply.
        public static int CountSolutions(int[] grid, int cap)
        {
            int[] work = (int[])grid.Clone();
            int count = 0;
            SolveCount(work, ref count, cap);
            return count;
        }

        private static void SolveCount(int[] grid, ref int count, int cap)
        {
            if (count >= cap) return;
            int idx = FindEmpty(grid);
            if (idx < 0) { count++; return; }

            int r = idx / N, c = idx % N;
            for (int v = 1; v <= N; v++)
            {
                if (IsSafe(grid, r, c, v))
                {
                    grid[idx] = v;
                    SolveCount(grid, ref count, cap);
                    grid[idx] = 0;
                    if (count >= cap) return;
                }
            }
        }

        /// <summary>Standard single-solution solver (used by the hint system).</summary>
        public static bool Solve(int[] grid)
        {
            int idx = FindEmpty(grid);
            if (idx < 0) return true;
            int r = idx / N, c = idx % N;
            for (int v = 1; v <= N; v++)
            {
                if (IsSafe(grid, r, c, v))
                {
                    grid[idx] = v;
                    if (Solve(grid)) return true;
                    grid[idx] = 0;
                }
            }
            return false;
        }

        // ---- Constraint checks --------------------------------------------------
        public static bool IsSafe(int[] grid, int row, int col, int val)
        {
            for (int i = 0; i < N; i++)
            {
                if (grid[row * N + i] == val) return false; // row
                if (grid[i * N + col] == val) return false; // col
            }
            int br = (row / 3) * 3, bc = (col / 3) * 3;
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    if (grid[(br + r) * N + (bc + c)] == val) return false; // box
            return true;
        }

        private static int FindEmpty(int[] grid)
        {
            for (int i = 0; i < Cells; i++) if (grid[i] == 0) return i;
            return -1;
        }

        private static void Shuffle(Span<int> array, Random rng)
        {
            for (int i = array.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (array[i], array[j]) = (array[j], array[i]);
            }
        }
    }
}
