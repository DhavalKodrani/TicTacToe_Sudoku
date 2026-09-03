// -----------------------------------------------------------------------------
//  SudokuPuzzleBank.cs
//  Owns the "bank of 500 unique, validated Sudoku puzzles" required by the spec.
//
//  Strategy (offline & deterministic):
//   * 125 puzzles per difficulty x 4 difficulties = 500 total.
//   * Each puzzle is generated from a fixed, reproducible seed, so the bank is
//     byte-for-byte identical on every device and every reinstall -> puzzle IDs
//     are stable for analytics ("sudoku_puzzle_id").
//   * First launch builds the bank on a background thread and caches it as a
//     single JSON file under persistentDataPath. Subsequent launches just load
//     the cache (a few ms) -> no per-session generation cost, fully offline.
//
//  Puzzles are stored as compact 81-char strings ('0' = blank) to keep the JSON
//  small (~0.5 MB for the whole bank) and parsing allocation-light.
// -----------------------------------------------------------------------------
using System;
using System.Collections;
using System.Text;
using System.IO;
using System.Threading;
using TTLS.Core;
using TTLS.Persistence;
using UnityEngine;

namespace TTLS.Games.Sudoku
{
    [Serializable]
    public struct SudokuPuzzleRecord
    {
        public int id;                 // 0..499, stable
        public int difficulty;         // (int)SudokuDifficulty
        public string puzzle;          // 81 chars, '0' == blank
        public string solution;        // 81 chars, full solution
    }

    [Serializable]
    public class SudokuBankData
    {
        public int version = 1;
        public int count;
        public SudokuPuzzleRecord[] puzzles;
    }

    /// <summary>
    /// Singleton-style component. Add ONE to a bootstrap GameObject. Build/load is
    /// async so the boot screen never hitches.
    /// </summary>
    public class SudokuPuzzleBank : MonoBehaviour
    {
        public const string BankKey = "sudoku/bank";
        public const int PerDifficulty = 125;
        public const int Total = PerDifficulty * 4; // 500

        // A large base offset per difficulty keeps seed ranges from colliding.
        private const int SeedBase = 1_000_000;

        [Tooltip("If a pre-baked bank exists in StreamingAssets, load it instead of " +
                 "generating on first launch. Bake it via Tools > TTLS > Bake Sudoku Bank.")]
        [SerializeField] private bool preferStreamingAssets = true;
        [SerializeField] private string streamingAssetsFile = "sudoku_bank.json";

        public static SudokuPuzzleBank Instance { get; private set; }

        public bool IsReady { get; private set; }
        public float BuildProgress01 { get; private set; } // 0..1 for a loading bar
        public event Action OnBankReady;

        private SudokuBankData _data;

        // Background-build handshake (thread -> main thread).
        private volatile bool _bgDone;
        private volatile int _bgBuilt;
        private SudokuBankData _bgResult;
        private Thread _bgThread;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start() => StartCoroutine(InitRoutine());

