using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        // GroundVehicle.Update never runs: SilenceAirframe disables the behaviour,
        // and the body is kinematic. AnimateWheels is the game's visual:
        //   tracks.material.mainTextureOffset += (0, speed * -0.2 * dt)
        //   wheel.Rotate((speed * Rad2Deg * dt / wheelRadius), 0, 0)
        // speed is the signed nose component of velocity. One tracks renderer
        // cannot scroll left and right opposite ways, so a single track mesh
        // keeps the centerline speed. Two separate track renderers, and every
        // wheel, get the skid-steer term.
        const float WheelVis = 1000f;

        static FieldInfo? _fWheels;
        static FieldInfo? _fTracks;
        static FieldInfo? _fWheelRadius;
        static FieldInfo? _fWheelsLocked;
        static bool _wheelFields;

        bool _wheelsBound;
        bool _wheelsNone;
        bool _wheelsLocked;
        float _wheelRadius = GroundRoll.DefaultRadius;
        Transform[] _wheelTr = System.Array.Empty<Transform>();
        float[] _wheelX = System.Array.Empty<float>();
        Renderer[] _trackR = System.Array.Empty<Renderer>();
        Material[] _trackMat = System.Array.Empty<Material>();
        float[] _trackX = System.Array.Empty<float>();
        bool _wheelHave;
        Vector3 _wheelPrevPos;
        Vector3 _wheelPrevFwd;
        float _wheelPrevClock = -1f;

        static int _wheelLogs;

        void TickWheels()
        {
            if (_wheelsNone || !_visible || _neutral)
            {
                _wheelHave = false;
                return;
            }
            try
            {
                TickWheelsNow();
            }
            catch (Exception ex)
            {
                if (_wheelLogs < 4)
                {
                    _wheelLogs++;
                    Plugin.Log?.LogWarning("Wheels " + name + ": " + ex.Message);
                }
            }
        }

        void TickWheelsNow()
        {
            if (_wheelsNone || !_visible || _neutral)
            {
                _wheelHave = false;
                return;
            }
            if (!WheelsInView())
            {
                _wheelHave = false;
                return;
            }
            if (!_wheelsBound) BindWheels();
            if (_wheelsNone || _wheelsLocked) return;

            Transform drive = Drive;
            Vector3 pos = drive.position;
            Vector3 fwd = drive.forward;
            float now = Plugin.Clock != null ? Plugin.Clock.Time : 0f;
            if (!_wheelHave)
            {
                RememberWheels(pos, fwd, now);
                return;
            }

            Vector3 delta = pos - _wheelPrevPos;
            float yawDeg = FlatYaw(_wheelPrevFwd, fwd);
            float clockDt = _wheelPrevClock >= 0f ? now - _wheelPrevClock : 0f;
            bool seek = GroundRoll.IsSeek(clockDt, delta.sqrMagnitude, yawDeg);
            if (!seek && (delta.sqrMagnitude > 1e-6f || Mathf.Abs(yawDeg) > 0.05f))
            {
                float forward = Vector3.Dot(delta, fwd);
                float yawRad = yawDeg * Mathf.Deg2Rad;
                SpinWheels(forward, yawRad);
                ScrollTracks(forward, yawRad);
            }
            RememberWheels(pos, fwd, now);
        }

        void RememberWheels(Vector3 pos, Vector3 fwd, float now)
        {
            _wheelPrevPos = pos;
            _wheelPrevFwd = fwd;
            _wheelPrevClock = now;
            _wheelHave = true;
        }

        void SpinWheels(float forward, float yawRad)
        {
            for (int i = 0; i < _wheelTr.Length; i++)
            {
                var tr = _wheelTr[i];
                if (tr == null) continue;
                float deg = GroundRoll.WheelDegrees(GroundRoll.Travel(forward, yawRad, _wheelX[i]), _wheelRadius);
                if (deg > 0.02f || deg < -0.02f)
                    tr.Rotate(deg, 0f, 0f, Space.Self);
            }
        }

        void ScrollTracks(float forward, float yawRad)
        {
            bool split = _trackR.Length > 1;
            for (int i = 0; i < _trackR.Length; i++)
            {
                var mat = _trackMat[i];
                if (mat == null) continue;
                float travel = split ? GroundRoll.Travel(forward, yawRad, _trackX[i]) : forward;
                float add = GroundRoll.TrackOffset(travel);
                if (add > -1e-6f && add < 1e-6f) continue;
                Vector2 offset = mat.mainTextureOffset;
                offset.y += add;
                offset.y -= Mathf.Floor(offset.y);
                mat.mainTextureOffset = offset;
            }
        }

        void BindWheels()
        {
            _wheelsBound = true;
            BindWheelFields();
            var gv = GetComponentInChildren<GroundVehicle>(true);
            if (gv == null)
            {
                _wheelsNone = true;
                return;
            }
            if (_fWheelsLocked?.GetValue(gv) is bool locked && locked)
            {
                _wheelsLocked = true;
                return;
            }
            if (_fWheelRadius?.GetValue(gv) is float radius && radius > 0.02f)
                _wheelRadius = radius;

            Transform host = gv.transform;
            var wheels = new List<Transform>(8);
            var wheelX = new List<float>(8);
            if (_fWheels?.GetValue(gv) is GameObject[] objs)
            {
                for (int i = 0; i < objs.Length; i++)
                {
                    var go = objs[i];
                    if (go == null) continue;
                    wheels.Add(go.transform);
                    wheelX.Add(LeverX(host, go.transform.position));
                }
            }
            _wheelTr = wheels.ToArray();
            _wheelX = wheelX.ToArray();

            var tracks = new List<Renderer>(2);
            var trackX = new List<float>(2);
            var mats = new List<Material>(2);
            if (_fTracks?.GetValue(gv) is Renderer primary && primary != null)
                AddTrack(host, primary, tracks, trackX, mats);
            var rends = gv.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null || !TrackName(r.name)) continue;
                if (UnderWheel(r.transform, wheels)) continue;
                if (RelatedTrack(r, tracks)) continue;
                AddTrack(host, r, tracks, trackX, mats);
            }
            _trackR = tracks.ToArray();
            _trackX = trackX.ToArray();
            _trackMat = mats.ToArray();
            _wheelsNone = _wheelTr.Length == 0 && _trackR.Length == 0;
        }

        static void BindWheelFields()
        {
            if (_wheelFields) return;
            _wheelFields = true;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = typeof(GroundVehicle);
            _fWheels = t.GetField("wheels", flags);
            _fTracks = t.GetField("tracks", flags);
            _fWheelRadius = t.GetField("wheelRadius", flags);
            _fWheelsLocked = t.GetField("wheelsLocked", flags);
        }

        static void AddTrack(Transform host, Renderer rend, List<Renderer> tracks, List<float> xs, List<Material> mats)
        {
            Material? mat = null;
            try { mat = rend.material; }
            catch { return; }
            if (mat == null) return;
            tracks.Add(rend);
            xs.Add(LeverX(host, rend.bounds.center));
            mats.Add(mat);
        }

        static bool RelatedTrack(Renderer rend, List<Renderer> tracks)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                var have = tracks[i];
                if (have == null) continue;
                if (rend.transform == have.transform) return true;
                if (rend.transform.IsChildOf(have.transform)) return true;
                if (have.transform.IsChildOf(rend.transform)) return true;
            }
            return false;
        }

        static bool UnderWheel(Transform t, List<Transform> wheels)
        {
            for (int i = 0; i < wheels.Count; i++)
            {
                var w = wheels[i];
                if (w == null) continue;
                if (t == w || t.IsChildOf(w)) return true;
            }
            return false;
        }

        static bool TrackName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("track", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("tread", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("caterpillar", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static float LeverX(Transform host, Vector3 world)
        {
            float x = host.InverseTransformPoint(world).x;
            if (x > GroundRoll.MaxLever || x < -GroundRoll.MaxLever) return 0f;
            return x;
        }

        bool WheelsInView()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return NearMe(WheelVis);
            float lim = WheelVis * WheelVis;
            return (transform.position - _cam.transform.position).sqrMagnitude <= lim;
        }

        void ReleaseWheelMaterials()
        {
            for (int i = 0; i < _trackMat.Length; i++)
            {
                var mat = _trackMat[i];
                _trackMat[i] = null!;
                if (mat != null) UnityEngine.Object.Destroy(mat);
            }
        }

        static float FlatYaw(Vector3 from, Vector3 to)
        {
            from.y = 0f;
            to.y = 0f;
            if (from.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.SignedAngle(from, to, Vector3.up);
        }
    }
}
