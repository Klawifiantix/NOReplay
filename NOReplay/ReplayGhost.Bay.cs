using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        struct BayHinge
        {
            public string Key;
            public Transform Leaf;
            public bool Ramp;
            public Component Source;
            public Vector3 Rest;
        }

        struct BayLoad
        {
            public string Key;
            public GameObject Body;
            public Vector3 Rest;
        }

        struct BayGlass
        {
            public string Key;
            public Transform Leaf;
            public Transform Parent;
            public Vector3 LocalPos;
            public Quaternion LocalRot;
            public AudioClip Clip;
            public AudioSource Audio;
            public bool Loosed;
        }

        BayHinge[] _hinges = System.Array.Empty<BayHinge>();
        BayLoad[] _loads = System.Array.Empty<BayLoad>();
        BayGlass[] _glass = System.Array.Empty<BayGlass>();
        bool _bayBound;
        bool _bayNone;
        bool _bayLogged;
        float _bayWait = -1f;
        float _bayPrev = -1f;
        static readonly BindingFlags BayFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        void DriveBay()
        {
            if (Track == null || _bayNone) return;
            if (!_bayBound) BindBay();
            if (_bayNone) return;
            float time = Plugin.Clock.Time;
            for (int i = 0; i < _hinges.Length; i++)
            {
                if (!Track.TryDoor(_hinges[i].Key, time, out var view) || !view.HasPose) continue;
                var leaf = _hinges[i].Leaf;
                if (leaf == null) continue;
                var e = _hinges[i].Rest;
                if (_hinges[i].Ramp) e.x = view.Angle;
                else e.z = view.Angle;
                leaf.localEulerAngles = e;
                if (_hinges[i].Ramp) AimStruts(_hinges[i].Source);
            }
            for (int i = 0; i < _loads.Length; i++)
            {
                if (!Track.TryDoor(_loads[i].Key, time, out var view)) continue;
                var body = _loads[i].Body;
                if (body == null) continue;
                if (!view.On)
                {
                    if (body.activeSelf) body.SetActive(false);
                    continue;
                }
                if (!body.activeSelf) body.SetActive(true);
                if (view.HasPose)
                    body.transform.localPosition = new Vector3(view.X, view.Y, view.Z);
            }
        }

        void DriveGlass()
        {
            if (Track == null || !Track.HasGlass) return;
            if (!_bayBound) BindBay();
            float time = Plugin.Clock.Time;
            float dt = _bayPrev >= 0f ? time - _bayPrev : 0f;
            bool had = _bayPrev >= 0f;
            _bayPrev = time;
            bool paused = Plugin.Clock.Paused;
            for (int i = 0; i < _glass.Length; i++)
            {
                var g = _glass[i];
                if (g.Leaf == null) continue;
                float start = Track.GlassStart(g.Key);
                bool known = Track.TryGlass(g.Key, time, out var view);
                if (start < 0f || time < start)
                {
                    RestoreGlass(ref g);
                }
                else if (known && view.On && view.HasPose)
                {
                    if (g.Leaf.parent != null)
                    {
                        g.Leaf.SetParent(null, true);
                        g.Loosed = true;
                    }
                    var p = Plugin.GlobalToLocal(view.X, view.Y, view.Z);
                    g.Leaf.SetPositionAndRotation(p, new Quaternion(view.Qx, view.Qy, view.Qz, view.Qw));
                    if (!g.Leaf.gameObject.activeSelf) g.Leaf.gameObject.SetActive(true);
                    if (had && Track.GlassCrossed(g.Key, time - dt, time, dt, paused))
                        PlayGlass(ref g);
                }
                else if (g.Loosed)
                {
                    if (g.Leaf.gameObject.activeSelf) g.Leaf.gameObject.SetActive(false);
                }
                HoldGlassAudio(ref g, paused);
                _glass[i] = g;
            }
        }

        void BindBay()
        {
            bool doors = Track != null && Track.HasDoors;
            bool glass = Track != null && Track.HasGlass;
            if (!doors && !glass)
            {
                _bayNone = true;
                _bayBound = true;
                return;
            }
            var hinges = new List<BayHinge>();
            var loads = new List<BayLoad>();
            var glassBits = new List<BayGlass>();
            var rampType = Plugin.FindGameType("CargoRamp");
            var doorType = Plugin.FindGameType("BayDoor");
            var cargoType = Plugin.FindGameType("MountedCargo");
            var canopyType = Plugin.FindGameType("Canopy");
            if (doorType != null)
            {
                var all = GetComponentsInChildren(doorType, true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] is not Component door) continue;
                    bool ramp = rampType != null && rampType.IsInstanceOfType(door);
                    Transform leaf = door.transform;
                    if (ramp)
                    {
                        var part = door.GetType().GetField("rampPart", BayFlags)?.GetValue(door) as Component;
                        if (part != null) leaf = part.transform;
                    }
                    if (door is Behaviour beh) beh.enabled = false;
                    hinges.Add(new BayHinge
                    {
                        Key = (ramp ? "r/" : "b/") + BayPath(door.transform),
                        Leaf = leaf,
                        Ramp = ramp,
                        Source = door,
                        Rest = leaf.localEulerAngles
                    });
                }
            }
            if (cargoType != null)
            {
                var all = GetComponentsInChildren(cargoType, true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] is not Component cargo) continue;
                    loads.Add(new BayLoad
                    {
                        Key = "m/" + BayPath(cargo.transform),
                        Body = cargo.gameObject,
                        Rest = cargo.transform.localPosition
                    });
                }
            }
            if (canopyType != null)
            {
                var all = GetComponentsInChildren(canopyType, true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] is not Component canopy) continue;
                    var thrown = canopy.GetType().GetField("ejectionTransform", BayFlags)?.GetValue(canopy) as Transform;
                    if (thrown == null) continue;
                    if (canopy is Behaviour beh) beh.enabled = false;
                    glassBits.Add(new BayGlass
                    {
                        Key = "g/" + BayPath(canopy.transform),
                        Leaf = thrown,
                        Parent = thrown.parent,
                        LocalPos = thrown.localPosition,
                        LocalRot = thrown.localRotation,
                        Clip = canopy.GetType().GetField("ejectSound", BayFlags)?.GetValue(canopy) as AudioClip
                    });
                }
            }
            _hinges = hinges.ToArray();
            _loads = loads.ToArray();
            _glass = glassBits.ToArray();
            bool wantMount = false;
            if (Track != null)
            {
                foreach (var key in Track.DoorKeys())
                {
                    if (!string.IsNullOrEmpty(key) && key[0] == 'm') { wantMount = true; break; }
                }
            }
            if (wantMount && _loads.Length == 0)
            {
                if (_bayWait < 0f) _bayWait = Time.unscaledTime + 8f;
                if (Time.unscaledTime < _bayWait)
                {
                    _bayBound = false;
                    return;
                }
            }
            if (!_bayLogged && Track != null)
            {
                _bayLogged = true;
                string unitName = !string.IsNullOrEmpty(Track.Name) ? Track.Name : name;
                Plugin.Log?.LogInfo("Ramp play " + unitName
                    + " samples=" + Track.DoorCount()
                    + " hinges=" + _hinges.Length
                    + " mounts=" + _loads.Length
                    + " glass=" + Track.GlassCount()
                    + " panes=" + _glass.Length);
            }
            _bayBound = true;
        }

        void AimStruts(Component ramp)
        {
            if (ramp.GetType().GetField("struts", BayFlags)?.GetValue(ramp) is not System.Array struts) return;
            for (int i = 0; i < struts.Length; i++)
            {
                object strut = struts.GetValue(i);
                if (strut == null) continue;
                var ft = strut.GetType();
                var tr = ft.GetField("transform", BayFlags)?.GetValue(strut) as Transform;
                var target = ft.GetField("target", BayFlags)?.GetValue(strut) as Transform;
                if (tr == null || target == null) continue;
                tr.LookAt(target);
                float dist = Vector3.Distance(target.position, tr.position);
                tr.localScale = new Vector3(1f, 1f, dist);
                if (ft.GetField("maxLength", BayFlags)?.GetValue(strut) is float max && dist > max)
                {
                    var rend = tr.GetComponent<Renderer>();
                    if (rend != null) rend.enabled = false;
                }
            }
        }

        static void RestoreGlass(ref BayGlass g)
        {
            if (g.Leaf == null) return;
            if (g.Loosed && g.Parent != null)
                g.Leaf.SetParent(g.Parent, false);
            g.Leaf.localPosition = g.LocalPos;
            g.Leaf.localRotation = g.LocalRot;
            if (!g.Leaf.gameObject.activeSelf) g.Leaf.gameObject.SetActive(true);
            g.Loosed = false;
        }

        void PlayGlass(ref BayGlass g)
        {
            if (g.Clip == null || g.Leaf == null) return;
            if (g.Audio == null)
            {
                g.Audio = g.Leaf.gameObject.AddComponent<AudioSource>();
                g.Audio.clip = g.Clip;
                g.Audio.volume = 2f;
                g.Audio.spatialBlend = 1f;
                g.Audio.dopplerLevel = 0f;
                g.Audio.minDistance = 50f;
                g.Audio.maxDistance = 1000f;
                g.Audio.playOnAwake = false;
                g.Audio.rolloffMode = AudioRolloffMode.Linear;
            }
            if (!g.Audio.isPlaying) g.Audio.Play();
        }

        static void HoldGlassAudio(ref BayGlass g, bool paused)
        {
            if (g.Audio == null) return;
            if (paused)
            {
                if (g.Audio.isPlaying) g.Audio.Pause();
                return;
            }
            if (!g.Audio.isPlaying && g.Audio.time > 0f) g.Audio.UnPause();
        }

        string BayPath(Transform t)
        {
            string path = TurretKeys.Sanitize(TurretKeys.Relative(transform, t));
            return path.Length == 0 ? "door" : path;
        }
    }
}