        private IEnumerator InitRoutine()
        {
            // 1) Try the cache first (instant path).
            if (JsonDataStore.Exists(BankKey))
            {
                _data = JsonDataStore.Load<SudokuBankData>(BankKey);
                if (_data != null && _data.puzzles != null && _data.count == Total)
                {
                    IsReady = true;
                    BuildProgress01 = 1f;
                    OnBankReady?.Invoke();
                    yield break;
                }
            }

            // 2) Try a pre-baked bank shipped inside the APK (StreamingAssets).
            //    On Android this lives inside the .jar, so it must be read with
            //    UnityWebRequest rather than File IO.
            if (preferStreamingAssets)
            {
                string saPath = Path.Combine(Application.streamingAssetsPath, streamingAssetsFile);
                string json = null;

                if (saPath.Contains("://")) // Android / compressed StreamingAssets
                {
                    using (var req = UnityEngine.Networking.UnityWebRequest.Get(saPath))
                    {
                        yield return req.SendWebRequest();
#if UNITY_2020_2_OR_NEWER
                        if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
#else
                        if (!req.isNetworkError && !req.isHttpError)
#endif
                            json = req.downloadHandler.text;
                    }
                }
                else if (File.Exists(saPath)) // Editor / desktop
                {
                    json = File.ReadAllText(saPath);
                }

                if (!string.IsNullOrEmpty(json))
                {
                    var baked = JsonUtility.FromJson<SudokuBankData>(json);
                    if (baked != null && baked.puzzles != null && baked.count == Total)
                    {
                        _data = baked;
                        JsonDataStore.Save(BankKey, _data, prettyPrint: false); // cache locally
                        IsReady = true;
                        BuildProgress01 = 1f;
                        OnBankReady?.Invoke();
                        yield break;
                    }
                }
            }

            // 3) No cache, no baked bank -> build on a background thread; poll from main.
            _bgDone = false;
            _bgBuilt = 0;
            _bgThread = new Thread(BuildWorker) { IsBackground = true, Name = "SudokuBankBuild" };
            _bgThread.Start();

            while (!_bgDone)
            {
                BuildProgress01 = Mathf.Clamp01(_bgBuilt / (float)Total);
                yield return null; // one frame; keeps VR at framerate
            }

            _data = _bgResult;
            BuildProgress01 = 1f;

            // 3) Persist so we never rebuild again.
            JsonDataStore.Save(BankKey, _data, prettyPrint: false);

            IsReady = true;
            OnBankReady?.Invoke();
        }

        // Runs OFF the main thread. Only touches plain arrays + the generator.
        private void BuildWorker()
        {
            var data = new SudokuBankData { count = Total, puzzles = new SudokuPuzzleRecord[Total] };
            var sbPuzzle = new StringBuilder(SudokuGenerator.Cells);
            var sbSolution = new StringBuilder(SudokuGenerator.Cells);

            int id = 0;
            for (int d = 0; d < 4; d++)
            {
                var diff = (SudokuDifficulty)d;
                for (int i = 0; i < PerDifficulty; i++)
                {
                    int seed = SeedBase * (d + 1) + i;
                    SudokuGenerator.Generate(diff, seed, out int[] puz, out int[] sol);

                    sbPuzzle.Clear();
                    sbSolution.Clear();
                    for (int c = 0; c < SudokuGenerator.Cells; c++)
                    {
                        sbPuzzle.Append((char)('0' + puz[c]));
                        sbSolution.Append((char)('0' + sol[c]));
                    }

                    data.puzzles[id] = new SudokuPuzzleRecord
                    {
                        id = id,
                        difficulty = d,
                        puzzle = sbPuzzle.ToString(),
                        solution = sbSolution.ToString()
                    };
                    id++;
                    _bgBuilt = id;
                }
            }

            _bgResult = data;
            _bgDone = true;
        }

        // ---- Query API ----------------------------------------------------------
        public int CountFor(SudokuDifficulty difficulty) => PerDifficulty;

        /// <summary>Get a specific puzzle by its stable global id (0..499).</summary>
        public SudokuPuzzleRecord GetById(int id) => _data.puzzles[id];

        /// <summary>Get the nth puzzle (0..124) of a difficulty.</summary>
        public SudokuPuzzleRecord Get(SudokuDifficulty difficulty, int indexWithinDifficulty)
        {
            int id = (int)difficulty * PerDifficulty +
                     Mathf.Clamp(indexWithinDifficulty, 0, PerDifficulty - 1);
            return _data.puzzles[id];
        }

        /// <summary>Convert an 81-char record string into a fresh int[81].</summary>
        public static int[] ToGrid(string s)
        {
            var g = new int[SudokuGenerator.Cells];
            for (int i = 0; i < g.Length && i < s.Length; i++)
                g[i] = s[i] - '0';
            return g;
        }
    }
}
