using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class Plugin
    {
        void WarmOrdnance()
        {
            if (_warmLeft.Count == 0) return;
            int step = _holdClock ? 4 : 2;
            for (int n = 0; n < step && _warmLeft.Count > 0; n++)
            {
                var job = _warmLeft[0];
                var made = MakePooled(job.prefab);
                if (made != null)
                {
                    if (!_pool.TryGetValue(job.prefab.GetInstanceID(), out var q))
                    {
                        q = new Queue<GameObject>();
                        _pool[job.prefab.GetInstanceID()] = q;
                    }
                    q.Enqueue(made);
                    _poolOwner[made.GetInstanceID()] = job.prefab.GetInstanceID();
                }
                job.left--;
                if (job.left <= 0) _warmLeft.RemoveAt(0);
                else _warmLeft[0] = job;
            }
        }

        static void TickFlareEvents()
        {
            if (_flareEvents.Count == 0 || CurrentDoc == null) return;
            float t = Clock.Time;
            if (_flareClock >= 0f && t + 0.2f < _flareClock)
                _flareDone.Clear();
            _flareClock = t;
            int n = 0;
            for (int i = 0; i < _flareEvents.Count && n < 2; i++)
            {
                var obj = _flareEvents[i];
                float born = TrackBorn(obj);
                if (t + 0.05f < born) continue;
                if (_flareDone.Contains(obj)) continue;
                _flareDone.Add(obj);
                try { PlayAirframeFlare(obj); }
                catch (Exception ex) { Log.LogWarning("Flare-FX: " + ex.Message); }
                n++;
            }
        }

        static void PlayAirframeFlare(ReplayObject obj)
        {
            ReplaySample s;
            if (!obj.TrySample(Clock.Time, out s))
            {
                if (obj.Samples.Count == 0) return;
                s = obj.Samples[0];
            }
            Vector3 pos = GlobalToLocal(s.X, s.Y, s.Z);
            GameObject? host = null;
            float best = 250f * 250f;
            for (int i = 0; i < _ghosts.Count; i++)
            {
                var g = _ghosts[i];
                if (g == null) continue;
                var rg = g.GetComponent<ReplayGhost>();
                if (rg == null || rg.Track == null) continue;
                if (!IsAircraft(rg.Track.Type, rg.Track.Name)) continue;
                float d = (g.transform.position - pos).sqrMagnitude;
                if (d < best) { best = d; host = g; }
            }
            if (host == null)
            {
                if (_flareFxLog < 8)
                {
                    _flareFxLog++;
                    Log.LogWarning("Flare-FX kein Airframe nahe " + pos);
                }
                return;
            }
            if (!SpawnGameFlare(host, pos)) SpawnFlareOrb(pos);
            if (_flareFxLog < 8)
            {
                _flareFxLog++;
                Log.LogInfo("Flare-FX " + host.name + " dist=" + Mathf.Sqrt(best).ToString("0.0"));
            }
        }

        static bool SpawnGameFlare(GameObject host, Vector3 pos)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var ejectType = FindGameType("FlareEjector");
            if (ejectType == null) return false;
            var ejectors = host.GetComponentsInChildren(ejectType, true);
            if (ejectors == null || ejectors.Length == 0) return false;
            var points = new System.Collections.Generic.List<Transform>();
            GameObject? prefab = null;
            float speed = 25f;
            for (int i = 0; i < ejectors.Length; i++)
            {
                var ej = ejectors[i];
                if (prefab == null)
                    prefab = ej.GetType().GetField("flarePrefab", flags)?.GetValue(ej) as GameObject;
                var vel = ej.GetType().GetField("ejectionVelocity", flags)?.GetValue(ej);
                if (vel is float f && f > 1f) speed = f;
                var arr = ej.GetType().GetField("ejectionPoints", flags)?.GetValue(ej) as System.Array;
                if (arr == null) continue;
                for (int p = 0; p < arr.Length; p++)
                {
                    var pt = arr.GetValue(p);
                    var tr = pt?.GetType().GetField("transform", flags)?.GetValue(pt) as Transform;
                    if (tr != null) points.Add(tr);
                }
            }
            if (prefab == null) return false;
            Transform launch = points.Count > 0 ? points[_flareSide++ % points.Count] : host.transform;
            var go = UnityEngine.Object.Instantiate(prefab, launch.position, launch.rotation);
            go.name = "NOReplay_Flare";
            var flare = go.GetComponent(FindGameType("IRFlare") ?? typeof(Component));
            if (flare != null && flare.GetType().Name == "IRFlare")
            {
                var method = flare.GetType().GetMethod("LaunchFlare", flags);
                if (method != null)
                {
                    var ps = method.GetParameters();
                    var args = new object?[ps.Length];
                    var aircraft = host.GetComponent(FindGameType("Aircraft") ?? typeof(Component));
                    var kick = launch.forward * speed + Vector3.down * 3f;
                    for (int i = 0; i < ps.Length; i++)
                    {
                        string n = ps[i].Name.ToLowerInvariant();
                        if (n.Contains("aircraft")) args[i] = aircraft;
                        else if (n.Contains("point")) args[i] = launch;
                        else if (n.Contains("vel")) args[i] = kick;
                    }
                    try { method.Invoke(flare, args); } catch { }
                }
            }
            UnityEngine.Object.Destroy(go, 8f);
            PlayFlareEjectSound(host, launch);

            return true;
        }

        static void PrepareOrdnanceVisual(GameObject go, ReplayObject obj, Vector3 pos)
        {
            var at = Type.GetType("UnityEngine.AudioSource, UnityEngine.AudioModule")
                     ?? FindGameType("AudioSource");
            if (at != null)
            {
                var stop = at.GetMethod("Stop", Type.EmptyTypes);
                var playAwake = at.GetProperty("playOnAwake");
                foreach (var c in go.GetComponentsInChildren(at, true))
                {
                    if (c == null) continue;
                    try
                    {
                        playAwake?.SetValue(c, false);
                        stop?.Invoke(c, null);
                        at.GetProperty("spatialBlend")?.SetValue(c, 1f);
                        at.GetProperty("mute")?.SetValue(c, false);
                    }
                    catch { }
                }
            }
            foreach (var l in go.GetComponentsInChildren<Light>(true))
                if (l != null) l.enabled = false;
            foreach (var cam in go.GetComponentsInChildren<Camera>(true))
            {
                if (cam == null) continue;
                cam.enabled = false;
                cam.gameObject.tag = "Untagged";
            }
            foreach (var tr in go.GetComponentsInChildren<Transform>(true))
            {
                if (tr == null) continue;
                tr.gameObject.layer = 0;
                tr.gameObject.SetActive(true);
            }
            var lodType = FindGameType("LODGroup");
            if (lodType != null)
            {
                foreach (var c in go.GetComponentsInChildren(lodType, true))
                {
                    var beh = c as Behaviour;
                    if (beh != null) beh.enabled = false;
                }
            }
            var rend = go.GetComponentsInChildren<Renderer>(true);
            int on = 0;
            for (int i = 0; i < rend.Length; i++)
            {
                if (rend[i] == null) continue;
                rend[i].enabled = true;
                on++;
            }
            Log.LogInfo("Ordnance " + obj.Name + " rend=" + rend.Length
                        + " pos=" + pos.x.ToString("0") + "," + pos.y.ToString("0") + "," + pos.z.ToString("0"));
        }

        static void ShowMissile(GameObject go)
        {
            int rends = 0;
            foreach (var tr in go.GetComponentsInChildren<Transform>(true))
            {
                if (tr == null) continue;
                tr.gameObject.layer = 0;
                if (!tr.gameObject.activeSelf) tr.gameObject.SetActive(true);
            }
            var rs = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null) continue;
                rs[i].enabled = true;
                rends++;
            }
            var mt = FindGameType("Missile");
            var missile = mt != null ? go.GetComponent(mt) : null;
            if (missile != null && mt != null)
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                try { mt.GetMethod("SetTangible", flags)?.Invoke(missile, new object[] { true }); } catch { }
                try
                {
                    var flight = mt.GetField("flightSound", flags)?.GetValue(missile);
                    if (flight != null)
                    {
                        var ft = flight.GetType();
                        ft.GetProperty("mute")?.SetValue(flight, false);
                        ft.GetProperty("loop")?.SetValue(flight, true);
                        ft.GetProperty("volume")?.SetValue(flight, 0.7f);
                        ft.GetProperty("spatialBlend")?.SetValue(flight, 1f);
                        bool playing = ft.GetProperty("isPlaying")?.GetValue(flight) is bool b && b;
                        if (!playing) ft.GetMethod("Play", Type.EmptyTypes)?.Invoke(flight, null);
                    }
                }
                catch { }
                var motors = mt.GetField("motors", flags)?.GetValue(missile) as Array;
                if (motors != null)
                {
                    foreach (var motor in motors)
                    {
                        if (motor == null) continue;
                        try { motor.GetType().GetMethod("Activate", flags)?.Invoke(motor, new[] { missile }); } catch { }
                    }
                }
            }
        }
    }
}
