// -----------------------------------------------------------------------------
//  SudokuBankBaker.cs  (Editor-only)
//  Generates the 500-puzzle bank at edit time and writes it to
//  Assets/StreamingAssets/sudoku_bank.json so it ships INSIDE the APK. With a
//  baked bank present, the game never generates puzzles at runtime -> instant,
//  fully-offline first launch (ideal for a Meta Quest store submission).
//
//  Menu:  Tools > TTLS > Bake Sudoku Bank (500)
//
//  This mirrors SudokuPuzzleBank's build logic exactly (same seeds) so baked and
//  runtime-generated banks are identical.
// -----------------------------------------------------------------------------
using System.IO;
using System.Text;
using TTLS.Core;
using TTLS.Games.Sudoku;
using UnityEditor;
using UnityEngine;

namespace TTLS.EditorTools
{
    public static class SudokuBankBaker
    {
        private const int PerDifficulty = SudokuPuzzleBank.PerDifficulty; // 125
        private const int Total = SudokuPuzzleBank.Total;                 // 500
        private const int SeedBase = 1_000_000;

        [MenuItem("Tools/TTLS/Bake Sudoku Bank (500)")]
        public static void Bake()
        {
            var data = new SudokuBankData { count = Total, puzzles = new SudokuPuzzleRecord[Total] };
            var sbP = new StringBuilder(81);
            var sbS = new StringBuilder(81);

            int id = 0;
            try
            {
                for (int d = 0; d < 4; d++)
                {
                    var diff = (SudokuDifficulty)d;
                    for (int i = 0; i < PerDifficulty; i++)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "Baking Sudoku bank",
                                $"{diff} puzzle {i + 1}/{PerDifficulty}  (total {id + 1}/{Total})",
                                id / (float)Total))
                        {
                            EditorUtility.ClearProgressBar();
                            Debug.LogWarning("[SudokuBankBaker] Cancelled.");
                            return;
                        }

                        int seed = SeedBase * (d + 1) + i;
                        SudokuGenerator.Generate(diff, seed, out int[] puz, out int[] sol);

                        sbP.Clear(); sbS.Clear();
                        for (int c = 0; c < 81; c++)
                        {
                            sbP.Append((char)('0' + puz[c]));
                            sbS.Append((char)('0' + sol[c]));
                        }
                        data.puzzles[id] = new SudokuPuzzleRecord
                        {
                            id = id, difficulty = d,
                            puzzle = sbP.ToString(), solution = sbS.ToString()
                        };
                        id++;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            string dir = Path.Combine(Application.streamingAssetsPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "sudoku_bank.json");
            File.WriteAllText(path, JsonUtility.ToJson(data, false));
            AssetDatabase.Refresh();

            Debug.Log($"[SudokuBankBaker] Baked {Total} puzzles -> {path} " +
                      $"({new FileInfo(path).Length / 1024} KB)");
            EditorUtility.DisplayDialog("TTLS", $"Baked {Total} Sudoku puzzles into\nStreamingAssets/sudoku_bank.json", "OK");
        }

        [MenuItem("Tools/TTLS/Validate Sudoku Generator (quick)")]
        public static void ValidateQuick()
        {
            int checkedCount = 0;
            foreach (SudokuDifficulty d in System.Enum.GetValues(typeof(SudokuDifficulty)))
            {
                for (int i = 0; i < 5; i++)
                {
                    SudokuGenerator.Generate(d, SeedBase * ((int)d + 1) + i, out int[] puz, out int[] sol);
                    // Unique solution?
                    if (SudokuGenerator.CountSolutions(puz, 2) != 1)
                    { Debug.LogError($"[Validate] Non-unique {d} #{i}"); return; }
                    // Puzzle is a subset of the solution?
                    for (int c = 0; c < 81; c++)
                        if (puz[c] != 0 && puz[c] != sol[c])
                        { Debug.LogError($"[Validate] Mismatch {d} #{i} cell {c}"); return; }
                    checkedCount++;
                }
            }
            Debug.Log($"[Validate] OK — {checkedCount} puzzles unique & consistent.");
        }
    }
}
