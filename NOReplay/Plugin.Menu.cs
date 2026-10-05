using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NOReplay
{
    public partial class Plugin
    {
        static bool _menuHooked;
        static GameObject? _replayBtn;

        void TickMenuButton()
        {
            if (!MainMenuVisible())
            {
                _menuHooked = false;
                _replayBtn = null;
                return;
            }
            if (_menuHooked && _replayBtn != null && _replayBtn.activeInHierarchy) return;
            TryInjectReplayButton();
        }

        bool _menuMiss;
        bool _menuReady;

        void TryInjectReplayButton()
        {
            Transform? workshop = FindLabelTransform("WORKSHOP") ?? FindLabelTransform("Workshop");
            if (workshop == null)
            {
                _menuReady = false;
                if (!_menuMiss)
                {
                    _menuMiss = true;
                    Log.LogInfo("Replay-Button: Workshop im Menü nicht gefunden.");
                }
                return;
            }
            _menuReady = true;
            var src = workshop;
            while (src.parent != null && src.GetComponent("Button") == null)
                src = src.parent;
            if (src.GetComponent("Button") == null) src = workshop;
            var parent = src.parent;
            if (parent == null) return;
            var existing = parent.Find("ReplayButton");
            if (existing != null)
            {
                _replayBtn = existing.gameObject;
                _replayBtn.SetActive(true);
                SetLabelText(_replayBtn.transform, "REPLAY");
                _menuHooked = true;
                return;
            }
            var clone = UnityEngine.Object.Instantiate(src.gameObject, parent);
            clone.name = "ReplayButton";
            clone.SetActive(true);
            clone.transform.SetSiblingIndex(src.GetSiblingIndex() + 1);
            SetLabelText(clone.transform, "REPLAY");
            HookClick(clone);
            _replayBtn = clone;
            _menuHooked = true;
            _menuMiss = false;
            var scene = SceneManager.GetActiveScene();
            Log.LogInfo("Replay-Button nur in MainMenu. Szene=" + scene.name + " Pfad=" + scene.path);
            DumpGameMaps();
        }

        static Transform? FindLabelTransform(string text)
        {
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                string n = t.name ?? "";
                if (n.IndexOf("Workshop", StringComparison.OrdinalIgnoreCase) >= 0
                    && t.GetComponent("Button") != null)
                    return t;
                string label = ReadLabel(t);
                if (string.Equals(label, text, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            return null;
        }

        static string ReadLabel(Transform t)
        {
            foreach (var c in t.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string tn = c.GetType().Name;
                if (tn != "Text" && tn != "TextMeshProUGUI" && tn != "TMP_Text") continue;
                var p = c.GetType().GetProperty("text");
                if (p != null && p.GetValue(c) is string s) return s.Trim();
            }
            return "";
        }

        static void SetLabelText(Transform root, string text)
        {
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string tn = c.GetType().Name;
                if (tn != "Text" && tn != "TextMeshProUGUI" && tn != "TMP_Text") continue;
                var p = c.GetType().GetProperty("text");
                p?.SetValue(c, text);
            }
        }

        void HookClick(GameObject go)
        {
            var btn = go.GetComponent("Button");
            if (btn == null) return;
            var pOnClick = btn.GetType().GetProperty("onClick");
            if (pOnClick == null) return;
            object? old = pOnClick.GetValue(btn);
            if (old != null)
            {
                old.GetType().GetMethod("RemoveAllListeners")?.Invoke(old, null);
                var persist = old.GetType().GetField("m_PersistentCalls",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                persist?.GetValue(old)?.GetType().GetMethod("Clear")?.Invoke(persist.GetValue(old), null);
            }
            object fresh = Activator.CreateInstance(pOnClick.PropertyType)!;
            pOnClick.SetValue(btn, fresh);
            var add = fresh.GetType().GetMethod("AddListener", new[] { typeof(UnityEngine.Events.UnityAction) });
            add?.Invoke(fresh, new object[] { new UnityEngine.Events.UnityAction(OpenReplayPicker) });
        }

        void OpenReplayPicker()
        {
            ScanReplays();
            _pickerOpen = true;
            _replayLive = false;
            if (_replayFiles.Count > 0) _replayIndex = _replayFiles.Count - 1;
            Log.LogInfo("Replay-Auswahl: " + _replayFiles.Count);
        }

        void DrawReplayPicker()
        {
            float w = 720f, h = 520f;
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;
            GUI.Box(new Rect(x, y, w, h), "");
            GUI.Label(new Rect(x + 16, y + 8, 200, 20), "SELECT REPLAY");
            if (GUI.Button(new Rect(x + w - 90, y + 6, 74, 22), "Back"))
                _pickerOpen = false;
            GUI.Label(new Rect(x + 16, y + 32, 40, 20), "Pfad");
            _folderEdit = GUI.TextField(new Rect(x + 56, y + 30, w - 280, 22), _folderEdit ?? "");
            if (GUI.Button(new Rect(x + w - 216, y + 30, 70, 22), "Scan"))
            {
                if (ReplayFolder != null)
                {
                    ReplayFolder.Value = (_folderEdit ?? "").Trim();
                    Config.Save();
                    SaveFolderBesideDll(ReplayFolder.Value);
                }
                ScanReplays();
            }
            if (GUI.Button(new Rect(x + w - 140, y + 30, 70, 22), "Default"))
            {
                _folderEdit = DefaultReplayFolder();
                if (ReplayFolder != null)
                {
                    ReplayFolder.Value = _folderEdit;
                    Config.Save();
                    SaveFolderBesideDll(ReplayFolder.Value);
                }
                ScanReplays();
            }
            _pickerScroll = GUI.BeginScrollView(
                new Rect(x + 16, y + 58, w - 32, h - 74),
                _pickerScroll,
                new Rect(0, 0, w - 56, Math.Max(_replayFiles.Count * 28f, 40f)));
            for (int i = 0; i < _replayFiles.Count; i++)
            {
                string name = Path.GetFileNameWithoutExtension(_replayFiles[i]);
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - 4);
                var row = new Rect(0, i * 28f, w - 60, 26f);
                if (i == _replayIndex) GUI.Box(row, "");
                if (GUI.Button(row, name))
                {
                    _replayIndex = i;
                    GUI.EndScrollView();
                    StartSelectedReplay();
                    return;
                }
            }
            GUI.EndScrollView();
        }

        void DrawTimeline()
        {
            float w = 980f, h = 58f;
            float x = (Screen.width - w) * 0.5f;
            float y = 12f;
            GUI.Box(new Rect(x, y, w, h), "");
            string title = CurrentDoc?.Title ?? "Replay";
            GUI.Label(new Rect(x + 10, y + 4, w - 20, 18),
                $"{title}   {Clock.Time:0.0}/{Clock.Duration:0.0}s   x{Clock.Speed}   {(Clock.Paused ? "PAUSE" : "PLAY")}");
            float slider = Clock.Duration <= 0f ? 0f : Clock.Time / Clock.Duration;
            float next = GUI.HorizontalSlider(new Rect(x + 10, y + 24, w - 20, 16), slider, 0f, 1f);
            if (Math.Abs(next - slider) > 0.0001f)
            {
                _holdClock = false;
                Clock.Seek(next * Clock.Duration);
            }
            if (GUI.Button(new Rect(x + 10, y + 36, 70, 18), Clock.Paused ? "Play" : "Pause"))
            {
                _holdClock = false;
                Clock.Paused = !Clock.Paused;
            }
            if (GUI.Button(new Rect(x + 88, y + 36, 36, 18), "-4x")) Clock.Speed = -4f;
            if (GUI.Button(new Rect(x + 126, y + 36, 36, 18), "-2x")) Clock.Speed = -2f;
            if (GUI.Button(new Rect(x + 164, y + 36, 36, 18), "-1x")) Clock.Speed = -1f;
            if (GUI.Button(new Rect(x + 202, y + 36, 48, 18), "-0.5x")) Clock.Speed = -0.5f;
            if (GUI.Button(new Rect(x + 252, y + 36, 52, 18), "-0.25x")) Clock.Speed = -0.25f;
            if (GUI.Button(new Rect(x + 308, y + 36, 48, 18), "0.25x")) Clock.Speed = 0.25f;
            if (GUI.Button(new Rect(x + 358, y + 36, 44, 18), "0.5x")) Clock.Speed = 0.5f;
            if (GUI.Button(new Rect(x + 406, y + 36, 36, 18), "1x")) Clock.Speed = 1f;
            if (GUI.Button(new Rect(x + 444, y + 36, 36, 18), "2x")) Clock.Speed = 2f;
            if (GUI.Button(new Rect(x + 482, y + 36, 36, 18), "4x")) Clock.Speed = 4f;
        }

        void StartSelectedReplay()
        {
            if (_replayFiles.Count == 0) return;
            try
            {
                LoadReplayAt(_replayIndex);
                if (CurrentDoc == null) return;
                string? jsonPath = ExportMissionJson(CurrentDoc);
                if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
                {
                    Log.LogError("JSON wurde nicht geschrieben, Abbruch.");
                    return;
                }
                Log.LogInfo("JSON bereit (" + new FileInfo(jsonPath).Length + " Byte).");
                _pickerOpen = false;
                _replayLive = true;
                _seenWorld = false;
                Clock.Seek(0f);
                Clock.Paused = true;
                _holdClock = true;
                ApplyNamedUserMission("NOReplay");
                _pendingOfficialStart = true;
                OpenMissionsMenu();
                Log.LogInfo("Missionsmenü — warte auf SinglePlayerMenu.");
            }
            catch (Exception ex)
            {
                Log.LogError("Start Replay: " + ex);
            }
        }

        void OpenMissionsMenu()
        {
            var ml = FindGameType("MapLoader");
            string scene = "MissionsMenu";
            if (ml != null)
            {
                var f = ml.GetField("MissionsMenu", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (f?.GetValue(null) is string s && !string.IsNullOrEmpty(s))
                    scene = s;
            }
            Log.LogInfo("Lade Szene " + scene);
            _worldReadyAt = Time.unscaledTime + 1.5f;
            SceneManager.LoadScene(scene);
        }

        void TickOfficialStart()
        {
            if (!_pendingOfficialStart) return;
            if (Time.unscaledTime < _worldReadyAt) return;
            var pickerType = FindGameType("MissionsPicker");
            if (pickerType == null) return;
            Behaviour? picker = null;
            foreach (var o in Resources.FindObjectsOfTypeAll(pickerType))
            {
                if (o is Behaviour b && b.isActiveAndEnabled) { picker = b; break; }
            }
            if (picker == null) return;
            var mmType = FindGameType("MissionManager");
            object? mission = mmType?.GetProperty("CurrentMission",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            if (mission == null)
            {
                Log.LogError("CurrentMission leer, offizieller Start abgebrochen.");
                _pendingOfficialStart = false;
                return;
            }
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            pickerType.GetField("mission", flags)?.SetValue(picker, mission);
            var confirm = pickerType.GetMethod("ConfirmPressed", flags);
            if (confirm == null)
            {
                Log.LogError("MissionsPicker.ConfirmPressed fehlt.");
                _pendingOfficialStart = false;
                return;
            }
            _pendingOfficialStart = false;
            Log.LogInfo("Replay bestätigt wie Klick auf Start.");
            confirm.Invoke(picker, null);
        }
    }
}