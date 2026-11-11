using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Input = UnityEngine.Input;

namespace NOReplay
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("NuclearOption.exe")]
    public partial class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "you.noreplay";
        public const string PluginName = "NO Replay";
        public const string PluginVersion = "0.1.1";

        /// <summary>Hearing range for missile motor and flight audio. Farther than this, a Play() is dropped.</summary>
        internal const float OrdMaxAudioDist = 900f;

        internal static ReplayLog Log = new ReplayLog();
        internal static Harmony Harmony = null!;
        internal static ReplayDocument? CurrentDoc;
        static bool _staticHandlerStarted;
        internal static ReplayClock Clock = new ReplayClock();

        float _logAcc;
        List<string> _replayFiles = new List<string>();
        int _replayIndex;
        bool _pickerOpen;
        bool _replayLive;
        bool _seenWorld;
        bool _pendingOfficialStart;
        bool _timelineHidden;
        internal static bool TrailsOn;
        internal static bool ShowKillFeed = true;
        internal static bool ShowChat = true;
        internal static bool ShowSystem = true;
        internal static bool PatternOn;
        internal static bool PerfLog;
        internal static float TrailSeconds = 60f;
        static bool _holdClock;
        Vector2 _pickerScroll;
        internal static ConfigEntry<string>? ReplayFolder;
        string _folderEdit = "";

        static string DllDir()
        {
            try
            {
                string loc = typeof(Plugin).Assembly.Location;
                if (!string.IsNullOrEmpty(loc))
                {
                    string? d = Path.GetDirectoryName(loc);
                    if (!string.IsNullOrEmpty(d)) return d;
                }
            }
            catch { }
            try { return Paths.PluginPath; }
            catch { return "."; }
        }

        static string FolderSavePath() => Path.Combine(DllDir(), "replay_folder.txt");

        static string DefaultReplayFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low",
                "Shockfront", "NuclearOption", "Replays");
        }

        static void SaveFolderBesideDll(string folder)
        {
            try { File.WriteAllText(FolderSavePath(), folder ?? ""); }
            catch { }
        }

        static string LoadFolderBesideDll(string fallback)
        {
            try
            {
                string p = FolderSavePath();
                if (File.Exists(p))
                {
                    string t = File.ReadAllText(p).Trim();
                    if (t.Length > 0) return t;
                }
            }
            catch { }
            return fallback;
        }

        internal static GameObject? _ghost;
        static readonly List<GameObject> _ghosts = new List<GameObject>();
        static readonly List<ReplayGhost> _ghostComps = new List<ReplayGhost>();
        internal static List<ReplayGhost> GhostComps => _ghostComps;

        static void AddGhost(GameObject go)
        {
            _ghosts.Add(go);
            _ghostComps.Add(go != null ? go.GetComponent<ReplayGhost>() : null);
        }
        static readonly List<ReplayObject> _pendingAir = new List<ReplayObject>();
        static readonly Dictionary<string, UnityEngine.Object?> _defCache = new Dictionary<string, UnityEngine.Object?>();
        static int _followIndex = -1;
        static bool _follow;

        private void Awake()
        {
            try { Logger.LogInfo("NOReplay Awake"); } catch { }
            try { Log.Open(Path.Combine(DllDir(), "NOReplay.log")); } catch { }
            Log.LogInfo("BUILD 0.1.1 Awake");
            try
            {
                string def = DefaultReplayFolder();
                ReplayFolder = Config.Bind(
                    "Paths",
                    "ReplayFolder",
                    def,
                    "Ordner mit .acmi Replay-Dateien");
                string saved = LoadFolderBesideDll(ReplayFolder.Value);
                if (!string.IsNullOrWhiteSpace(saved) && saved != ReplayFolder.Value)
                    ReplayFolder.Value = saved;
                _folderEdit = ReplayFolder.Value;
                try
                {
                    PerfLog = Config.Bind(
                        "Debug",
                        "PerfLog",
                        false,
                        "Alle 5 Sekunden die mittlere Dauer des Replay-Updates in NOReplay.log schreiben.").Value;
                }
                catch { }
                Harmony = new Harmony(PluginGuid);
                try { InstallListenPatches(); }
                catch (Exception ex) { Log.LogError("Listen-Patches: " + ex); }
                try { Harmony.PatchAll(typeof(CameraTVPatch).Assembly); }
                catch (Exception ex) { Log.LogError("TV-Patch: " + ex); }
                try { Record.Init(Config); }
                catch (Exception ex) { Log.LogError("Record.Init: " + ex); }
                Log.LogInfo($"{PluginName} {PluginVersion} geladen.");
                SceneManager.sceneUnloaded += OnSceneUnloaded;
            }
            catch (Exception ex)
            {
                Log.LogError("Awake: " + ex);
                try { Logger.LogError("NOReplay Awake: " + ex); } catch { }
            }
        }

        private void OnDestroy()
        {
            ClearReplaySelection(false);
            RestoreReplayKeys();
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            Harmony?.UnpatchSelf();
            Log.Close();
        }

        void OnSceneUnloaded(Scene scene)
        {
            if (!string.Equals(scene.name, "GameWorld", StringComparison.OrdinalIgnoreCase)) return;
            Record.Stop("left mission");
            if (CurrentDoc == null && !_replayLive) return;
            EndReplay("GameWorld entladen");
        }

        void EndReplay(string why)
        {
            ClearReplaySelection(false);
            RestoreReplayKeys();
            _replayLive = false;
            _seenWorld = false;
            _timelineHidden = false;
            _ordnanceWarmed = false;
            _warmLeft.Clear();
            _warmSeen.Clear();
            _aircraftQueued = false;
            _holdClock = false;
            _pickerOpen = false;
            ReplayRounds.Clear();
            BoomAudio.Clear();
            CurrentDoc = null;
            FeedPlayback.Reset();
            _staticHandlerStarted = false;
            Clock.Reset(0f);
            _menuHooked = false;
            _replayBtn = null;
            StateTextId = 0;
            Log.LogInfo("Replay beendet, Zeitleiste aus. " + why);
        }

        private void Update()
        {
            TickWorldStart();
            TickBindGhosts();
            if (CurrentDoc != null)
            {
                long perfStamp = ReplayPerf.Stamp();
                if (HotkeyDown(RecordConfig.PlayPauseKey, KeyCode.Space))
                {
                    _holdClock = false;
                    Clock.Paused = !Clock.Paused;
                }
                if (HotkeyDown(RecordConfig.Speed1Key, KeyCode.Alpha1)) Clock.Speed = 1f;
                if (HotkeyDown(RecordConfig.Speed2Key, KeyCode.Alpha2)) Clock.Speed = 2f;
                if (HotkeyDown(RecordConfig.Speed4Key, KeyCode.Alpha4)) Clock.Speed = 4f;
                if (HotkeyDown(RecordConfig.SeekBackKey, KeyCode.LeftArrow)) Clock.Seek(Clock.Time - 5f);
                if (HotkeyDown(RecordConfig.SeekForwardKey, KeyCode.RightArrow)) Clock.Seek(Clock.Time + 5f);
                if (HotkeyDown(RecordConfig.ToggleTimelineKey, KeyCode.F10)) _timelineHidden = !_timelineHidden;
                EnsureReplayKeysMuted();
                if (!ReplayTextFocused())
                {
                    if (HotkeyDown(RecordConfig.ToggleTrailsKey, KeyCode.T)) ToggleTrails();
                    if (HotkeyDown(RecordConfig.LookAtLockKey, KeyCode.F)) ToggleLookLock();
                    if (HotkeyDown(RecordConfig.CasePatternKey, KeyCode.P)) TogglePattern();
                }
                if (_holdClock)
                {
                    Clock.Paused = true;
                    if (Clock.Time > 0.001f) Clock.Seek(0f);
                }
                Clock.Tick(Time.unscaledDeltaTime);
                if (Clock.WentBack)
                {
                    Clock.WentBack = false;
                    ResetParticles();
                }
                long section = ReplayPerf.Stamp();
                TickGhostSpawns();
                KeepReplayCamera();
                TickGhostSleep();
                ApplyOrdnanceSeeks();
                ReplayPerf.Add("spawn", ReplayPerf.Stamp() - section);
                if (!_staticHandlerStarted && InGameWorld() && Time.timeSinceLevelLoad > 2f)
                {
                    _staticHandlerStarted = true;
                    ReplayStaticHandler.StartFor(CurrentDoc);
                }
                section = ReplayPerf.Stamp();
                TickMissileBooms();
                BoomAudio.Tick(Clock.Time, Clock.Paused, Clock.Speed, Clock.SeekSerial);
                TickReplaySelection();
                ReplayPerf.Add("booms", ReplayPerf.Stamp() - section);
                section = ReplayPerf.Stamp();
                TickTrails();
                ReplayPerf.Add("trails", ReplayPerf.Stamp() - section);
                WarmOrdnance();
                section = ReplayPerf.Stamp();
                ShellReplay.Tick(CurrentDoc, Clock.Time);
                ReplayRounds.Tick(Clock.Time, Clock.Paused);
                ReplayPerf.Add("shells", ReplayPerf.Stamp() - section);
                section = ReplayPerf.Stamp();
                FeedPlayback.Tick(CurrentDoc, Clock.Time, Clock.Paused);
                ReplayPerf.Add("feed", ReplayPerf.Stamp() - section);
                TickPanel();
                ReplayPerf.Add("plugin", ReplayPerf.Stamp() - perfStamp);
            }
            TickMenuButton();
            TickLeaveReplay();
            TickOfficialStart();
            if (HotkeyDown(RecordConfig.StartStopRecordingKey, KeyCode.F8))
            {
                Log.LogInfo("Aufnahmetaste gedrückt.");
                Record.Toggle();
            }
        }

        void LateUpdate()
        {
            ReplayPerf.Frame();
        }

        internal static void ToggleTrails()
        {
            TrailsOn = !TrailsOn;
        }

        internal static void TogglePattern()
        {
            PatternOn = !PatternOn;
        }

        static FieldInfo? _insideInput;
        static Func<bool>? _insideInputGet;
        static bool _insideInputLooked;

        // IsDown() is the configured key and its modifiers, but it stays quiet
        // while any other key is held. The old GetKeyDown checks did not, so a
        // trail toggle still works during WASD. A missing entry keeps the old key.
        internal static bool HotkeyDown(ConfigEntry<KeyboardShortcut>? entry, KeyCode fallback)
        {
            if (entry == null) return Input.GetKeyDown(fallback);
            try
            {
                KeyboardShortcut key = entry.Value;
                if (key.IsDown()) return true;
                if (key.MainKey == KeyCode.None) return false;
                if (!Input.GetKeyDown(key.MainKey)) return false;
                foreach (KeyCode mod in key.Modifiers)
                {
                    if (mod == KeyCode.None) continue;
                    if (!Input.GetKey(mod)) return false;
                }
                return true;
            }
            catch { return false; }
        }

        internal static bool ReplayTextFocused()
        {
            if (GUIUtility.keyboardControl != 0) return true;
            if (!_insideInputLooked)
            {
                _insideInputLooked = true;
                var checker = FindGameType("InputFieldChecker");
                _insideInput = checker?.GetField("InsideInputField", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (_insideInput != null) _insideInputGet = FieldFast.StaticBoolGetter(_insideInput);
            }
            try
            {
                if (_insideInputGet != null) return _insideInputGet();
                return _insideInput?.GetValue(null) is bool on && on;
            }
            catch { return false; }
        }

        float _panelAt;

        int _ordSeekSerial;

        static readonly List<ReplayGhost> _ordRespawn = new List<ReplayGhost>();

        internal static void QueueOrdnanceRespawn(ReplayGhost rg)
        {
            if (rg != null && !_ordRespawn.Contains(rg))
                _ordRespawn.Add(rg);
        }

        static void FlushOrdnanceRespawn()
        {
            if (_ordRespawn.Count == 0) return;
            for (int i = 0; i < _ordRespawn.Count; i++)
            {
                var rg = _ordRespawn[i];
                if (rg == null) continue;
                var track = rg.Track;
                var go = rg.gameObject;
                ReleaseCameraIfOn(go);
                _ghosts.Remove(go);
                _ghostComps.Remove(rg);
                if (track != null && !_pendingAir.Contains(track))
                    _pendingAir.Add(track);
                if (go != null)
                    UnityEngine.Object.Destroy(go);
            }
            _ordRespawn.Clear();
        }

        void ApplyOrdnanceSeeks()
        {
            if (Clock.SeekSerial == _ordSeekSerial) return;
            _ordSeekSerial = Clock.SeekSerial;
            float t = Clock.Time;
            for (int i = 0; i < _ghostComps.Count; i++)
            {
                var rg = _ghostComps[i];
                if (rg == null || !rg.IsOrdnancePublic) continue;
                rg.OnReplaySeek(t);
            }
            FlushOrdnanceRespawn();
        }

        void TickMissileBooms()
        {
            float t = Clock.Time;
            for (int i = 0; i < _ghostComps.Count; i++)
            {
                var rg = _ghostComps[i];
                if (rg == null || !rg.IsOrdnancePublic) continue;
                rg.TryBoom(t);
            }
        }

        static void ClearAllTrails()
        {
            for (int i = 0; i < _ghostComps.Count; i++)
            {
                var rg = _ghostComps[i];
                if (rg == null) continue;
                rg.ForceHideTrail();
            }
        }

        void TickTrails()
        {
            float t = Clock.Time;
            float len = TrailsOn ? TrailSeconds : 0f;
            for (int i = 0; i < _ghostComps.Count; i++)
            {
                var rg = _ghostComps[i];
                if (rg == null) continue;
                rg.UpdateTrail(t, len);
            }
        }

        void TickPanel()
        {
            if (Time.unscaledTime - _panelAt < 0.4f) return;
            _panelAt = Time.unscaledTime;
            var camType = FindGameType("CameraStateManager");
            if (camType == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fFollow = camType.GetField("followingUnit", flags);
            if (fFollow == null) return;
            foreach (var cam in Resources.FindObjectsOfTypeAll(camType))
            {
                var unit = fFollow.GetValue(cam) as Component;
                if (unit == null) continue;
                var go = unit.gameObject;
                var ghost = go.GetComponent<ReplayGhost>() ?? go.GetComponentInParent<ReplayGhost>();
                if (ghost == null) continue;
                string? player = ghost.Track?.Pilot;
                if (string.IsNullOrEmpty(player)) player = PlayerNameFromCallSign(ghost.Track?.CallSign);
                ApplyPanel(ghost.gameObject, ghost.Track, string.IsNullOrEmpty(player) ? ghost.Track?.Name : player);
                break;
            }
        }

        static void ResetParticles()
        {
            var pt = Type.GetType("UnityEngine.ParticleSystem, UnityEngine.ParticleSystemModule")
                     ?? FindGameType("ParticleSystem");
            var clear = pt?.GetMethod("Clear", new[] { typeof(bool) });
            var stop = pt?.GetMethod("Stop", Type.EmptyTypes);
            for (int i = 0; i < _ghostComps.Count; i++)
            {
                var rg = _ghostComps[i];
                if (rg == null || pt == null) continue;
                var go = rg.gameObject;
                if (go == null) continue;
                foreach (var ps in go.GetComponentsInChildren(pt, true))
                {
                    if (ps == null) continue;
                    try { stop?.Invoke(ps, null); } catch { }
                    try { clear?.Invoke(ps, new object[] { true }); } catch { }
                }
                rg?.ResetFx();
            }
        }

        static void TickGhostSleep()
        {
            // Only check a quarter of the ghosts each frame.
            if (_ghostComps.Count == 0) return;
            int stride = 4;
            int start = Time.frameCount % stride;
            for (int i = start; i < _ghostComps.Count; i += stride)
            {
                var rg = _ghostComps[i];
                if (rg == null) continue;
                var go = rg.gameObject;
                if (go == null) continue;
                bool on = rg.KeepsGhostActive();
                if (go.activeSelf == on) continue;
                if (!on)
                {
                    rg.OrphanLiveRounds();
                    rg.ForceWreck();
                    rg.NoteOrdnanceAsleep();
                    ReleaseCameraIfOn(go);
                }
                go.SetActive(on);
                if (!on) SetMapIcon(go, false);
            }
        }

        static bool InGameWorld()
        {
            var scene = SceneManager.GetActiveScene();
            return string.Equals(scene.name, "GameWorld", StringComparison.OrdinalIgnoreCase)
                || (scene.path ?? "").IndexOf("GameWorld", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        string _sceneLogged = "";

        static bool MainMenuVisible()
        {
            var scene = SceneManager.GetActiveScene();
            string n = scene.name ?? "";
            string p = scene.path ?? "";
            return string.Equals(n, "MainMenu", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith("MainMenu/MainMenu.unity", StringComparison.OrdinalIgnoreCase);
        }

        void TickLeaveReplay()
        {
            var scene = SceneManager.GetActiveScene();
            string id = (scene.name ?? "") + "|" + (scene.path ?? "");
            if (id != _sceneLogged)
            {
                _sceneLogged = id;
                ClearHqCache();
                Log.LogInfo("Szene gewechselt: name=" + scene.name + " path=" + scene.path);
            }
            if (!InGameWorld())
            {
                _datum = null;
                _datumLogged = false;
                if (_pendingOfficialStart) return;
                if (!_seenWorld) return;
                if (!MainMenuVisible()) return;
                if (CurrentDoc == null && !_replayLive) return;
                EndReplay("Szene=" + scene.name);
                return;
            }
            if (_replayLive || CurrentDoc != null) _seenWorld = true;
            EnsureDatum();
        }

        void ScanReplays()
        {
            string folder = ReplayFolder != null && !string.IsNullOrWhiteSpace(ReplayFolder.Value)
                ? ReplayFolder.Value.Trim()
                : DefaultReplayFolder();
            _replayFiles.Clear();
            if (Directory.Exists(folder))
            {
                _replayFiles.AddRange(Directory.GetFiles(folder, "*.acmi"));
                _replayFiles.AddRange(Directory.GetFiles(folder, "*.zip.acmi"));
            }
            var unique = new List<string>();
            foreach (var f in _replayFiles)
                if (!unique.Contains(f)) unique.Add(f);
            _replayFiles = unique;
            _replayFiles.Sort();
            Log.LogInfo($"Replay-Dateien gefunden: {_replayFiles.Count} in {folder}");
            for (int i = 0; i < _replayFiles.Count; i++)
                Log.LogInfo($"  [{i}] {_replayFiles[i]}");
        }

        static void ApplyWeather(ReplayDocument doc)
        {
            if (doc.TimeOfDay < 0f) return;
            try
            {
                var li = UnityEngine.Object.FindObjectOfType<LevelInfo>();
                if (li == null) return;
                li.SetTimeOfDay(doc.TimeOfDay);
                if (doc.Conditions >= 0f) li.conditions = doc.Conditions;
                if (doc.CloudHeight >= 0f) li.cloudHeight = doc.CloudHeight;
                if (doc.WindSpeed >= 0f) li.windSpeed = doc.WindSpeed;
                if (doc.WindTurbulence >= 0f) li.windTurbulence = doc.WindTurbulence;
                if (doc.MoonPhase >= 0f) li.moonPhase = doc.MoonPhase;
                Log.LogInfo("Weather: TOD=" + doc.TimeOfDay.ToString("0.0") + " cond=" + doc.Conditions.ToString("0.0") + " cloud=" + doc.CloudHeight.ToString("0"));
            }
            catch (Exception ex) { Log.LogError("Weather: " + ex); }
        }

        void LoadReplayAt(int index)
        {
            if (_replayFiles.Count == 0) return;
            if (index < 0) index = 0;
            if (index >= _replayFiles.Count) index = _replayFiles.Count - 1;
            _replayIndex = index;
            string path = _replayFiles[index];
            try
            {
                var doc = AcmiParser.Load(path);
                CurrentDoc = doc;
                ResetMapIconLog();
                BoomAudio.Clear();
                _replayCam = null;
                _camFreed = false;
                Clock.Reset(doc.Duration);
                Clock.Seek(0f);
                Clock.Paused = true;
                _holdClock = true;
                Log.LogInfo($"Geladen [{index + 1}/{_replayFiles.Count}]: {Path.GetFileName(path)}");
                int players = 0;
                foreach (var o in doc.Objects)
                    if (!string.IsNullOrEmpty(PlayerNameFromCallSign(o.CallSign))) players++;
                Log.LogInfo($"Title={doc.Title}  MapId={doc.MapId}  Dauer={doc.Duration:0.0}s  Objekte={doc.Objects.Count}  Spieler={players}");
                float minBorn = 9999f;
                int withPose = 0;
                foreach (var o in doc.Objects)
                {
                    if (o.Samples.Count == 0) continue;
                    withPose++;
                    float b = o.SpawnAt >= 0f ? o.SpawnAt : o.Samples[0].Time;
                    if (b < minBorn) minBorn = b;
                }
                Log.LogInfo("ACMI SpawnAt min=" + minBorn.ToString("0.000") + " mitPose=" + withPose);
                int withKit = 0;
                foreach (var o in doc.Objects)
                    if (!string.IsNullOrEmpty(o.Loadout) || !string.IsNullOrEmpty(o.Livery)) withKit++;
                Log.LogInfo("ACMI Loadout/Livery an " + withKit + " Einheiten.");
            }
            catch (Exception ex)
            {
                Log.LogError("Parse fehlgeschlagen: " + ex);
            }
        }

        GUIStyle? _recStyle;

        private void OnGUI()
        {
            if (Record.Active)
            {
                if (_recStyle == null)
                {
                    _recStyle = new GUIStyle(GUI.skin.label) { fontSize = 22 };
                    _recStyle.normal.textColor = Color.green;
                }
                GUI.Label(new Rect(Screen.width - 78f, Screen.height - 42f, 60f, 28f), "Rec", _recStyle);
            }
            if (_pickerOpen) DrawReplayPicker();
            else if (InGameWorld() && _replayLive && CurrentDoc != null && !_timelineHidden) DrawTimeline();
        }
    }

    internal sealed class ReplayLog
    {
        StreamWriter? _w;
        float _nextFlush;
        public void Open(string path)
        {
            try { _w?.Flush(); } catch { }
            try { _w?.Dispose(); } catch { }
            _w = new StreamWriter(path, false) { AutoFlush = false };
            _nextFlush = Time.unscaledTime + 3f;
        }
        public void Close()
        {
            try { _w?.Flush(); } catch { }
            try { _w?.Dispose(); } catch { }
            _w = null;
        }
        public void LogInfo(object d) => Write("Info", d);
        public void LogWarning(object d) => Write("Warning", d);
        public void LogError(object d) => Write("Error", d);
        void Write(string lvl, object d)
        {
            try
            {
                if (_w == null) return;
                _w.WriteLine("[" + lvl + "] " + d);
                if (Time.unscaledTime >= _nextFlush)
                {
                    _w.Flush();
                    _nextFlush = Time.unscaledTime + 3f;
                }
            }
            catch { }
        }
    }
}