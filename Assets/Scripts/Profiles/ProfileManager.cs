// -----------------------------------------------------------------------------
//  ProfileManager.cs
//  Central authority for up to 4 isolated local profiles. Handles creation,
//  deletion, switching, and routing every profile's data to/from disk.
//
//  Storage layout (all under persistentDataPath/TTLS_Save/):
//     profiles/index          -> ProfileIndex (which slots exist + last active)
//     profiles/p0 .. p3       -> one PlayerProfile per slot (fully isolated)
//
//  Isolation guarantee: each profile is a separate file; loading profile B never
//  reads profile A's data. Deleting a profile removes only its own file.
//
//  This is a MonoBehaviour singleton so other systems (UIManager, analytics) can
//  reach the ActiveProfile, but all heavy lifting is delegated to JsonDataStore.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using TTLS.Persistence;
using UnityEngine;

namespace TTLS.Profiles
{
    [Serializable]
    public class ProfileIndex
    {
        public const int MaxProfiles = 4;
        public bool[] slotUsed = new bool[MaxProfiles];
        public string lastActiveId = "";
    }

    public class ProfileManager : MonoBehaviour
    {
        public const string IndexKey = "profiles/index";
        public const int MaxProfiles = ProfileIndex.MaxProfiles;

        public static ProfileManager Instance { get; private set; }

        public PlayerProfile ActiveProfile { get; private set; }
        public bool HasActiveProfile => ActiveProfile != null;

        public event Action<PlayerProfile> OnActiveProfileChanged;
        public event Action OnProfilesChanged; // slot added/removed/renamed

        private ProfileIndex _index;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadIndex();
        }

        private void LoadIndex()
        {
            _index = JsonDataStore.Load(IndexKey, new ProfileIndex());
            if (_index.slotUsed == null || _index.slotUsed.Length != MaxProfiles)
                _index.slotUsed = new bool[MaxProfiles];
        }

        private void SaveIndex() => JsonDataStore.Save(IndexKey, _index);

        private static string SlotKey(int slot) => $"profiles/p{slot}";
        private static string SlotId(int slot) => $"p{slot}";
        private static int SlotFromId(string id) =>
            int.TryParse(id.Substring(1), out int s) ? s : -1;

        // ---- Queries ------------------------------------------------------------
        public int UsedCount()
        {
            int n = 0;
            for (int i = 0; i < MaxProfiles; i++) if (_index.slotUsed[i]) n++;
            return n;
        }

        public bool CanCreate() => UsedCount() < MaxProfiles;
        public bool SlotExists(int slot) => slot >= 0 && slot < MaxProfiles && _index.slotUsed[slot];

        /// <summary>Load all existing profile headers (for the selection screen).</summary>
        public List<PlayerProfile> GetAllProfiles()
        {
            var list = new List<PlayerProfile>(MaxProfiles);
            for (int i = 0; i < MaxProfiles; i++)
            {
                if (!_index.slotUsed[i]) continue;
                var p = JsonDataStore.Load<PlayerProfile>(SlotKey(i));
                if (p != null) list.Add(p);
            }
            return list;
        }

        public int FirstFreeSlot()
        {
            for (int i = 0; i < MaxProfiles; i++) if (!_index.slotUsed[i]) return i;
            return -1;
        }

        // ---- Mutations ----------------------------------------------------------
        public PlayerProfile CreateProfile(string name, int avatarIndex)
        {
            int slot = FirstFreeSlot();
            if (slot < 0)
            {
                Debug.LogWarning("[ProfileManager] Max 4 profiles reached.");
                return null;
            }
            var profile = PlayerProfile.CreateNew(SlotId(slot), name, avatarIndex);
            JsonDataStore.Save(SlotKey(slot), profile);
            _index.slotUsed[slot] = true;
            SaveIndex();
            OnProfilesChanged?.Invoke();
            return profile;
        }

        public bool DeleteProfile(string profileId)
        {
            int slot = SlotFromId(profileId);
            if (!SlotExists(slot)) return false;

            JsonDataStore.Delete(SlotKey(slot));
            _index.slotUsed[slot] = false;
            if (_index.lastActiveId == profileId) _index.lastActiveId = "";
            SaveIndex();

            if (ActiveProfile != null && ActiveProfile.profileId == profileId)
            {
                ActiveProfile = null;
                OnActiveProfileChanged?.Invoke(null);
            }
            OnProfilesChanged?.Invoke();
            return true;
        }

        public bool SetActive(string profileId)
        {
            int slot = SlotFromId(profileId);
            if (!SlotExists(slot)) return false;

            var profile = JsonDataStore.Load<PlayerProfile>(SlotKey(slot));
            if (profile == null) return false;

            // Ensure sub-records are never null after a schema migration.
            profile.settings ??= new ProfileSettings();
            profile.stats ??= new ProfileStats();
            profile.inProgress ??= new InProgressGames();
            if (string.IsNullOrEmpty(profile.analyticsClientId))
                profile.analyticsClientId = Guid.NewGuid().ToString("N");

            ActiveProfile = profile;
            profile.lastPlayedUtcTicks = DateTime.UtcNow.Ticks;
            _index.lastActiveId = profileId;
            SaveIndex();
            SaveActive();

            OnActiveProfileChanged?.Invoke(profile);
            return true;
        }

        public string LastActiveId => _index?.lastActiveId ?? "";

        /// <summary>Persist the current in-memory ActiveProfile back to its slot.</summary>
        public void SaveActive()
        {
            if (ActiveProfile == null) return;
            int slot = SlotFromId(ActiveProfile.profileId);
            if (slot < 0) return;
            JsonDataStore.Save(SlotKey(slot), ActiveProfile);
        }

        public void RenameActive(string newName)
        {
            if (ActiveProfile == null) return;
            ActiveProfile.displayName = string.IsNullOrWhiteSpace(newName)
                ? ActiveProfile.displayName : newName.Trim();
            SaveActive();
            OnProfilesChanged?.Invoke();
        }

        public void SetActiveAvatar(int avatarIndex)
        {
            if (ActiveProfile == null) return;
            ActiveProfile.avatarIndex = avatarIndex;
            SaveActive();
            OnProfilesChanged?.Invoke();
        }
    }
}
