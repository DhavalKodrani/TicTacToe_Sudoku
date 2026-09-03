// -----------------------------------------------------------------------------
//  GoogleAnalyticsManager.cs
//  Lightweight, NON-BLOCKING GA4 telemetry via the Measurement Protocol.
//
//  Endpoint:  POST https://www.google-analytics.com/mp/collect
//                  ?measurement_id=G-XXXXXXX&api_secret=XXXXXXXX
//  Body:      { "client_id": "<anon-guid>", "events": [ { name, params }, ... ] }
//
//  Offline-first design (Meta Quest is often used offline):
//   * Every event is appended to an on-disk JSON queue immediately.
//   * A coroutine flushes the queue in BATCHES (GA4 allows up to 25 events per
//     request) whenever Application.internetReachability != NotReachable.
//   * On a failed/again-offline send, events stay queued and retry later.
//   * The queue is capped so a very long offline stretch can't grow unbounded.
//
//  Privacy / VRC:
//   * client_id is the active profile's anonymized GUID (NOT a hardware id).
//   * A per-profile telemetry toggle (settings.telemetryEnabled) gates ALL sends;
//     when off, nothing is queued or transmitted.
//   * No PII, no device ids, no controller serials are ever collected.
//
//  Setup: put your GA4 Measurement ID + API secret in the inspector fields
//  (or via SetCredentials at runtime). Without them the manager no-ops safely.
// -----------------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TTLS.Core;
using TTLS.Persistence;
using TTLS.Profiles;
using UnityEngine;
using UnityEngine.Networking;

namespace TTLS.Analytics
{
    public class GoogleAnalyticsManager : MonoBehaviour
    {
        [Header("GA4 Credentials (Measurement Protocol)")]
        [Tooltip("GA4 Measurement ID, e.g. G-XXXXXXXXXX")]
        [SerializeField] private string measurementId = "";
        [Tooltip("GA4 API secret from Admin > Data Streams > Measurement Protocol API secrets")]
        [SerializeField] private string apiSecret = "";
        [Tooltip("Use the GA4 debug endpoint (validates events, does not record).")]
        [SerializeField] private bool debugValidationMode = false;

        [Header("Behaviour")]
        [SerializeField] private float flushIntervalSeconds = 15f;
        [SerializeField] private int maxBatchSize = 25;       // GA4 hard limit
        [SerializeField] private int maxQueuedEvents = 1000;  // safety cap

        public static GoogleAnalyticsManager Instance { get; private set; }

        private const string QueueKey = "analytics/ga4_queue";
        private const string ProdUrl  = "https://www.google-analytics.com/mp/collect";
        private const string DebugUrl = "https://www.google-analytics.com/debug/mp/collect";

        private EventQueue _queue;
        private bool _sending;

        private ProfileManager Profiles => ProfileManager.Instance;

        // Telemetry is allowed only when the active profile opted in AND creds exist.
        private bool TelemetryAllowed =>
            !string.IsNullOrEmpty(measurementId) &&
            !string.IsNullOrEmpty(apiSecret) &&
            Profiles?.ActiveProfile?.settings != null &&
            Profiles.ActiveProfile.settings.telemetryEnabled;

        private string ClientId =>
            Profiles?.ActiveProfile?.analyticsClientId ?? SystemAnonId();

        // ---- Lifecycle ----------------------------------------------------------
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _queue = JsonDataStore.Load(QueueKey, new EventQueue());
            if (_queue.events == null) _queue.events = new List<QueuedEvent>();
        }

        private void Start() => StartCoroutine(FlushLoop());

        public void SetCredentials(string mid, string secret)
        {
            measurementId = mid;
            apiSecret = secret;
        }

        // ---- Public event API (called by LocalAnalytics / UIManager) ------------
        public void LogScreenView(string screenName, string pageTitle = null)
        {
            var p = NewParams(4);
            p["screen_name"] = screenName;
            p["page_title"] = pageTitle ?? screenName;
            Enqueue("screen_view", p);
        }

        public void LogGameStart(GameType gameType, string difficulty)
        {
            var p = NewParams(2);
            p["game_type"] = gameType.ToString();
            p["difficulty"] = difficulty ?? "unknown";
            Enqueue("game_start", p);
        }

        public void LogGameComplete(GameType gameType, float timeTakenSeconds,
                                    GameOutcome outcome, int hintsUsed, string difficulty = null)
        {
            var p = NewParams(5);
            p["game_type"] = gameType.ToString();
            p["time_taken_seconds"] = Mathf.RoundToInt(timeTakenSeconds).ToString();
            p["outcome"] = outcome.ToString();
            p["hints_used"] = hintsUsed.ToString();
            if (difficulty != null) p["difficulty"] = difficulty;
            Enqueue("game_complete", p);
        }

        /// <summary>VR settings / interaction preference change events.</summary>
        public void LogSettingChanged(string settingName, string newValue)
        {
            var p = NewParams(2);
            p["setting_name"] = settingName;
            p["new_value"] = newValue;
            Enqueue("setting_changed", p);
        }

