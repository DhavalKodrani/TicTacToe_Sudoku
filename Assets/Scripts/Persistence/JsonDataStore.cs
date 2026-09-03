// -----------------------------------------------------------------------------
//  JsonDataStore.cs
//  Tiny, dependency-free persistence helper built on JsonUtility.
//
//  Design goals:
//   * ATOMIC writes  -> write to a ".tmp" file then File.Replace so a crash /
//                       battery-death mid-write can never corrupt a save.
//   * BACKUP on write -> keeps a single ".bak" for last-known-good recovery.
//   * SANDBOXED paths -> all keys resolve under Application.persistentDataPath,
//                        which on Quest maps to the app's private storage
//                        (VRC-compliant: no PII leaves the device).
//
//  JsonUtility is used instead of Newtonsoft to avoid an extra managed DLL and
//  to stay IL2CPP/AOT friendly on Quest. Any [Serializable] class works.
// -----------------------------------------------------------------------------
using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace TTLS.Persistence
{
    public static class JsonDataStore
    {
        // Root under persistentDataPath. Everything the game writes lives here.
        private const string RootFolder = "TTLS_Save";

        private static string Root
        {
            get
            {
                string p = Path.Combine(Application.persistentDataPath, RootFolder);
                if (!Directory.Exists(p)) Directory.CreateDirectory(p);
                return p;
            }
        }

        /// <summary>Resolve a logical key (e.g. "profiles/index") to a full path.</summary>
        public static string PathFor(string relativeKey)
        {
            // Normalise separators and strip any attempt to escape the sandbox.
            relativeKey = relativeKey.Replace('\\', '/').TrimStart('/');
            if (relativeKey.Contains(".."))
                throw new ArgumentException($"Illegal key '{relativeKey}'");

            string full = Path.Combine(Root, relativeKey + ".json");
            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return full;
        }

        public static bool Exists(string relativeKey) => File.Exists(PathFor(relativeKey));

        /// <summary>Serialize and write atomically. Returns false (logged) on failure.</summary>
        public static bool Save<T>(string relativeKey, T data, bool prettyPrint = true)
        {
            try
            {
                string path = PathFor(relativeKey);
                string tmp = path + ".tmp";
                string bak = path + ".bak";

                string json = JsonUtility.ToJson(data, prettyPrint);
                File.WriteAllText(tmp, json, new UTF8Encoding(false));

                if (File.Exists(path))
                {
                    // File.Replace gives us an atomic swap + a backup in one call.
                    File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tmp, path);
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[JsonDataStore] Save failed for '{relativeKey}': {e}");
                return false;
            }
        }

        /// <summary>
        /// Load and deserialize. Falls back to the ".bak" copy if the primary file
        /// is missing or corrupt. Returns <paramref name="fallback"/> if all fail.
        /// </summary>
        public static T Load<T>(string relativeKey, T fallback = default)
        {
            string path = PathFor(relativeKey);
            T result;
            if (TryReadFile(path, out result)) return result;

            string bak = path + ".bak";
            if (File.Exists(bak) && TryReadFile(bak, out result))
            {
                Debug.LogWarning($"[JsonDataStore] Recovered '{relativeKey}' from backup.");
                return result;
            }
            return fallback;
        }

        private static bool TryReadFile<T>(string path, out T value)
        {
            value = default;
            if (!File.Exists(path)) return false;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return false;
                value = JsonUtility.FromJson<T>(json);
                return value != null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[JsonDataStore] Read failed for '{path}': {e}");
                return false;
            }
        }

        public static bool Delete(string relativeKey)
        {
            try
            {
                string path = PathFor(relativeKey);
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
                if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[JsonDataStore] Delete failed for '{relativeKey}': {e}");
                return false;
            }
        }

        /// <summary>Read a raw JSON string (used by the analytics export feature).</summary>
        public static string ReadRaw(string relativeKey)
        {
            string path = PathFor(relativeKey);
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }
    }
}
