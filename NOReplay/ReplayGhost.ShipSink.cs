using System;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        bool _shipDeadVisual;
        float _sinkClock = -1f;
        GameObject? _splashGo;
        bool _sinkLogged;
        static GameObject? _splashPrefab;
        static bool _splashTried;

        internal bool ShipIsDead(float t)
        {
            if (!_ship || Track == null) return false;
            if (Track.KilledAt >= 0f && t + 0.0001f >= Track.KilledAt) return true;
            if (!HasRecordedShipSink() && Track.RemovedAt >= 0f && t + 0.0001f >= Track.RemovedAt)
                return true;
            return false;
        }

        bool HasRecordedShipSink()
        {
            if (Track == null || Track.Samples.Count == 0) return false;
            float last = Track.Samples[Track.Samples.Count - 1].Time;
            return ShipSink.HasRecordedSink(Track.KilledAt, last);
        }

        internal bool InScriptedSink(float t)
        {
            if (!_ship || Track == null || HasRecordedShipSink()) return false;
            float end = Track.RemovedAt;
            if (end < 0f) return false;
            float u = t - end;
            return u >= 0f && u < ShipSink.Duration;
        }

        internal bool KeepsGhostActive()
        {
            if (IsTrackAlive()) return true;
            return InScriptedSink(Plugin.Clock.Time);
        }

        float ShipFrozenTime(float t)
        {
            if (Track == null) return t;
            float kill = Track.KilledAt >= 0f ? Track.KilledAt
                : (Track.RemovedAt >= 0f ? Track.RemovedAt : t);
            return t < kill ? t : kill;
        }

        void QuietDeadShip(float t)
        {
            _shipDeadVisual = true;
            Plugin.SetMapIcon(gameObject, false);
            ApplyTurretAngles(ShipFrozenTime(t));
            HaltGuns(false);
            StopShipFx(false);
            StopGroundAudio();
        }

        void ReleaseDeadShipGuns()
        {
            if (!_shipDeadVisual) return;
            _shipDeadVisual = false;
            ResumeGuns();
        }

        bool DriveScriptedShip(float t)
        {
            if (Track == null) return false;
            float removed = Track.RemovedAt;
            if (!TrySinkSample(removed, out var last, out var prev, out float dt, out bool havePrev))
                return false;
            _boomed = false;
            if (!_sinkLogged)
            {
                _sinkLogged = true;
                Plugin.Log.LogInfo("Schiff sinkt (Kurve) " + (Track.Name ?? name));
            }
            if (!_visible) SetGhostVisible(true);
            var local = Plugin.GlobalToLocal(last.X, last.Y, last.Z);
            float sea = SeaY();
            var q = QuatOf(last);
            var pq = havePrev ? QuatOf(prev) : Quaternion.identity;
            var pose = ShipSink.At(
                local.x, local.y, local.z,
                q.x, q.y, q.z, q.w,
                sea, t - removed,
                havePrev, pq.x, pq.y, pq.z, pq.w, dt);
            if (!_colsOff)
            {
                for (int i = 0; i < _cols.Length; i++)
                {
                    if (_cols[i] == null || IsIgnored(_cols[i].transform)) continue;
                    _cols[i].enabled = false;
                }
                _colsOff = true;
            }
            var drive = _drive != null ? _drive : transform;
            drive.SetPositionAndRotation(
                new Vector3(pose.X, pose.Y, pose.Z),
                new Quaternion(pose.Qx, pose.Qy, pose.Qz, pose.Qw));
            _velMs = Vector3.zero;
            _spdMs = 0f;
            for (int i = 0; i < _rbs.Length; i++)
            {
                var rb = _rbs[i];
                if (rb == null || IsIgnored(rb.transform)) continue;
                rb.isKinematic = true;
                rb.detectCollisions = false;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            QuietDeadShip(t);
            NoteShipSplash(removed, local.y, sea, t, new Vector3(pose.X, sea, pose.Z));
            if (_ship) SyncCasePattern(drive);
            try { DriveCarrier(t); }
            catch (Exception ex) { LogSubsystemOnce("Carrier", ex); }
            try { TickStructural(false); }
            catch (Exception ex) { LogSubsystemOnce("TickStructural", ex); }
            return true;
        }

        bool TrySinkSample(float removed, out ReplaySample last, out ReplaySample prev, out float dt, out bool havePrev)
        {
            last = default;
            prev = default;
            dt = 0f;
            havePrev = false;
            if (Track == null || Track.Samples.Count == 0) return false;
            var samples = Track.Samples;
            int i = samples.Count - 1;
            while (i >= 0 && samples[i].Time > removed + 0.001f) i--;
            if (i < 0) i = 0;
            last = samples[i];
            if (i >= 1)
            {
                prev = samples[i - 1];
                dt = last.Time - prev.Time;
                havePrev = dt >= 0.05f && dt <= 2.5f;
            }
            return true;
        }

        void NoteShipSplash(float removed, float startY, float sea, float t, Vector3 at)
        {
            float cross = ShipSink.CrossSeconds(startY, sea);
            float u = t - removed;
            bool havePrev = _sinkClock >= 0f;
            float prevU = havePrev ? _sinkClock - removed : -1f;
            // A seek, or playing back up through the waterline, drops the splash.
            // A normal frame that crosses it plays the splash once.
            bool jumped = havePrev && Mathf.Abs(t - _sinkClock) > 2f;
            if (!havePrev || jumped || cross < 0f || u < cross)
            {
                if (jumped || cross < 0f || u < cross)
                    ClearShipSplash();
            }
            else if (_splashGo == null && prevU < cross)
                SpawnShipSplash(at);
            _sinkClock = t;
        }

        void SpawnShipSplash(Vector3 at)
        {
            var prefab = ShipSplashPrefab();
            if (prefab == null) return;
            GameObject go;
            try { go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.LookRotation(Vector3.up)); }
            catch { return; }
            go.name = "NOReplay_ShipSplash";
            go.transform.SetParent(null, true);
            NeutralizeFx(go);
            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
                if (cols[i] != null) cols[i].enabled = false;
            PlayParticles(go);
            _splashGo = go;
        }

        GameObject? ShipSplashPrefab()
        {
            if (_splashTried) return _splashPrefab;
            _splashTried = true;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var partT = Plugin.FindGameType("ShipPart");
            if (partT != null)
            {
                var field = partT.GetField("sinkEffect", flags);
                if (field != null)
                {
                    var parts = GetComponentsInChildren(partT, true);
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i] == null) continue;
                        if (field.GetValue(parts[i]) is GameObject fx && fx != null)
                        {
                            _splashPrefab = fx;
                            return _splashPrefab;
                        }
                    }
                }
            }
            try
            {
                var gaT = Plugin.FindGameType("GameAssets");
                var ga = gaT?.GetProperty("i", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                         ?? gaT?.GetField("i", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (ga != null)
                    _splashPrefab = gaT?.GetField("splash_large")?.GetValue(ga) as GameObject;
            }
            catch { }
            return _splashPrefab;
        }

        void ClearShipSplash()
        {
            if (_splashGo == null) return;
            UnityEngine.Object.Destroy(_splashGo);
            _splashGo = null;
        }
    }
}