        // ---- Queue plumbing -----------------------------------------------------
        private void Enqueue(string name, Dictionary<string, string> parameters)
        {
            if (!TelemetryAllowed) return; // opted out or no creds -> drop silently

            var evt = new QueuedEvent
            {
                name = SanitizeName(name),
                clientId = ClientId,
                keys = new List<string>(parameters.Count),
                values = new List<string>(parameters.Count),
                unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            foreach (var kv in parameters)
            {
                evt.keys.Add(kv.Key);
                evt.values.Add(kv.Value);
            }

            _queue.events.Add(evt);
            // Drop oldest if we overflow the safety cap (keeps newest signal).
            if (_queue.events.Count > maxQueuedEvents)
                _queue.events.RemoveRange(0, _queue.events.Count - maxQueuedEvents);

            PersistQueue();
        }

        private void PersistQueue() => JsonDataStore.Save(QueueKey, _queue, prettyPrint: false);

        // ---- Flush loop ---------------------------------------------------------
        private IEnumerator FlushLoop()
        {
            var wait = new WaitForSecondsRealtime(flushIntervalSeconds);
            while (true)
            {
                yield return wait;
                if (!_sending &&
                    _queue.events.Count > 0 &&
                    Application.internetReachability != NetworkReachability.NotReachable &&
                    !string.IsNullOrEmpty(measurementId) &&
                    !string.IsNullOrEmpty(apiSecret))
                {
                    yield return StartCoroutine(FlushOnce());
                }
            }
        }

        /// <summary>Public hook so UI can force a flush (e.g. after reconnecting).</summary>
        public void RequestFlush()
        {
            if (!_sending && isActiveAndEnabled) StartCoroutine(FlushOnce());
        }

        private IEnumerator FlushOnce()
        {
            _sending = true;

            // GA4 requires one client_id per request, so group by client_id and
            // send up to maxBatchSize events at a time.
            while (_queue.events.Count > 0 &&
                   Application.internetReachability != NetworkReachability.NotReachable)
            {
                string cid = _queue.events[0].clientId;

                // Collect a contiguous-ish batch sharing this client_id.
                var batch = new List<QueuedEvent>(maxBatchSize);
                for (int i = 0; i < _queue.events.Count && batch.Count < maxBatchSize; i++)
                {
                    if (_queue.events[i].clientId == cid) batch.Add(_queue.events[i]);
                }

                string url = $"{(debugValidationMode ? DebugUrl : ProdUrl)}" +
                             $"?measurement_id={UnityWebRequest.EscapeURL(measurementId)}" +
                             $"&api_secret={UnityWebRequest.EscapeURL(apiSecret)}";
                string body = BuildPayload(cid, batch);

                using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                    req.uploadHandler = new UploadHandlerRaw(bytes);
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.SetRequestHeader("Content-Type", "application/json");
                    req.timeout = 10;

                    yield return req.SendWebRequest();

                    bool ok =
#if UNITY_2020_2_OR_NEWER
                        req.result == UnityWebRequest.Result.Success;
#else
                        !req.isNetworkError && !req.isHttpError;
#endif
                    if (ok)
                    {
                        // GA4 MP returns 204 No Content on success (or 200 in debug).
                        RemoveBatch(batch);
                        PersistQueue();
                        if (debugValidationMode)
                            Debug.Log($"[GA4] Validation response: {req.downloadHandler.text}");
                    }
                    else
                    {
                        // Leave events queued; try again on the next loop.
                        Debug.LogWarning($"[GA4] Send failed ({req.responseCode}): {req.error}. " +
                                         $"{_queue.events.Count} events remain queued.");
                        break; // stop this flush; back off to next interval
                    }
                }
            }

            _sending = false;
        }

        private void RemoveBatch(List<QueuedEvent> batch)
        {
            // Remove by reference identity; batch holds the same object refs.
            for (int i = 0; i < batch.Count; i++)
                _queue.events.Remove(batch[i]);
        }

        // ---- Payload building (manual JSON -> no external serializer needed) ----
        private static string BuildPayload(string clientId, List<QueuedEvent> events)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"client_id\":\"").Append(Escape(clientId)).Append("\",");
            sb.Append("\"non_personalized_ads\":true,");
            sb.Append("\"events\":[");
            for (int e = 0; e < events.Count; e++)
            {
                var ev = events[e];
                if (e > 0) sb.Append(',');
                sb.Append("{\"name\":\"").Append(Escape(ev.name)).Append("\",\"params\":{");
                for (int i = 0; i < ev.keys.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(Escape(ev.keys[i])).Append("\":\"")
                      .Append(Escape(ev.values[i])).Append('"');
                }
                // engagement time keeps sessions valid in GA4 realtime.
                if (ev.keys.Count > 0) sb.Append(',');
                sb.Append("\"engagement_time_msec\":\"100\"");
                sb.Append("}}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:   sb.Append(c);      break;
                }
            }
            return sb.ToString();
        }

        // GA4 event names: letters/digits/underscores, must start with a letter.
        private static string SanitizeName(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? char.ToLowerInvariant(c) : '_');
            string result = sb.ToString();
            if (result.Length == 0 || !char.IsLetter(result[0])) result = "e_" + result;
            return result.Length > 40 ? result.Substring(0, 40) : result;
        }

        // Small reusable dictionary factory (kept explicit for clarity).
        private static Dictionary<string, string> NewParams(int capacity) =>
            new Dictionary<string, string>(capacity);

        // Fallback anon id if no profile is active yet (e.g. profile-select screen).
        private static string SystemAnonId()
        {
            const string k = "analytics/anon_fallback_id";
            var stored = JsonDataStore.Load<AnonId>(k);
            if (stored != null && !string.IsNullOrEmpty(stored.id)) return stored.id;
            var fresh = new AnonId { id = Guid.NewGuid().ToString("N") };
            JsonDataStore.Save(k, fresh);
            return fresh.id;
        }

        // ---- Serializable queue types ------------------------------------------
        [Serializable] private class AnonId { public string id; }

        [Serializable]
        private class EventQueue
        {
            public List<QueuedEvent> events = new List<QueuedEvent>();
        }

        [Serializable]
        private class QueuedEvent
        {
            public string name;
            public string clientId;
            // Parallel key/value lists keep JsonUtility happy (no Dictionary support).
            public List<string> keys;
            public List<string> values;
            public long unixSeconds;
        }
    }
}
