using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class Plugin
    {
        internal static void ApplyReplayIdentity(GameObject go, ReplayObject? obj)
        {
            if (go == null) return;
            TryApplyRecordedLivery(go, obj);
            TryApplyRecordedLoadout(go, obj);
            if (obj == null) return;
            string? player = obj.Pilot;
            if (string.IsNullOrEmpty(player))
                player = PlayerNameFromCallSign(obj.CallSign);
            string label = string.IsNullOrEmpty(player) ? (obj.Name ?? "") : player;
            if (string.IsNullOrEmpty(label)) return;
            var unit = FindUnitOn(go);
            if (unit == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = unit.GetType();
            try { t.GetProperty("NetworkunitName", flags)?.SetValue(unit, label); } catch { }
            try { t.GetField("unitName", flags)?.SetValue(unit, label); } catch { }
            RefreshSpectatorName(unit, label);
            ApplyPanel(go, obj, label);
        }

        internal static void ApplyPanel(GameObject go, ReplayObject? obj, string? label)
        {
            if (go == null) return;
            var unit = FindUnitOn(go);
            if (unit == null) return;
            var dbg = FindGameType("UnitDebug");
            if (dbg == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fFollow = dbg.GetField("followingUnit", flags);
            if (fFollow == null) return;
            string load = FormatLoadout(obj?.Loadout);
            foreach (var o in Resources.FindObjectsOfTypeAll(dbg))
            {
                if (o == null) continue;
                if (!ReferenceEquals(fFollow.GetValue(o), unit)) continue;
                SetTextField(o, "unitName", label ?? obj?.Name ?? "");
                if (load.Length > 0) SetTextField(o, "loadout", load);
                PinStateText(o);
            }
        }

        internal static void SetMapIcon(GameObject go, bool on)
        {
            if (go == null) return;
            var iconType = FindGameType("UnitMapIcon");
            if (iconType == null) return;
            if (Time.unscaledTime - _iconCacheAt > 2f)
            {
                _iconCache = Resources.FindObjectsOfTypeAll(iconType);
                _iconCacheAt = Time.unscaledTime;
                _iconUnitProp = iconType.GetProperty("unit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            if (_iconUnitProp == null) return;
            for (int i = 0; i < _iconCache.Length; i++)
            {
                var o = _iconCache[i];
                if (o == null) continue;
                var u = _iconUnitProp.GetValue(o) as Component;
                if (u == null) continue;
                if (u.gameObject != go && !u.transform.IsChildOf(go.transform) && !go.transform.IsChildOf(u.transform))
                    continue;
                if (o is Component c && c.gameObject != null && c.gameObject.activeSelf != on)
                    c.gameObject.SetActive(on);
            }
        }

        static void TickOrdnanceCamera()
        {
            var camType = FindGameType("CameraStateManager");
            if (camType == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fFollow = camType.GetField("followingUnit", flags);
            var mSet = camType.GetMethod("SetFollowingUnit", flags);
            var mSw = camType.GetMethod("SwitchState", flags);
            if (fFollow == null) return;
            foreach (var cam in Resources.FindObjectsOfTypeAll(camType))
            {
                if (cam == null) continue;
                var u = fFollow.GetValue(cam) as Component;
                if (u == null) continue;
                var rg = u.GetComponentInParent<ReplayGhost>();
                if (rg == null || !IsOrdnance(rg.Track?.Type, rg.Track?.Name)) continue;
                DetachCamerasFrom(rg.gameObject);
                object? free = camType.GetField("freeState", flags)?.GetValue(cam);
                mSet?.Invoke(cam, new object[] { null });
                if (free != null) mSw?.Invoke(cam, new[] { free });
            }
        }

        internal static void ReleaseCameraIfOn(GameObject go) => ReleaseFollowedCamera(go);

        static UnityEngine.Object? _replayCam;
        static bool _camFreed;

        static void TryFollowGhost(GameObject go)
        {
            var ghost = go.GetComponent<ReplayGhost>();
            if (ghost != null && IsOrdnance(ghost.Track?.Type, ghost.Track?.Name))
            {
                LogOnce("Cam-Bombe", "Cam bleibt frei, keine Bombe verfolgt: " + go.name, false);
                return;
            }
            var unit = FindUnitOn(go);
            if (unit == null) return;
            var camType = FindGameType("CameraStateManager");
            if (camType == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var cam in Resources.FindObjectsOfTypeAll(camType))
            {
                if (cam is not Component view || !view.gameObject.scene.IsValid()) continue;
                var sw = camType.GetMethod("SwitchState", flags);
                var follow = camType.GetMethod("SetFollowingUnit", flags);
                var tv = camType.GetField("TVState", flags)?.GetValue(cam);
                follow?.Invoke(cam, new object[] { unit });
                if (tv != null) sw?.Invoke(cam, new[] { tv });
                break;
            }
        }

        /// <summary>
        /// TV mode is entered before its EnterState runs. Following a bomb is
        /// refused, so EnterState throws and the camera stays in TV with no
        /// target: movement is zeroed and the pivot is planted on the ground.
        /// </summary>
        internal static void KeepReplayCamera()
        {
            if (CurrentDoc == null) return;
            var camType = FindGameType("CameraStateManager");
            if (camType == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (_replayCam == null)
            {
                foreach (var cam in Resources.FindObjectsOfTypeAll(camType))
                {
                    if (cam is Component view && view.gameObject.scene.IsValid())
                    {
                        _replayCam = view;
                        break;
                    }
                }
            }
            if (_replayCam == null) return;
            var state = camType.GetProperty("currentState", flags)?.GetValue(_replayCam);
            var tv = camType.GetField("TVState", flags)?.GetValue(_replayCam);
            if (state == null || tv == null || !ReferenceEquals(state, tv)) return;
            var unit = camType.GetField("followingUnit", flags)?.GetValue(_replayCam) as Component;
            if (unit != null)
            {
                var rg = unit.GetComponent<ReplayGhost>() ?? unit.GetComponentInParent<ReplayGhost>();
                if (rg == null || !IsOrdnance(rg.Track?.Type, rg.Track?.Name)) return;
                DetachCamerasFrom(unit.gameObject);
            }
            var free = camType.GetField("freeState", flags)?.GetValue(_replayCam);
            if (free == null) return;
            try { camType.GetMethod("SwitchState", flags)?.Invoke(_replayCam, new[] { free }); }
            catch (Exception ex)
            {
                Log.LogWarning("Cam frei: " + (ex.InnerException?.Message ?? ex.Message));
                return;
            }
            if (_camFreed) return;
            _camFreed = true;
            Log.LogInfo("Cam TV verlassen, freie Sicht");
        }
    }
}
