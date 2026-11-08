using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Moves a building wreck's fragments from the replay clock. Speeds and
    /// dust settings are the prefab's own fields. Nothing here is simulated
    /// by a live rigidbody.
    /// </summary>
    internal sealed class ReplayWreckMotion : MonoBehaviour
    {
        public float Born;
        struct Piece
        {
            public Transform Tr;
            public Vector3 P0;
            public Quaternion R0;
            public float GroundY;
            public WreckThrow.Throw Throw;
        }

        struct Dust
        {
            public Transform Emit;
            public string Type;
            public float Duration;
            public float Interval;
            public float Size;
            public float Life;
            public float Speed;
            public float Radius;
            public int Sent;
            public int SystemId;
        }

        Piece[] _pieces = Array.Empty<Piece>();
        Dust[] _dust = Array.Empty<Dust>();
        Transform? _main;
        float _mainTime;
        float _mainRise;
        Vector3 _mainFrom;
        Vector3 _mainTo;
        bool _haveMain;
        Transform? _collapser;
        Quaternion _collapserRot;
        float _toppleSpeed;
        Vector3 _toppleAxis;
        float _prevAge = -1f;
        bool _dustMiss;
        static bool _speedLogged;

        public void Arm(Component manager, int seed)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = manager.GetType();
            float speed = t.GetField("ejectionSpeed", flags)?.GetValue(manager) is float sp ? sp : float.NaN;
            if (!_speedLogged)
            {
                _speedLogged = true;
                Plugin.Log?.LogInfo("Wreck ejectionSpeed=" + (float.IsNaN(speed) ? "missing" : speed.ToString("0.##")));
            }
            if (float.IsNaN(speed)) speed = 0f;
            var list = t.GetField("fragments", flags)?.GetValue(manager) as IList;
            var pieces = new List<Piece>();
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var item = list[i];
                    if (item == null) continue;
                    var rb = item.GetType().GetField("rb", flags)?.GetValue(item) as Rigidbody;
                    if (rb == null) continue;
                    var tr = rb.transform;
                    float ground = tr.position.y;
                    if (Physics.Raycast(tr.position + Vector3.up * 2f, Vector3.down, out var hit, 80f, ~0, QueryTriggerInteraction.Ignore))
                        ground = hit.point.y;
                    pieces.Add(new Piece
                    {
                        Tr = tr,
                        P0 = tr.position,
                        R0 = tr.rotation,
                        GroundY = ground,
                        Throw = WreckThrow.Seed(seed, i, speed)
                    });
                    rb.isKinematic = true;
                    rb.detectCollisions = false;
                    rb.useGravity = false;
                }
            }
            _pieces = pieces.ToArray();
            ReadMain(manager, flags, t);
            ReadDust(manager, flags, t);
            Plugin.Log?.LogInfo("Wreck pieces=" + _pieces.Length + " dust=" + _dust.Length
                + " " + gameObject.name);
        }

        void ReadMain(Component manager, BindingFlags flags, Type t)
        {
            _main = t.GetField("mainDebris", flags)?.GetValue(manager) as Transform;
            _collapser = t.GetField("mainCollapser", flags)?.GetValue(manager) as Transform;
            _mainTime = t.GetField("mainDebrisAppearanceTime", flags)?.GetValue(manager) is float mt && mt > 0.05f ? mt : 0f;
            _mainRise = t.GetField("mainDebrisRiseAmount", flags)?.GetValue(manager) is float rise ? rise : 0f;
            _toppleSpeed = t.GetField("toppleSpeed", flags)?.GetValue(manager) is float ts ? ts : 0f;
            if (_main != null && _mainTime > 0f)
            {
                _mainTo = _main.position;
                _mainFrom = _mainTo - Vector3.up * _mainRise;
                _main.position = _mainFrom;
                _haveMain = true;
            }
            if (_collapser != null)
            {
                _collapserRot = _collapser.rotation;
                var spin = WreckThrow.Seed(manager.GetInstanceID(), 99, 1f);
                _toppleAxis = new Vector3(spin.Ax, 0f, spin.Az);
                if (_toppleAxis.sqrMagnitude < 0.01f) _toppleAxis = Vector3.right;
                _toppleAxis.Normalize();
            }
        }

        void ReadDust(Component manager, BindingFlags flags, Type t)
        {
            var list = t.GetField("collapseDust", flags)?.GetValue(manager) as IList;
            if (list == null) return;
            var dust = new List<Dust>();
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item == null) continue;
                var it = item.GetType();
                var emit = it.GetField("emitTransform", flags)?.GetValue(item) as Transform;
                var kind = it.GetField("effectType", flags)?.GetValue(item) as string ?? "";
                if (emit == null || kind.Length == 0) continue;
                if (!TryField(it, item, "duration", flags, out float duration)) continue;
                if (!TryField(it, item, "emitInterval", flags, out float interval)) continue;
                dust.Add(new Dust
                {
                    Emit = emit,
                    Type = kind,
                    Duration = duration,
                    Interval = interval > 0.02f ? interval : 0.02f,
                    Size = TryField(it, item, "size", flags, out float size) ? size : 1f,
                    Life = TryField(it, item, "life", flags, out float life) ? life : 1f,
                    Speed = TryField(it, item, "emitSpeed", flags, out float spd) ? spd : 0f,
                    Radius = TryField(it, item, "flatRadius", flags, out float radius) ? radius : 0f,
                    SystemId = -1
                });
            }
            _dust = dust.ToArray();
        }

        static bool TryField(Type t, object item, string name, BindingFlags flags, out float v)
        {
            v = 0f;
            if (t.GetField(name, flags)?.GetValue(item) is float n)
            {
                v = n;
                return true;
            }
            return false;
        }

        void Update()
        {
            if (Plugin.CurrentDoc == null) return;
            float now = Plugin.Clock != null ? Plugin.Clock.Time : 0f;
            float age = now - Born;
            if (age < 0f) age = 0f;
            bool paused = Plugin.Clock != null && Plugin.Clock.Paused;
            for (int i = 0; i < _pieces.Length; i++)
            {
                var p = _pieces[i];
                if (p.Tr == null) continue;
                WreckThrow.Place(age, p.P0.x, p.P0.y, p.P0.z, p.Throw, p.GroundY, out float x, out float y, out float z);
                p.Tr.position = new Vector3(x, y, z);
                float spinAge = age;
                float hit = WreckThrow.ImpactTime(p.P0.y, p.Throw.Vy, p.GroundY);
                if (spinAge > hit) spinAge = hit;
                p.Tr.rotation = Quaternion.AngleAxis(p.Throw.Spin * spinAge * Mathf.Rad2Deg, new Vector3(p.Throw.Ax, p.Throw.Ay, p.Throw.Az)) * p.R0;
            }
            if (_haveMain && _main != null && _mainTime > 0f)
            {
                float u = age / _mainTime;
                if (u < 0f) u = 0f;
                if (u > 1f) u = 1f;
                _main.position = Vector3.Lerp(_mainFrom, _mainTo, u);
            }
            if (_collapser != null && _toppleSpeed != 0f && age > 0f)
                _collapser.rotation = Quaternion.AngleAxis(_toppleSpeed * age, _toppleAxis) * _collapserRot;
            if (!paused)
                EmitDust(age);
            _prevAge = age;
        }

        void EmitDust(float age)
        {
            bool forward = _prevAge >= 0f && age > _prevAge && age - _prevAge < 0.5f;
            if (!forward) return;
            for (int i = 0; i < _dust.Length; i++)
            {
                var d = _dust[i];
                if (d.Emit == null || age > d.Duration) continue;
                int want = (int)(age / d.Interval);
                int add = want - d.Sent;
                if (add <= 0) continue;
                if (add > 4) add = 4;
                d.Sent = want;
                _dust[i] = d;
                try
                {
                    var pemT = Plugin.FindGameType("ParticleEffectManager");
                    if (pemT == null) return;
                    object? pem = pemT.GetProperty("i", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)?.GetValue(null);
                    if (pem == null) return;
                    if (d.SystemId < 0)
                    {
                        d.SystemId = pemT.GetMethod("GetSystemID")?.Invoke(pem, new object[] { d.Type }) is int id ? id : -1;
                        _dust[i] = d;
                        if (d.SystemId < 0 && !_dustMiss)
                        {
                            _dustMiss = true;
                            Plugin.Log?.LogInfo("Wreck dust missing " + d.Type);
                        }
                    }
                    if (d.SystemId < 0) continue;
                    var emit = pemT.GetMethod("EmitParticles");
                    if (emit == null) continue;
                    Vector3 at = d.Emit.position;
                    var origin = at.ToGlobalPosition();
                    emit.Invoke(pem, new object[]
                    {
                        d.SystemId, add, origin, Vector3.up * d.Speed,
                        d.Radius, d.Life, 0.3f, d.Size, 0.3f, 0.3f, 1f, 0.2f
                    });
                }
                catch (Exception ex)
                {
                    if (!_dustMiss)
                    {
                        _dustMiss = true;
                        Plugin.Log?.LogWarning("Wreck dust: " + ex.Message);
                    }
                }
            }
        }
    }
}
