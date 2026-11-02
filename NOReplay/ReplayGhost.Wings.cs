using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    public partial class ReplayGhost
    {
        // Missile.UnfoldFins is what swings foldingFins from foldAngle to deployAngle.
        // DisableMissileSim and ShowMissile turn the Missile behaviour off, so Seek never
        // calls DeployFins and the async open never starts. The pose is written here from
        // replay time, including after a seek reuses the same ghost.

        struct WingFin
        {
            public Transform Fin;
            public Vector3 Fold;
            public Vector3 Open;
            public float Speed;
            public string Path;
        }

        static bool _wingRefl;
        static bool _wingReflWarned;
        static FieldInfo? _fFoldingFins;
        static FieldInfo? _fFinTransform;
        static FieldInfo? _fFinFold;
        static FieldInfo? _fFinOpen;
        static FieldInfo? _fFinSpeed;
        static System.Type? _wingMissileType;
        static System.Type? _wingSeekerType;
        static readonly Dictionary<System.Type, FieldInfo?> _wingDelayFields = new Dictionary<System.Type, FieldInfo?>();
        static readonly HashSet<string> _wingTypeLogged = new HashSet<string>();

        WingFin[] _wings = System.Array.Empty<WingFin>();
        bool _wingsReady;
        int _wingHost;
        float _wingDelay;
        string _wingTrack = "";
        bool _wingDeployLogged;
        bool _wingSkip;

        static void BindWingRefl()
        {
            if (_wingRefl) return;
            _wingRefl = true;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _wingMissileType = Plugin.FindGameType("Missile");
            _wingSeekerType = Plugin.FindGameType("MissileSeeker");
            if (_wingMissileType == null)
            {
                _wingRefl = false;
                return;
            }
            _fFoldingFins = _wingMissileType.GetField("foldingFins", flags);
            var finType = _wingMissileType.GetNestedType("FoldingFin", BindingFlags.Public | BindingFlags.NonPublic);
            if (finType != null)
            {
                _fFinTransform = finType.GetField("fin", flags);
                _fFinFold = finType.GetField("foldAngle", flags);
                _fFinOpen = finType.GetField("deployAngle", flags);
                _fFinSpeed = finType.GetField("deploySpeed", flags);
            }
            if ((_fFoldingFins == null || _fFinTransform == null) && !_wingReflWarned)
            {
                _wingReflWarned = true;
                Plugin.Log?.LogWarning("Wing reflection incomplete. foldingFins="
                    + (_fFoldingFins != null) + " fin=" + (_fFinTransform != null));
            }
        }

        static FieldInfo? DelayField(System.Type seekerType, string fieldName)
        {
            if (_wingDelayFields.TryGetValue(seekerType, out var cached)) return cached;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var field = seekerType.GetField(fieldName, flags);
            _wingDelayFields[seekerType] = field;
            return field;
        }

        void EnsureWings()
        {
            if (_wingsReady) return;
            BindWingRefl();
            if (_wingMissileType == null || _fFoldingFins == null) return;
            var missile = GetComponent(_wingMissileType) ?? GetComponentInChildren(_wingMissileType, true);
            if (missile == null) return;
            var host = missile as Component;
            int id = host != null ? host.GetInstanceID() : 0;
            if (_wingsReady && id == _wingHost) return;
            _wingsReady = true;
            _wingHost = id;
            _wingDeployLogged = false;
            _wingSkip = false;
            _wings = System.Array.Empty<WingFin>();
            _wingDelay = 0f;

            var seeker = _wingSeekerType != null
                ? (GetComponent(_wingSeekerType) ?? GetComponentInChildren(_wingSeekerType, true))
                : null;
            string seekerName = seeker != null ? seeker.GetType().Name : "";
            var kind = FinDeploy.KindFor(seekerName);
            if (kind == FinDeploy.Kind.None)
            {
                _wingSkip = true;
                return;
            }
            if (kind == FinDeploy.Kind.FinDelay)
                _wingDelay = ReadSeekerDelay(seeker, "finDelay", seekerName);
            else if (kind == FinDeploy.Kind.Guidance)
                _wingDelay = ReadSeekerDelay(seeker, "guidanceDelay", seekerName);

            Array? raw = null;
            try { raw = _fFoldingFins.GetValue(missile) as Array; }
            catch { raw = null; }
            if (raw == null || raw.Length == 0) return;

            var root = host != null ? host.transform : transform;
            var list = new List<WingFin>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                var item = raw.GetValue(i);
                if (item == null || _fFinTransform == null) continue;
                Transform? fin = null;
                try { fin = _fFinTransform.GetValue(item) as Transform; }
                catch { fin = null; }
                if (fin == null) continue;
                Vector3 fold = Vector3.zero;
                Vector3 open = Vector3.zero;
                float speed = 1f;
                try { if (_fFinFold?.GetValue(item) is Vector3 a) fold = a; } catch { }
                try { if (_fFinOpen?.GetValue(item) is Vector3 b) open = b; } catch { }
                try { if (_fFinSpeed?.GetValue(item) is float s) speed = s; } catch { }
                list.Add(new WingFin
                {
                    Fin = fin,
                    Fold = fold,
                    Open = open,
                    Speed = speed,
                    Path = WingPath(fin, root),
                });
            }
            _wings = list.ToArray();
            if (_wings.Length == 0) return;
            LogWingType(seekerName.Length > 0 ? seekerName : "-");
        }

        static float ReadSeekerDelay(object? seeker, string fieldName, string seekerName)
        {
            if (seeker == null) return FinDeploy.FallbackDelay(seekerName);
            var field = DelayField(seeker.GetType(), fieldName);
            try
            {
                if (field?.GetValue(seeker) is float v) return v;
            }
            catch { }
            return FinDeploy.FallbackDelay(seekerName);
        }

        static string WingPath(Transform fin, Transform root)
        {
            var parts = new List<string>(4);
            var t = fin;
            while (t != null && t != root)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            if (parts.Count == 0) return fin.name;
            parts.Reverse();
            return string.Join("/", parts);
        }

        string WingLabel()
        {
            string n = Track?.Name ?? "";
            if (n.Length == 0) n = gameObject.name ?? "";
            const string prefix = "NOReplay_";
            if (n.StartsWith(prefix, StringComparison.Ordinal)) n = n.Substring(prefix.Length);
            return n.Length > 0 ? n : "?";
        }

        void LogWingType(string seekerName)
        {
            string label = WingLabel();
            string key = label + "|" + seekerName;
            if (!_wingTypeLogged.Add(key)) return;
            var names = new System.Text.StringBuilder();
            for (int i = 0; i < _wings.Length; i++)
            {
                if (i > 0) names.Append(',');
                names.Append(_wings[i].Path);
                names.Append(" @").Append(_wings[i].Speed.ToString("0.##"));
            }
            Plugin.Log?.LogInfo("Wings type " + label
                + " seeker=" + seekerName
                + " delay=" + _wingDelay.ToString("0.00")
                + " fins=" + names);
        }

        void TickWings(float time)
        {
            if (!_ordnance || IsFlareTrack()) return;
            EnsureWings();
            if (_wingSkip || _wings.Length == 0) return;
            string id = Track?.Id ?? "";
            if (!string.Equals(id, _wingTrack, StringComparison.Ordinal))
            {
                _wingTrack = id;
                _wingDeployLogged = false;
            }
            float age = time - OrdnanceBorn();
            float peak = 0f;
            for (int i = 0; i < _wings.Length; i++)
            {
                var fin = _wings[i];
                if (fin.Fin == null) continue;
                float amount = FinDeploy.Amount(age, _wingDelay, fin.Speed);
                if (amount > peak) peak = amount;
                FinDeploy.LerpEuler(
                    fin.Fold.x, fin.Fold.y, fin.Fold.z,
                    fin.Open.x, fin.Open.y, fin.Open.z,
                    amount,
                    out float x, out float y, out float z);
                fin.Fin.localEulerAngles = new Vector3(x, y, z);
            }
            if (peak <= 0f)
            {
                _wingDeployLogged = false;
                return;
            }
            if (_wingDeployLogged) return;
            _wingDeployLogged = true;
            Plugin.Log?.LogInfo("Wings deploy " + WingLabel() + " t=" + time.ToString("0.00"));
        }
    }
}
