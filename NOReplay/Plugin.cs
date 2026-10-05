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

        internal static ReplayLog Log = new ReplayLog();
        internal static Harmony Harmony = null!;
        internal static ReplayDocument? CurrentDoc;
        internal static ReplayClock Clock = new ReplayClock();

        float _logAcc;
        List<string> _replayFiles = new List<string>();
        int _replayIndex;
        bool _pickerOpen;
        bool _replayLive;
        bool _seenWorld;
        bool _pendingOfficialStart;
        bool _timelineHidden;
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

        static GameObject? _ghost;
        static readonly List<GameObject> _ghosts = new List<GameObject>();
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
                Harmony = new Harmony(PluginGuid);
                try { InstallListenPatches(); }
                catch (Exception ex) { Log.LogError("Listen-Patches: " + ex); }
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
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            Harmony?.UnpatchSelf();
        }

        void OnSceneUnloaded(Scene scene)
        {
            if (!string.Equals(scene.name, "GameWorld", StringComparison.OrdinalIgnoreCase)) return;
            if (CurrentDoc == null && !_replayLive) return;
            EndReplay("GameWorld entladen");
        }

        void EndReplay(string why)
        {
            _replayLive = false;
            _seenWorld = false;
            _timelineHidden = false;
            _ordnanceWarmed = false;
            _warmLeft.Clear();
            _warmSeen.Clear();
            _aircraftQueued = false;
            _holdClock = false;
            _pickerOpen = false;
            CurrentDoc = null;
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
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    _holdClock = false;
                    Clock.Paused = !Clock.Paused;
                }
                if (Input.GetKeyDown(KeyCode.Alpha1)) Clock.Speed = 1f;
                if (Input.GetKeyDown(KeyCode.Alpha2)) Clock.Speed = 2f;
                if (Input.GetKeyDown(KeyCode.Alpha4)) Clock.Speed = 4f;
                if (Input.GetKeyDown(KeyCode.LeftArrow)) Clock.Seek(Clock.Time - 5f);
                if (Input.GetKeyDown(KeyCode.RightArrow)) Clock.Seek(Clock.Time + 5f);
                if (Input.GetKeyDown(KeyCode.F10)) _timelineHidden = !_timelineHidden;
                if (_holdClock)
                {
                    Clock.Paused = true;
                    if (Clock.Time > 0.001f) Clock.Seek(0f);
                }
                Clock.Tick(Time.unscaledDeltaTime);
                TickGhostSpawns();
                TickGhostSleep();
                WarmOrdnance();
                ShellReplay.Tick(CurrentDoc, Clock.Time);
                TickPanel();
            }
            TickMenuButton();
            TickLeaveReplay();
            TickOfficialStart();
            if (Input.GetKeyDown(KeyCode.F8))
            {
                Log.LogInfo("F8 gedrückt.");
                Record.Toggle();
            }
        }

        float _panelAt;

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

        static void TickGhostSleep()
        {
            for (int i = 0; i < _ghosts.Count; i++)
            {
                var go = _ghosts[i];
                if (go == null) continue;
                var rg = go.GetComponent<ReplayGhost>();
                if (rg == null) continue;
                bool on = rg.IsTrackAlive();
                if (go.activeSelf == on) continue;
                if (!on)
                {
                    rg.ForceWreck();
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

        private void OnGUI()
        {
            if (Record.Active)
            {
                var rec = new GUIStyle(GUI.skin.label) { fontSize = 22 };
                rec.normal.textColor = Color.green;
                GUI.Label(new Rect(Screen.width - 78f, Screen.height - 42f, 60f, 28f), "Rec", rec);
            }
            if (_pickerOpen) DrawReplayPicker();
            else if (InGameWorld() && _replayLive && CurrentDoc != null && !_timelineHidden) DrawTimeline();
        }
    }

    internal sealed class ReplayLog
    {
        StreamWriter? _w;
        public void Open(string path)
        {
            try { _w?.Dispose(); } catch { }
            _w = new StreamWriter(path, false) { AutoFlush = true };
        }
        public void LogInfo(object d) => Write("Info", d);
        public void LogWarning(object d) => Write("Warning", d);
        public void LogError(object d) => Write("Error", d);
        void Write(string lvl, object d)
        {
            try { _w?.WriteLine("[" + lvl + "] " + d); }
            catch { }
        }
    }
}