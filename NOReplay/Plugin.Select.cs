using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace NOReplay
{
    public partial class Plugin
    {
        static readonly BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly BindingFlags Stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static ReplayGhost? _selected;
        internal static ReplayGhost? SelectedGhost => _selected;
        static bool _ownOrbit;
        static bool _lookLock;
        static float _orbYaw, _orbPitch, _orbDist = 40f;
        static Vector3 _clickCamPos;
        static bool _clickCamPosOk;
        static string? _selErr;
        static Vector3 _prevMouse;
        static bool _prevMouseOk;
        static object? _inputPlayer;
        static MethodInfo? _getAxis;
        static FieldInfo? _sensField;
        static FieldInfo? _invertField;
        static FieldInfo? _panField;
        static FieldInfo? _tiltField;
        static readonly List<object> _mutedKeys = new List<object>();
        static bool _keysMuted;
        static int _muteTries;

        static void NoteClickCamera()
        {
            _clickCamPosOk = false;
            try
            {
                var cam = Cam();
                if (cam == null) return;
                _clickCamPos = cam.transform.position;
                _clickCamPosOk = true;
            }
            catch { _clickCamPosOk = false; }
        }

        static void TrySelectFromMapClick(object icon)
        {
            if (CurrentDoc == null || icon == null) return;
            if (icon is not Component c) return;
            object? unit = null;
            try
            {
                unit = c.GetType().GetProperty("unit", Inst)?.GetValue(c);
            }
            catch { return; }
            if (unit is not Component uc) return;
            if (!TryPick(uc, out var rg)) return;
            if (!IconSelected(icon))
            {
                if (_selected == rg) ClearReplaySelection(false);
                return;
            }
            if (_lookLock)
            {
                _lookLock = false;
                Log.LogInfo("lock off");
            }
            _selected = rg;
            if (GameIsOrbiting(rg))
            {
                _ownOrbit = false;
                Log.LogInfo("select " + LabelOf(rg));
                return;
            }
            bool moved = false;
            var cam = Cam();
            if (_clickCamPosOk && cam != null)
                moved = (cam.transform.position - _clickCamPos).sqrMagnitude > 0.25f;
            if (!moved)
            {
                LeaveFollowKeepPose();
                JumpNear(rg);
            }
            else
                LeaveFollowKeepPose();
            _ownOrbit = true;
            SeedOwnOrbit(rg);
            Log.LogInfo("select " + LabelOf(rg) + " fallback-orbit");
        }

        static void TickReplaySelection()
        {
            if (CurrentDoc == null) return;
            try
            {
                if (_selected == null)
                {
                    if (_ownOrbit || _lookLock) ClearReplaySelection(true);
                    return;
                }
                if (!Alive(_selected))
                {
                    var go = _selected.gameObject;
                    if (go != null) ReleaseFollowedCamera(go);
                    else ClearReplaySelection(true);
                    return;
                }
                if (_lookLock && GameIsOrbiting(_selected))
                    LeaveFollowKeepPose();
                if (_ownOrbit && !ReplayTextFocused() && FlyMoveHeld())
                    ExitOwnOrbit();
            }
            catch (Exception ex)
            {
                if (_selErr != ex.Message)
                {
                    _selErr = ex.Message;
                    Log.LogWarning("Auswahl: " + ex.Message);
                }
                _ownOrbit = false;
            }
        }

        static void ExitOwnOrbit()
        {
            _ownOrbit = false;
            var cam = Cam();
            if (cam != null) SyncFreeAngles(cam, cam.transform.rotation);
        }

        static void ToggleLookLock()
        {
            if (!Alive(_selected) || _selected == null) return;
            if (_lookLock)
            {
                _lookLock = false;
                var cam = Cam();
                if (cam != null) SyncFreeAngles(cam, cam.transform.rotation);
                Log.LogInfo("lock off");
                return;
            }
            _lookLock = true;
            _ownOrbit = false;
            LeaveFollowKeepPose();
            Log.LogInfo("lock on " + LabelOf(_selected));
        }

        static void ClearReplaySelection(bool lost)
        {
            bool had = _selected != null || _ownOrbit || _lookLock;
            bool wasLock = _lookLock;
            string label = "";
            if (_selected != null)
            {
                try { label = LabelOf(_selected); }
                catch { label = ""; }
            }
            if (wasLock)
            {
                try
                {
                    var cam = Cam();
                    if (cam != null) SyncFreeAngles(cam, cam.transform.rotation);
                }
                catch { }
            }
            _selected = null;
            _ownOrbit = false;
            _lookLock = false;
            if (wasLock) Log.LogInfo("lock off");
            if (!lost || !had) return;
            Log.LogInfo(string.IsNullOrEmpty(label) ? "target lost" : "target lost " + label);
        }

        static void ReleaseFollowedCamera(GameObject go)
        {
            if (go == null) return;
            bool ours = SelectionMatches(go);
            var camType = FindGameType("CameraStateManager");
            if (camType == null)
            {
                if (ours) ClearReplaySelection(true);
                return;
            }
            var follow = camType.GetField("followingUnit", Inst);
            var set = camType.GetMethod("SetFollowingUnit", Inst);
            if (follow == null || set == null)
            {
                if (ours) ClearReplaySelection(true);
                return;
            }
            try
            {
                foreach (var cam in Resources.FindObjectsOfTypeAll(camType))
                {
                    if (cam is not Component cc) continue;
                    var u = follow.GetValue(cam) as Component;
                    if (u == null || u.transform == null) continue;
                    var ut = u.transform;
                    if (u.gameObject != go && !ut.IsChildOf(go.transform) && !go.transform.IsChildOf(ut))
                        continue;
                    if (ours) KeepPose(cc, () => set.Invoke(cam, new object[] { null }));
                    else
                    {
                        try { set.Invoke(cam, new object[] { null }); }
                        catch (Exception ex) { Log.LogWarning("Kamera lösen: " + ex.Message); }
                    }
                    break;
                }
            }
            catch (Exception ex) { Log.LogWarning("Kamera lösen: " + ex.Message); }
            if (ours)
            {
                try
                {
                    var cam = Cam();
                    if (cam != null && cam.transform != null && go.transform != null
                        && cam.transform.IsChildOf(go.transform))
                        LeaveFollowKeepPose();
                }
                catch (Exception ex) { Log.LogWarning("Kamera lösen: " + ex.Message); }
                ClearReplaySelection(true);
            }
        }

        static void LeaveFollowKeepPose()
        {
            var cam = Cam();
            if (cam == null) return;
            var free = cam.GetType().GetField("freeState", Inst)?.GetValue(cam);
            var cur = cam.GetType().GetProperty("currentState", Inst)?.GetValue(cam);
            var followed = cam.GetType().GetField("followingUnit", Inst)?.GetValue(cam);
            bool inFree = free != null && ReferenceEquals(cur, free);
            if (inFree && followed == null)
            {
                SyncFreeAngles(cam, cam.transform.rotation);
                return;
            }
            var set = cam.GetType().GetMethod("SetFollowingUnit", Inst);
            KeepPose(cam, () => set?.Invoke(cam, new object[] { null }));
        }

        // SetFollowingUnit(null) drops the parent and may turn the camera toward the old unit.
        // Put the pose back so a lost target does not move the view.
        static void KeepPose(Component cam, Action change)
        {
            var tr = cam.transform;
            Vector3 p = tr.position;
            Quaternion r = tr.rotation;
            try { change(); }
            catch (Exception ex) { Log.LogWarning("Kamera: " + ex.Message); }
            try
            {
                if (cam == null) return;
                tr.SetParent(null, true);
                tr.SetPositionAndRotation(p, r);
                ZeroVelocity(cam);
                SyncFreeAngles(cam, r);
            }
            catch (Exception ex) { Log.LogWarning("Kamera-Pose: " + ex.Message); }
        }

        static void JumpNear(ReplayGhost rg)
        {
            var cam = Cam();
            if (cam == null) return;
            Vector3 target = AimPoint(rg);
            float dist = Mathf.Clamp(JumpDistance(rg), 30f, 180f);
            Vector3 pos = target + new Vector3(0f, dist * 0.35f, -dist);
            Vector3 dir = target - pos;
            if (dir.sqrMagnitude < 1f) dir = Vector3.forward;
            var tr = cam.transform;
            tr.SetParent(null, true);
            tr.SetPositionAndRotation(pos, Quaternion.LookRotation(dir, Vector3.up));
            ZeroVelocity(cam);
        }

        static void SeedOwnOrbit(ReplayGhost rg)
        {
            var cam = Cam();
            Vector3 target = AimPoint(rg);
            Vector3 off = cam != null ? cam.transform.position - target : new Vector3(0f, 20f, -40f);
            if (off.sqrMagnitude < 25f) off = new Vector3(0f, 15f, -40f);
            float dist = off.magnitude;
            _orbDist = Mathf.Clamp(dist, 6f, 8000f);
            _orbYaw = Mathf.Atan2(off.x, off.z) * Mathf.Rad2Deg;
            _orbPitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(off.y / dist, -1f, 1f)) * Mathf.Rad2Deg, -85f, 85f);
        }

        static void PatchFreeCamera()
        {
            var t = FindGameType("CameraFreeState");
            var m = t?.GetMethod("UpdateState", Inst);
            if (m == null)
            {
                Log.LogWarning("CameraFreeState.UpdateState fehlt");
                return;
            }
            Harmony.Patch(m, postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterFreeCamera)));
            Log.LogInfo("Free-Look-Patch");
        }

        static void AfterFreeCamera()
        {
            if (CurrentDoc == null) return;
            try
            {
                if (_lookLock && Alive(_selected) && _selected != null)
                {
                    var cam = Cam();
                    if (cam != null) AimAt(cam, AimPoint(_selected!));
                    return;
                }
                if (_ownOrbit && Alive(_selected))
                    ApplyOwnOrbit();
            }
            catch (Exception ex)
            {
                if (_selErr != ex.Message)
                {
                    _selErr = ex.Message;
                    Log.LogWarning("Kamera-Ziel: " + ex.Message);
                }
                if (_lookLock) Log.LogInfo("lock off");
                _lookLock = false;
                _ownOrbit = false;
            }
        }

        static void AimAt(Component cam, Vector3 point)
        {
            Vector3 dir = point - cam.transform.position;
            if (dir.sqrMagnitude < 0.25f) return;
            if (Mathf.Abs(dir.y) > dir.magnitude * 0.995f)
                dir += Vector3.forward * 0.05f;
            Vector3 e = Quaternion.LookRotation(dir, Vector3.up).eulerAngles;
            float pitch = e.x > 180f ? e.x - 360f : e.x;
            cam.transform.rotation = Quaternion.Euler(pitch, e.y, 0f);
            SetFreeAngles(cam, pitch, e.y);
        }

        static void ApplyOwnOrbit()
        {
            var rg = _selected;
            if (!Alive(rg)) return;
            if (GameIsOrbiting(rg))
            {
                _ownOrbit = false;
                return;
            }
            var cam = Cam();
            if (cam == null) return;
            OrbitInput();
            Vector3 target = AimPoint(rg);
            float pr = _orbPitch * Mathf.Deg2Rad;
            float yr = _orbYaw * Mathf.Deg2Rad;
            float cp = Mathf.Cos(pr);
            Vector3 off = new Vector3(Mathf.Sin(yr) * cp, Mathf.Sin(pr), Mathf.Cos(yr) * cp) * _orbDist;
            if (off.sqrMagnitude < 1f) off = new Vector3(0f, 0f, -_orbDist);
            Vector3 pos = target + off;
            Vector3 dir = target - pos;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            Vector3 e = Quaternion.LookRotation(dir, Vector3.up).eulerAngles;
            float pitch = e.x > 180f ? e.x - 360f : e.x;
            var tr = cam.transform;
            tr.SetParent(null, true);
            tr.SetPositionAndRotation(pos, Quaternion.Euler(pitch, e.y, 0f));
            ZeroVelocity(cam);
            SetFreeAngles(cam, pitch, e.y);
        }

        static void OrbitInput()
        {
            if (Cursor.visible || ReplayTextFocused())
            {
                _prevMouseOk = false;
                return;
            }
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) dt = 0.016f;
            if (TryAxis("Pan View", out float pan) && TryAxis("Tilt View", out float tilt))
            {
                float fov = 60f;
                var lens = Cam()?.GetComponent<Camera>();
                if (lens != null) fov = lens.fieldOfView;
                float s = Mathf.Min(fov / 20f, 1f) * 90f * ViewSens() * dt;
                _orbYaw += pan * s;
                _orbPitch = Mathf.Clamp(_orbPitch + tilt * s * PitchSign(), -85f, 85f);
                if (TryAxis("Zoom View", out float zoom))
                {
                    zoom = Mathf.Clamp(zoom, -3f, 3f);
                    _orbDist = Mathf.Clamp(_orbDist * (1f - zoom * 0.2f), 6f, 8000f);
                }
                return;
            }
            var now = Input.mousePosition;
            if (_prevMouseOk)
            {
                _orbYaw += (now.x - _prevMouse.x) * 0.22f;
                _orbPitch = Mathf.Clamp(_orbPitch + (now.y - _prevMouse.y) * 0.16f * PitchSign(), -85f, 85f);
            }
            _prevMouse = now;
            _prevMouseOk = true;
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 10f) scroll /= 120f;
            scroll = Mathf.Clamp(scroll, -4f, 4f);
            if (Mathf.Abs(scroll) > 0.01f)
                _orbDist = Mathf.Clamp(_orbDist * (1f - scroll * 0.18f), 6f, 8000f);
        }

        static bool Alive(ReplayGhost? rg)
        {
            if (rg == null) return false;
            try { return rg.IsTrackAlive(); }
            catch { return false; }
        }

        static bool SelectionMatches(GameObject go)
        {
            if (go == null || _selected == null) return false;
            GameObject sg;
            try { sg = _selected.gameObject; }
            catch { return false; }
            if (sg == null) return false;
            if (sg == go) return true;
            try
            {
                return sg.transform.IsChildOf(go.transform) || go.transform.IsChildOf(sg.transform);
            }
            catch { return false; }
        }

        static bool TryPick(Component uc, out ReplayGhost rg)
        {
            var found = uc.GetComponent<ReplayGhost>()
                ?? uc.GetComponentInParent<ReplayGhost>()
                ?? uc.GetComponentInChildren<ReplayGhost>(true);
            if (found == null || !Alive(found))
            {
                rg = null!;
                return false;
            }
            rg = found!;
            return true;
        }

        static bool GameIsOrbiting(ReplayGhost? rg)
        {
            if (rg == null) return false;
            var cam = Cam();
            if (cam == null) return false;
            var t = cam.GetType();
            var fu = t.GetField("followingUnit", Inst)?.GetValue(cam) as Component;
            if (fu == null || fu.transform == null || rg.transform == null) return false;
            var gt = rg.transform;
            bool same = fu.gameObject == rg.gameObject
                || fu.transform.IsChildOf(gt)
                || gt.IsChildOf(fu.transform);
            if (!same) return false;
            var orbit = t.GetField("orbitState", Inst)?.GetValue(cam);
            var cur = t.GetProperty("currentState", Inst)?.GetValue(cam);
            return orbit != null && ReferenceEquals(cur, orbit);
        }

        static bool IconSelected(object icon)
        {
            var f = FieldIn(icon.GetType(), "isSelected");
            if (f == null) return true;
            try { return f.GetValue(icon) is bool on && on; }
            catch { return true; }
        }

        static FieldInfo? FieldIn(Type t, string name)
        {
            while (t != null)
            {
                var f = t.GetField(name, Inst | BindingFlags.DeclaredOnly);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        internal static string SelectionCaption()
        {
            if (!Alive(_selected) || _selected == null) return "";
            string name;
            try { name = LabelOf(_selected); }
            catch { return ""; }
            if (string.IsNullOrEmpty(name)) return "";
            return _lookLock ? name + "  [F]" : name;
        }

        static string LabelOf(ReplayGhost rg)
        {
            try
            {
                var t = rg.Track;
                if (t == null) return string.IsNullOrEmpty(rg.name) ? "unit" : rg.name;
                string? who = t.Pilot;
                if (string.IsNullOrEmpty(who))
                    who = PlayerNameFromCallSign(t.CallSign);
                string type = t.Name ?? "";
                if (string.IsNullOrEmpty(type)) type = TailOf(t.Type);
                if (string.IsNullOrEmpty(who))
                    return string.IsNullOrEmpty(type) ? "unit" : type;
                if (string.IsNullOrEmpty(type)) return who;
                if (type.IndexOf(who, StringComparison.OrdinalIgnoreCase) >= 0) return type;
                return type + "  " + who;
            }
            catch { return "unit"; }
        }

        static string TailOf(string? type)
        {
            if (string.IsNullOrEmpty(type)) return "";
            int plus = type.LastIndexOf('+');
            if (plus >= 0 && plus < type.Length - 1) return type.Substring(plus + 1);
            return type;
        }

        static float JumpDistance(ReplayGhost rg)
        {
            var go = rg.gameObject;
            if (go == null) return 40f;
            var unit = FindUnitOn(go);
            if (unit != null)
            {
                var f = unit.GetType().GetField("maxRadius", Inst);
                if (f?.GetValue(unit) is float rad && rad > 2f)
                    return rad * 2.2f;
            }
            return 40f;
        }

        static Vector3 AimPoint(ReplayGhost rg)
        {
            var tr = rg.transform;
            return tr != null ? tr.position : Vector3.zero;
        }

        static Component? Cam()
        {
            CacheListener();
            if (_camMgr == null || _camMgr.gameObject == null)
            {
                _listenFrame = -1;
                CacheListener();
            }
            return _camMgr;
        }

        static bool FlyMoveHeld()
        {
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A)
                || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D))
                return true;
            if (TryAxis("Move Longitudinal", out float lon) && Mathf.Abs(lon) > 0.1f) return true;
            if (TryAxis("Move Lateral", out float lat) && Mathf.Abs(lat) > 0.1f) return true;
            return false;
        }

        static bool TryAxis(string name, out float value)
        {
            value = 0f;
            if (_getAxis == null || _inputPlayer == null)
            {
                var gm = FindGameType("GameManager");
                var player = gm?.GetField("playerInput", Stat)?.GetValue(null);
                if (player == null) return false;
                var method = player.GetType().GetMethod("GetAxis", new[] { typeof(string) });
                if (method == null) return false;
                _inputPlayer = player;
                _getAxis = method;
            }
            try
            {
                var v = _getAxis.Invoke(_inputPlayer, new object[] { name });
                if (v is not float f) return false;
                value = f;
                return true;
            }
            catch { return false; }
        }

        static float ViewSens()
        {
            CacheView();
            if (_sensField?.GetValue(null) is float s && s > 0.01f) return s;
            return 1f;
        }

        static float PitchSign()
        {
            CacheView();
            if (_invertField?.GetValue(null) is bool inv && inv) return -1f;
            return 1f;
        }

        static void CacheView()
        {
            if (_sensField != null) return;
            var t = FindGameType("PlayerSettings");
            if (t == null) return;
            _sensField = t.GetField("viewSensitivity", Stat);
            _invertField = t.GetField("viewInvertPitch", Stat);
        }

        static void ZeroVelocity(Component cam)
        {
            try
            {
                cam.GetType().GetField("cameraVelocity", Inst)?.SetValue(cam, Vector3.zero);
            }
            catch { }
        }

        static void SyncFreeAngles(Component cam, Quaternion rot)
        {
            Vector3 e = rot.eulerAngles;
            float pitch = e.x > 180f ? e.x - 360f : e.x;
            SetFreeAngles(cam, pitch, e.y);
        }

        static void SetFreeAngles(Component cam, float pitch, float yaw)
        {
            var free = cam.GetType().GetField("freeState", Inst)?.GetValue(cam);
            if (free == null) return;
            if (_panField == null || _tiltField == null)
            {
                var ft = free.GetType();
                _panField = ft.GetField("panView", Inst);
                _tiltField = ft.GetField("tiltView", Inst);
            }
            try
            {
                _panField?.SetValue(free, yaw);
                _tiltField?.SetValue(free, pitch);
            }
            catch { }
        }

        internal static void RefreshReplayKeyMute()
        {
            if (!_keysMuted) return;
            RestoreReplayKeys();
            EnsureReplayKeysMuted();
        }

        static void EnsureReplayKeysMuted()
        {
            if (_keysMuted || CurrentDoc == null) return;
            try
            {
                int n = MuteReplayKeys();
                if (n < 0) return;
                _keysMuted = true;
                string label = ReplayMuteLabel();
                Log.LogInfo("Replay-Tasten " + (label.Length == 0 ? "(keine)" : label) + " stumm x" + n);
            }
            catch (Exception ex)
            {
                _keysMuted = true;
                Log.LogWarning("Replay-Tasten: " + ex.Message);
            }
        }

        static void RestoreReplayKeys()
        {
            for (int i = 0; i < _mutedKeys.Count; i++)
            {
                try { WriteEnabled(_mutedKeys[i], true); }
                catch { }
            }
            _mutedKeys.Clear();
            _keysMuted = false;
            _muteTries = 0;
        }

        // Look-at lock and trails. Turn off keyboard bindings that use those keys
        // so the game action does not fire as well. Other devices keep their bindings.
        static int MuteReplayKeys()
        {
            var gm = FindGameType("GameManager");
            var player = gm?.GetField("playerInput", Stat)?.GetValue(null);
            if (player == null) return -1;
            var controllers = player.GetType().GetProperty("controllers", Inst)?.GetValue(player);
            var maps = controllers?.GetType().GetProperty("maps", Inst)?.GetValue(controllers);
            if (maps == null) return -1;
            var all = AllMaps(maps);
            if (all == null) return -1;
            _muteTries++;
            bool sawKeyboard = false;
            int n = 0;
            var muteNames = ReplayMuteNames();
            foreach (var map in all)
            {
                if (map == null) continue;
                string kind = map.GetType().GetProperty("controllerType", Inst)?.GetValue(map)?.ToString() ?? "";
                if (kind.IndexOf("Keyboard", StringComparison.OrdinalIgnoreCase) < 0) continue;
                sawKeyboard = true;
                var elems = ElementMaps(map);
                if (elems == null) continue;
                foreach (var el in elems)
                {
                    if (el == null || !IsReplayMuteKey(el, muteNames)) continue;
                    if (!ReadEnabled(el, out bool on) || !on) continue;
                    if (!WriteEnabled(el, false)) continue;
                    _mutedKeys.Add(el);
                    n++;
                }
            }
            if (!sawKeyboard && _muteTries < 120) return -1;
            return n;
        }

        static string ReplayMuteLabel()
        {
            var names = ReplayMuteNames();
            if (names.Count == 0) return "";
            return string.Join("/", names);
        }

        static List<string> ReplayMuteNames()
        {
            var names = new List<string>(2);
            AddMuteName(names, RecordConfig.LookAtLockKey, KeyCode.F);
            AddMuteName(names, RecordConfig.ToggleTrailsKey, KeyCode.T);
            return names;
        }

        static void AddMuteName(List<string> names, ConfigEntry<KeyboardShortcut>? entry, KeyCode fallback)
        {
            KeyCode key = fallback;
            if (entry != null)
            {
                try { key = entry.Value.MainKey; }
                catch { key = fallback; }
            }
            if (key == KeyCode.None) return;
            string name = key.ToString();
            for (int i = 0; i < names.Count; i++)
                if (names[i] == name) return;
            names.Add(name);
        }

        static bool IsReplayMuteKey(object el, List<string> names)
        {
            try
            {
                var t = el.GetType();
                string a = t.GetProperty("keyCode", Inst)?.GetValue(el)?.ToString() ?? "";
                string b = t.GetProperty("keyboardKeyCode", Inst)?.GetValue(el)?.ToString() ?? "";
                return NameIn(names, a) || NameIn(names, b);
            }
            catch { return false; }
        }

        static bool NameIn(List<string> names, string name)
        {
            if (name.Length == 0) return false;
            for (int i = 0; i < names.Count; i++)
                if (names[i] == name) return true;
            return false;
        }

        static bool ReadEnabled(object el, out bool on)
        {
            on = false;
            var p = el.GetType().GetProperty("enabled", Inst);
            if (p == null || p.PropertyType != typeof(bool) || !p.CanRead) return false;
            try
            {
                on = p.GetValue(el) is bool b && b;
                return true;
            }
            catch { return false; }
        }

        static bool WriteEnabled(object el, bool on)
        {
            var p = el.GetType().GetProperty("enabled", Inst);
            if (p == null || !p.CanWrite || p.PropertyType != typeof(bool)) return false;
            try { p.SetValue(el, on); return true; }
            catch { return false; }
        }

        static IEnumerable? AllMaps(object maps)
        {
            var flags = Inst;
            foreach (var m in maps.GetType().GetMethods(flags))
            {
                if (m.Name != "GetAllMaps" || m.IsGenericMethod) continue;
                var ps = m.GetParameters();
                if (ps.Length != 0) continue;
                if (!typeof(IEnumerable).IsAssignableFrom(m.ReturnType)) continue;
                try { return m.Invoke(maps, null) as IEnumerable; }
                catch { return null; }
            }
            foreach (var m in maps.GetType().GetMethods(flags))
            {
                if (m.Name != "GetAllMaps" || m.IsGenericMethod) continue;
                var ps = m.GetParameters();
                if (ps.Length != 1 || !ps[0].ParameterType.IsGenericType) continue;
                try
                {
                    var list = Activator.CreateInstance(ps[0].ParameterType);
                    if (list == null) continue;
                    m.Invoke(maps, new[] { list });
                    return list as IEnumerable;
                }
                catch { }
            }
            return null;
        }

        static IEnumerable? ElementMaps(object map)
        {
            var t = map.GetType();
            var prop = t.GetProperty("ElementMaps", Inst);
            if (prop != null && typeof(IEnumerable).IsAssignableFrom(prop.PropertyType))
            {
                try
                {
                    if (prop.GetValue(map) is IEnumerable en) return en;
                }
                catch { }
            }
            foreach (var m in t.GetMethods(Inst))
            {
                if (m.Name != "GetElementMaps" || m.IsGenericMethod) continue;
                var ps = m.GetParameters();
                if (ps.Length == 0)
                {
                    try
                    {
                        var v = m.Invoke(map, null);
                        if (v is IEnumerable en) return en;
                    }
                    catch { }
                }
                else if (ps.Length == 1 && ps[0].ParameterType.IsGenericType)
                {
                    try
                    {
                        var list = Activator.CreateInstance(ps[0].ParameterType);
                        if (list == null) continue;
                        m.Invoke(map, new[] { list });
                        if (list is IEnumerable en) return en;
                    }
                    catch { }
                }
            }
            return null;
        }
    }
}
