using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Rounds already fired when the shooter dies. BulletSim is created under the
    /// world origin and only moves in FixedUpdate. Disabling that component, which
    /// is what happens on death and on pause, leaves the tracers where they are.
    /// Adopted sims stay disabled so FixedUpdate cannot run during a pause or
    /// apply damage on its own. This steps them with the replay clock, visual only.
    /// </summary>
    static class ReplayRounds
    {
        const float Step = 0.02f;
        const int MaxSteps = 8;

        struct Held
        {
            public BulletSim Sim;
        }

        static readonly List<Held> _held = new List<Held>();
        static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static FieldInfo? _bullets, _info, _owner, _impacts, _tracerSize, _visualOnly;
        static MethodInfo? _trace;
        static bool _bound;
        static float _lastTime = -1f;

        public static void Adopt(BulletSim sim)
        {
            if (sim == null) return;
            Bind();
            try { _visualOnly?.SetValue(sim, true); } catch { }
            sim.enabled = false;
            for (int i = 0; i < _held.Count; i++)
                if (_held[i].Sim == sim) return;
            _held.Add(new Held { Sim = sim });
        }

        public static void Clear()
        {
            for (int i = 0; i < _held.Count; i++)
                DestroyHeld(_held[i].Sim);
            _held.Clear();
            _lastTime = -1f;
        }

        public static void Tick(float time, bool paused)
        {
            if (_held.Count == 0)
            {
                _lastTime = time;
                return;
            }
            bool jump = _lastTime >= 0f && (time < _lastTime - 0.02f || time > _lastTime + 0.35f);
            if (jump || paused)
            {
                if (jump) Clear();
                _lastTime = time;
                return;
            }
            float dt = _lastTime >= 0f ? time - _lastTime : 0f;
            _lastTime = time;
            if (dt <= 0.0001f) return;
            int steps = (int)(dt / Step);
            if (steps < 1) steps = 1;
            if (steps > MaxSteps) steps = MaxSteps;
            float slice = dt / steps;
            if (slice > 0.05f) slice = 0.05f;
            Bind();
            var cam = Plugin.MainCamera();
            bool hasCam = cam != null;
            GlobalPosition camPos = default;
            float fov = 60f;
            if (hasCam)
            {
                camPos = cam.transform.position.ToGlobalPosition();
                fov = cam.fieldOfView;
            }
            for (int i = _held.Count - 1; i >= 0; i--)
            {
                var held = _held[i];
                if (held.Sim == null)
                {
                    _held.RemoveAt(i);
                    continue;
                }
                int alive = StepSim(held.Sim, slice, steps, hasCam, camPos, fov);
                if (alive <= 0)
                {
                    DestroyHeld(held.Sim);
                    _held.RemoveAt(i);
                }
            }
        }

        static int StepSim(BulletSim sim, float slice, int steps, bool hasCam, GlobalPosition camPos, float fov)
        {
            if (_bullets?.GetValue(sim) is not IList list) return 0;
            object? info = _info?.GetValue(sim);
            object? owner = _owner?.GetValue(sim);
            object? impacts = _impacts?.GetValue(sim);
            float tracerSize = _tracerSize?.GetValue(sim) is float sz ? sz : 1f;
            int alive = 0;
            for (int n = 0; n < steps; n++)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i] is not BulletSim.Bullet bullet) continue;
                    if (!bullet.active)
                    {
                        bullet.Remove();
                        list.RemoveAt(i);
                        continue;
                    }
                    try
                    {
                        _trace?.Invoke(bullet, new object[]
                        {
                            null, info, owner, impacts, tracerSize, true, slice,
                            hasCam, camPos, Vector3.zero, fov, false, default(GlobalPosition)
                        });
                    }
                    catch
                    {
                        bullet.active = false;
                    }
                    if (!bullet.active)
                    {
                        bullet.Remove();
                        list.RemoveAt(i);
                    }
                }
            }
            for (int i = 0; i < list.Count; i++)
                if (list[i] is BulletSim.Bullet b && b.active) alive++;
            return alive;
        }

        static void DestroyHeld(BulletSim? sim)
        {
            if (sim == null) return;
            try
            {
                if (_bullets?.GetValue(sim) is IList list)
                {
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        if (list[i] is BulletSim.Bullet bullet) bullet.Remove();
                    }
                    list.Clear();
                }
            }
            catch { }
            UnityEngine.Object.Destroy(sim.gameObject);
        }

        static void Bind()
        {
            if (_bound) return;
            _bound = true;
            var sim = typeof(BulletSim);
            _bullets = sim.GetField("bullets", Flags);
            _info = sim.GetField("weaponInfo", Flags);
            _owner = sim.GetField("owner", Flags);
            _impacts = sim.GetField("impactEffects", Flags);
            _tracerSize = sim.GetField("tracerSize", Flags);
            _visualOnly = sim.GetField("visualOnly", Flags);
            _trace = typeof(BulletSim.Bullet).GetMethod("TrajectoryTrace", Flags);
        }
    }
}
