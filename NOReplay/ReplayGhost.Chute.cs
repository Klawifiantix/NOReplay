using System;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        struct ChutePart
        {
            public Component Source;
            public GameObject? Canopy;
            public GameObject? Lines;
            public Transform? CanopyParent;
            public Renderer? Cloth;
        }

        ChutePart[] _chutes = Array.Empty<ChutePart>();
        GameObject? _chuteRoot;
        bool _chuteRootOwned;
        bool _chuteBound;
        bool _chuteNone;
        bool _chuteLogged;
        int _chuteWait;
        float _chuteMeshAgain = -1f;
        float _chutePrevClock = -1f;
        bool _chuteAudioPaused;
        Component? _seat;
        Transform? _seatParent;
        AudioSource? _seatAudio;
        AudioClip? _seatClip;
        Component[] _seatParticles = Array.Empty<Component>();
        Component? _pilotAnim;
        bool _pilotRun;
        static readonly BindingFlags ChuteFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        void DriveChutes()
        {
            if (Track == null) return;
            if (!_chuteBound) BindChutes();
            if (_chuteNone) return;
            float time = Plugin.Clock.Time;
            float dt = _chutePrevClock >= 0f ? time - _chutePrevClock : 0f;
            bool had = _chutePrevClock >= 0f;
            _chutePrevClock = time;
            bool paused = Plugin.Clock.Paused;
            bool rig = Track.TryChute("r", time, out var rigView) && rigView.On;
            if (_chuteRootOwned && _chuteRoot != null && _chuteRoot.activeSelf != rig)
                _chuteRoot.SetActive(rig);
            SetChuteWheels(rig);
            for (int i = 0; i < _chutes.Length; i++)
                PlaceCanopy(ref _chutes[i], "c" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), time);
            if (Track.TryChute("p", time, out var pilot))
                PlacePilot(pilot, dt, paused);
            if (Track.TryChute("s", time, out var seat))
                PlaceSeat(seat);
            bool fire = had && Track.ChuteCrossed("f", _chutePrevClock - dt, time, dt, paused);
            if (fire) PlaySeatFire();
            HoldSeatAudio(paused);
            if (_chuteMeshAgain > 0f && Time.unscaledTime >= _chuteMeshAgain)
            {
                _chuteMeshAgain = -1f;
                LogChuteMeshes("t+3s");
            }
        }

        void QuietChutes()
        {
            for (int i = 0; i < _chutes.Length; i++)
            {
                if (_chutes[i].Canopy != null && _chutes[i].Canopy.activeSelf)
                    _chutes[i].Canopy.SetActive(false);
                if (_chutes[i].Lines != null && _chutes[i].Lines.activeSelf)
                    _chutes[i].Lines.SetActive(false);
            }
            HoldSeatAudio(true);
        }

        void ReleaseChutes()
        {
            for (int i = 0; i < _chutes.Length; i++)
            {
                var canopy = _chutes[i].Canopy;
                if (canopy == null) continue;
                if (_chutes[i].CanopyParent != null && canopy.transform.parent != _chutes[i].CanopyParent)
                    canopy.transform.SetParent(_chutes[i].CanopyParent, true);
            }
            if (_seat != null && _seatParent != null && _seat.transform.parent != _seatParent)
                _seat.transform.SetParent(_seatParent, false);
            if (_chuteRootOwned && _chuteRoot != null)
                UnityEngine.Object.Destroy(_chuteRoot);
            _chuteRoot = null;
            _chutes = Array.Empty<ChutePart>();
        }

        void BindChutes()
        {
            MuteChuteScripts(gameObject);
            bool wanted = Track != null && Track.HasChute;
            var found = FindChuteParts();
            if (found.Length == 0 && wanted)
            {
                var box = Plugin.FindGameType("Container");
                if (box != null && GetComponent(box) != null && _chuteWait < 8)
                {
                    _chuteWait++;
                    return;
                }
                var prefab = ChutePrefab();
                if (prefab != null)
                {
                    _chuteRoot = UnityEngine.Object.Instantiate(prefab, transform);
                    _chuteRoot.name = "chute";
                    _chuteRootOwned = true;
                    FitCargoFrame(_chuteRoot);
                    MuteChuteScripts(_chuteRoot);
                    found = FindChuteParts();
                }
            }
            _chuteBound = true;
            _chutes = found;
            BindSeat();
            BindPilotAnimator();
            if (!wanted)
            {
                _chuteNone = true;
                return;
            }
            int samples = 0;
            float deploy = -1f;
            if (Track != null)
            {
                for (int i = 0; i < Math.Max(_chutes.Length, 1); i++)
                {
                    samples += Track.ChuteCount("c" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    float d = Track.ChuteDeploy("c" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (d >= 0f && (deploy < 0f || d < deploy)) deploy = d;
                }
            }
            string unitName = Track != null && !string.IsNullOrEmpty(Track.Name) ? Track.Name : name;
            int rends = 0;
            for (int i = 0; i < _chutes.Length; i++)
                if (_chutes[i].Cloth != null) rends++;
            Plugin.Log?.LogInfo("Chute play " + unitName
                + " samples=" + samples
                + " deploy=" + (deploy >= 0f ? deploy.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "-")
                + " rends=" + rends
                + " seat=" + (_seat != null ? 1 : 0));
            if (!_chuteLogged)
            {
                _chuteLogged = true;
                LogChuteMeshes("setup");
                _chuteMeshAgain = Time.unscaledTime + 3f;
            }
        }

        ChutePart[] FindChuteParts()
        {
            var t = Plugin.FindGameType("Parachute");
            if (t == null) return Array.Empty<ChutePart>();
            var all = GetComponentsInChildren(t, true);
            var list = new System.Collections.Generic.List<ChutePart>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is not Component c) continue;
                var part = new ChutePart { Source = c };
                part.Canopy = c.GetType().GetField("canopy", ChuteFlags)?.GetValue(c) as GameObject;
                part.Lines = c.GetType().GetField("lines", ChuteFlags)?.GetValue(c) as GameObject;
                part.CanopyParent = part.Canopy != null ? part.Canopy.transform.parent : null;
                part.Cloth = c.GetType().GetField("parachuteRenderer", ChuteFlags)?.GetValue(c) as Renderer;
                list.Add(part);
            }
            return list.ToArray();
        }

        void PlaceCanopy(ref ChutePart part, string key, float time)
        {
            bool show = Track != null && Track.TryChute(key, time, out var view) && view.On && view.HasPose;
            if (!show)
            {
                if (part.Canopy != null && part.Canopy.activeSelf) part.Canopy.SetActive(false);
                if (part.Lines != null && part.Lines.activeSelf) part.Lines.SetActive(false);
                return;
            }
            Track!.TryChute(key, time, out var pose);
            if (part.Canopy != null)
            {
                if (!part.Canopy.activeSelf) part.Canopy.SetActive(true);
                if (part.Canopy.transform.parent != null)
                    part.Canopy.transform.SetParent(null, true);
                var p = Plugin.GlobalToLocal(pose.X, pose.Y, pose.Z);
                part.Canopy.transform.SetPositionAndRotation(p, new Quaternion(pose.Qx, pose.Qy, pose.Qz, pose.Qw));
                part.Canopy.transform.localScale = new Vector3(pose.Sx, pose.Sy, pose.Sz);
                RevealChute(part.Canopy);
                if (part.Cloth != null)
                {
                    try { part.Cloth.material.SetFloat("_wrinkleDisplacment", pose.Wrinkle); } catch { }
                    try { part.Cloth.material.SetFloat("_wrinkleStrength", pose.Strength); } catch { }
                }
            }
            if (part.Lines != null)
            {
                if (!part.Lines.activeSelf) part.Lines.SetActive(true);
                part.Lines.transform.localScale = new Vector3(pose.Lx, pose.Ly, pose.Lz);
                if (part.Canopy != null)
                    part.Lines.transform.LookAt(part.Canopy.transform.position, transform.forward);
                RevealChute(part.Lines);
            }
        }

        void RevealChute(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null) continue;
                _disabledInPrefab.Remove(r);
                if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
                if (_visible) r.enabled = true;
                TouchRend(r, "ShowChute");
            }
        }

        void PlacePilot(ChuteClock.View view, float dt, bool paused)
        {
            if (_pilotAnim == null) return;
            if (_pilotAnim is Behaviour animBeh) animBeh.enabled = true;
            _pilotAnim.GetType().GetMethod("SetInteger", new[] { typeof(string), typeof(int) })
                ?.Invoke(_pilotAnim, new object[] { "PilotState", view.State });
            // PilotDismounted.PilotState.landing is 4. The run layer is the
            // landing blend in PilotDismounted.FixedUpdate.
            bool landing = view.State == 4 && !paused && dt > 0.0001f && dt <= 0.35f;
            float speed = 0f;
            if (landing && _prevT >= 0f)
            {
                var delta = transform.position - _prevLocalPos;
                speed = delta.magnitude / dt;
            }
            if (landing)
            {
                SetAnimWeight(Mathf.Clamp01(speed * 2f - 0.5f));
                SetAnimFloat("RunSpeed", speed * 0.25f);
                _pilotRun = true;
            }
            else if (_pilotRun)
            {
                SetAnimWeight(0f);
                _pilotRun = false;
            }
        }

        void PlaceSeat(ChuteClock.View view)
        {
            if (_seat == null) return;
            if (!view.On || !view.HasPose)
            {
                if (_seatParent != null && _seat.transform.parent != _seatParent)
                    _seat.transform.SetParent(_seatParent, false);
                return;
            }
            _seat.transform.SetParent(null, true);
            var p = Plugin.GlobalToLocal(view.X, view.Y, view.Z);
            _seat.transform.SetPositionAndRotation(p, new Quaternion(view.Qx, view.Qy, view.Qz, view.Qw));
        }

        void PlaySeatFire()
        {
            if (_seatClip != null)
            {
                if (_seatAudio == null && _seat != null)
                {
                    _seatAudio = _seat.gameObject.AddComponent<AudioSource>();
                    _seatAudio.clip = _seatClip;
                    _seatAudio.volume = 2f;
                    _seatAudio.spatialBlend = 1f;
                    _seatAudio.dopplerLevel = 0f;
                    _seatAudio.minDistance = 50f;
                    _seatAudio.maxDistance = 1000f;
                    _seatAudio.rolloffMode = AudioRolloffMode.Linear;
                    _seatAudio.playOnAwake = false;
                }
                if (_seatAudio != null && !_seatAudio.isPlaying)
                    _seatAudio.Play();
            }
            for (int i = 0; i < _seatParticles.Length; i++)
            {
                try { _seatParticles[i].GetType().GetMethod("Play", Type.EmptyTypes)?.Invoke(_seatParticles[i], null); }
                catch { }
            }
        }

        void HoldSeatAudio(bool paused)
        {
            if (_seatAudio == null) return;
            if (paused)
            {
                if (_seatAudio.isPlaying)
                {
                    _seatAudio.Pause();
                    _chuteAudioPaused = true;
                }
                return;
            }
            if (_chuteAudioPaused)
            {
                _seatAudio.UnPause();
                _chuteAudioPaused = false;
            }
        }

        void BindSeat()
        {
            var t = Plugin.FindGameType("EjectionSeat");
            if (t == null) return;
            var all = GetComponentsInChildren(t, true);
            if (all.Length == 0 || all[0] is not Component seat) return;
            _seat = seat;
            _seatParent = seat.transform.parent;
            _seatClip = seat.GetType().GetField("fireSound", ChuteFlags)?.GetValue(seat) as AudioClip;
            if (seat.GetType().GetField("ejectParticles", ChuteFlags)?.GetValue(seat) is Array particles)
            {
                var list = new System.Collections.Generic.List<Component>();
                for (int i = 0; i < particles.Length; i++)
                    if (particles.GetValue(i) is Component c) list.Add(c);
                _seatParticles = list.ToArray();
            }
            if (seat is Behaviour b) b.enabled = false;
        }

        void BindPilotAnimator()
        {
            var t = Plugin.FindGameType("PilotDismounted");
            if (t == null) return;
            var all = GetComponentsInChildren(t, true);
            if (all.Length == 0 || all[0] is not Component pilot) return;
            _pilotAnim = pilot.GetType().GetField("animator", ChuteFlags)?.GetValue(pilot) as Component;
            if (_pilotAnim == null)
            {
                var animType = Type.GetType("UnityEngine.Animator, UnityEngine.AnimationModule");
                if (animType != null)
                    _pilotAnim = pilot.GetComponent(animType);
            }
        }

        void SetAnimWeight(float weight)
        {
            if (_pilotAnim == null) return;
            _pilotAnim.GetType().GetMethod("SetLayerWeight", new[] { typeof(int), typeof(float) })
                ?.Invoke(_pilotAnim, new object[] { 1, weight });
        }

        void SetAnimFloat(string prop, float value)
        {
            if (_pilotAnim == null) return;
            _pilotAnim.GetType().GetMethod("SetFloat", new[] { typeof(string), typeof(float) })
                ?.Invoke(_pilotAnim, new object[] { prop, value });
        }

        GameObject? ChutePrefab()
        {
            var all = GetComponents<Component>();
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i];
                if (c == null) continue;
                if (c.GetType().GetField("parachuteSystem", ChuteFlags)?.GetValue(c) is GameObject go && go != null)
                    return go;
            }
            return null;
        }

        void FitCargoFrame(GameObject root)
        {
            var sysType = Plugin.FindGameType("CargoDeploymentSystem");
            if (sysType == null) return;
            var systems = root.GetComponentsInChildren(sysType, true);
            if (systems.Length == 0 || systems[0] is not Component system) return;
            if (system.GetType().GetField("initialized", ChuteFlags)?.GetValue(system) is bool ready && ready)
                return;
            var frame = system.GetType().GetField("liftingFrame", ChuteFlags)?.GetValue(system) as GameObject;
            if (frame == null) return;
            if (system.GetType().GetField("baseFrameDimensions", ChuteFlags)?.GetValue(system) is not Vector3 dims)
                return;
            if (dims.x == 0f || dims.y == 0f || dims.z == 0f) return;
            object? def = null;
            var comps = GetComponents<Component>();
            for (int i = 0; i < comps.Length && def == null; i++)
            {
                if (comps[i] == null) continue;
                def = comps[i].GetType().GetField("definition", ChuteFlags)?.GetValue(comps[i])
                    ?? comps[i].GetType().GetProperty("definition", ChuteFlags)?.GetValue(comps[i]);
            }
            if (def == null) return;
            var dt = def.GetType();
            float length = dt.GetField("length", ChuteFlags)?.GetValue(def) is float l ? l : 0f;
            float width = dt.GetField("width", ChuteFlags)?.GetValue(def) is float w ? w : 0f;
            float height = dt.GetField("height", ChuteFlags)?.GetValue(def) is float h ? h : 0f;
            if (length == 0f || width == 0f || height == 0f) return;
            if (dt.GetField("spawnOffset", ChuteFlags)?.GetValue(def) is Vector3 offset)
                system.transform.localPosition = -offset;
            frame.transform.localScale = new Vector3(width * 1.1f / dims.x, height * 1.1f / dims.y, length * 1.1f / dims.z);
            var scale = frame.transform.localScale;
            if (system.GetType().GetField("listParachutes", ChuteFlags)?.GetValue(system) is System.Collections.IList list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] is not Component chute) continue;
                    chute.transform.localScale = new Vector3(
                        scale.x != 0f ? 1f / scale.x : 1f,
                        scale.y != 0f ? 1f / scale.y : 1f,
                        scale.z != 0f ? 1f / scale.z : 1f);
                    if (chute.gameObject != gameObject) chute.gameObject.SetActive(true);
                }
            }
        }

        static void MuteChuteScripts(GameObject go)
        {
            var all = go.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var beh = all[i];
                if (beh == null || beh is ReplayGhost) continue;
                string n = beh.GetType().Name;
                if (n == "Parachute" || n == "CargoDeploymentSystem")
                    beh.enabled = false;
            }
        }

        void LogChuteMeshes(string when)
        {
            string unitName = Track != null && !string.IsNullOrEmpty(Track.Name) ? Track.Name : name;
            if (_chutes.Length == 0)
            {
                Plugin.Log?.LogInfo("Chute mesh " + unitName + " " + when + " canopy missing");
                return;
            }
            for (int i = 0; i < _chutes.Length; i++)
            {
                LogChuteMesh(unitName, when, "canopy", _chutes[i].Canopy);
                LogChuteMesh(unitName, when, "lines", _chutes[i].Lines);
            }
        }

        void LogChuteMesh(string unitName, string when, string slot, GameObject? go)
        {
            if (go == null)
            {
                Plugin.Log?.LogInfo("Chute mesh " + unitName + " " + when + " " + slot + " missing");
                return;
            }
            var r = go.GetComponentInChildren<Renderer>(true);
            string en = "-";
            string shader = "-";
            string touch = "none";
            string prefabOff = "-";
            if (r != null)
            {
                en = r.enabled ? "1" : "0";
                prefabOff = _disabledInPrefab.Contains(r) ? "1" : "0";
                if (r.sharedMaterial != null && r.sharedMaterial.shader != null)
                    shader = r.sharedMaterial.shader.name;
                _rendTouch.TryGetValue(r.GetInstanceID(), out touch);
                if (string.IsNullOrEmpty(touch)) touch = "none";
            }
            Plugin.Log?.LogInfo("Chute mesh " + unitName + " " + when + " " + slot
                + " go=" + go.name
                + " active=" + (go.activeInHierarchy ? 1 : 0)
                + " en=" + en
                + " shader=" + shader
                + " scale=" + go.transform.localScale.ToString("0.##")
                + " touch=" + touch
                + " prefabOff=" + prefabOff);
        }
    }
}
