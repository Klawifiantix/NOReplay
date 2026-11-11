using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Lets MushroomCloud and Shockwave run their own Update, with the replay
    /// clock substituted for Time.deltaTime and the wind forced to zero.
    /// Shockwave's damage list is cleared, and Update is not allowed to
    /// destroy the vapor or the component, so a seek can rewind the curves.
    /// </summary>
    internal static class NukeTime
    {
        static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        static readonly Dictionary<int, float> Born = new();
        static readonly Dictionary<int, float> LastAge = new();
        static readonly HashSet<int> KeepRoots = new();
        static readonly Dictionary<int, Vector3> CapBase = new();
        static readonly Dictionary<int, RiseSample> CapRise = new();
        static FieldInfo? _cloudField;
        static FieldInfo? _meshField;
        static FieldInfo? _riseCurveField;
        static bool _installed;
        static int _holdPieces;
        static bool _cloudHeld;
        static bool _waveHeld;
        static FieldInfo? _influenced;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            int n = 0;
            n += Patch("MushroomCloud", "Update", nameof(CloudIn), nameof(CloudOut));
            n += Patch("Shockwave", "Update", nameof(WaveIn), nameof(WaveOut));
            n += Patch("Shockwave", "Start", null, nameof(WaveStarted));
            n += Patch("BlastManager", "AddBlast", nameof(SkipMapScorch), null);
            var level = Plugin.FindGameType("LevelInfo");
            if (level != null)
            {
                foreach (var method in level.GetMethods(Flags))
                {
                    if (method.Name != "GetWind") continue;
                    try
                    {
                        Plugin.Harmony.Patch(method, prefix: new HarmonyMethod(typeof(NukeTime), nameof(ZeroWind)));
                        n++;
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log?.LogInfo("Nuke wind " + ex.Message);
                    }
                }
            }
            var destroyType = typeof(UnityEngine.Object);
            n += PatchDestroy(destroyType.GetMethod("Destroy", new[] { typeof(UnityEngine.Object) }));
            n += PatchDestroy(destroyType.GetMethod("Destroy", new[] { typeof(UnityEngine.Object), typeof(float) }));
            Plugin.Log?.LogInfo("Nuke clock hooks=" + n);
        }

        static int Patch(string typeName, string method, string? prefix, string? postfix)
        {
            var type = Plugin.FindGameType(typeName);
            MethodInfo? found = null;
            if (type != null)
            {
                foreach (var candidate in type.GetMethods(Flags))
                {
                    if (candidate.Name != method) continue;
                    if (candidate.GetParameters().Length == 0)
                    {
                        found = candidate;
                        break;
                    }
                    found ??= candidate;
                }
            }
            if (found == null)
            {
                Plugin.Log?.LogInfo("Nuke missing " + typeName + "." + method);
                return 0;
            }
            try
            {
                Plugin.Harmony.Patch(found,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(NukeTime), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(NukeTime), postfix));
                return 1;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogInfo("Nuke " + typeName + "." + method + " " + ex.Message);
                return 0;
            }
        }

        static int PatchDestroy(MethodInfo? method)
        {
            if (method == null) return 0;
            try
            {
                Plugin.Harmony.Patch(method, prefix: new HarmonyMethod(typeof(NukeTime), nameof(KeepPiece)));
                return 1;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogInfo("Nuke destroy " + ex.Message);
                return 0;
            }
        }

        public static void Watch(GameObject root, float born)
        {
            if (root == null) return;
            Born[root.GetInstanceID()] = born;
        }

        public static void KeepPieces(GameObject root)
        {
            if (root == null) return;
            KeepRoots.Add(root.GetInstanceID());
        }

        public static void Forget(GameObject root)
        {
            if (root == null) return;
            int id = root.GetInstanceID();
            Born.Remove(id);
            KeepRoots.Remove(id);
            CapBase.Remove(id);
            CapRise.Remove(id);
        }

        public static void ApplyMissileYield(GameObject go, float yieldKg)
        {
            if (go == null || yieldKg <= 0f) return;
            float kt = NukeScale.Kilotons(yieldKg);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var type = mb.GetType();
                if (type.Name == "Shockwave")
                    type.GetField("yieldKilotons", Flags)?.SetValue(mb, kt);
                else if (type.Name == "MushroomCloud")
                    type.GetField("yield", Flags)?.SetValue(mb, yieldKg);
            }
        }

        public static string Describe(GameObject go, float age)
        {
            var sb = new StringBuilder();
            sb.Append("Nuke age=").Append(age.ToString("0.00"));
            if (go == null) return sb.ToString();
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (n == "Shockwave")
                {
                    float kt = mb.GetType().GetField("yieldKilotons", Flags)?.GetValue(mb) is float y ? y : 0f;
                    var vapor = mb.GetType().GetField("vaporCloud", Flags)?.GetValue(mb) as GameObject;
                    sb.Append(" kt=").Append(kt.ToString("0.###"));
                    if (vapor != null)
                    {
                        var s = vapor.transform.localScale;
                        var w = vapor.transform.lossyScale;
                        sb.Append(" vapor=").Append(s.x.ToString("0")).Append(',').Append(s.y.ToString("0")).Append(',').Append(s.z.ToString("0"));
                        sb.Append(" lossy=").Append(w.x.ToString("0")).Append(',').Append(w.y.ToString("0")).Append(',').Append(w.z.ToString("0"));
                    }
                }
                else if (n == "MushroomCloud")
                {
                    float kg = mb.GetType().GetField("yield", Flags)?.GetValue(mb) is float y ? y : 0f;
                    sb.Append(" cloudKg=").Append(kg.ToString("0"));
                    AppendChildScale(sb, mb.transform, "cap");
                    AppendChildScale(sb, mb.transform, "ring");
                }
            }
            return sb.ToString();
        }

        static void AppendChildScale(StringBuilder sb, Transform root, string token)
        {
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (t.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var s = t.localScale;
                    sb.Append(' ').Append(token).Append('=')
                        .Append(s.x.ToString("0")).Append(',').Append(s.y.ToString("0")).Append(',').Append(s.z.ToString("0"));
                    return;
                }
                for (int i = 0; i < t.childCount; i++)
                    stack.Push(t.GetChild(i));
            }
        }

        static bool TryAge(object instance, out float age)
        {
            age = 0f;
            if (Plugin.CurrentDoc == null || Plugin.Clock == null) return false;
            if (instance is not Component component) return false;
            Transform? t = component.transform;
            while (t != null)
            {
                if (Born.TryGetValue(t.gameObject.GetInstanceID(), out float born))
                {
                    float span = Plugin.Clock.Time - born;
                    age = span > 0f ? span : 0f;
                    return true;
                }
                t = t.parent;
            }
            return false;
        }

        static bool UnderWatch(GameObject go) => WatchedRoot(go) != 0;

        static int WatchedRoot(GameObject go)
        {
            Transform? t = go.transform;
            int found = 0;
            while (t != null)
            {
                int id = t.gameObject.GetInstanceID();
                if (Born.ContainsKey(id)) found = id;
                t = t.parent;
            }
            return found;
        }

        static void Revive(Component component)
        {
            var renderers = component.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && !renderers[i].enabled)
                    renderers[i].enabled = true;
            }
        }

        static void ClearInfluenced(object instance)
        {
            if (instance == null) return;
            _influenced ??= instance.GetType().GetField("influencedObjects", Flags);
            if (_influenced?.GetValue(instance) is IList list)
                list.Clear();
        }

        static void CloudIn(object __instance, ref float ___time)
        {
            _cloudHeld = false;
            if (!TryAge(__instance, out float age)) return;
            if (__instance is Component component)
            {
                int id = component.GetInstanceID();
                if (LastAge.TryGetValue(id, out float prev) && age < prev - 0.02f)
                    Revive(component);
                LastAge[id] = age;
                RememberCap(__instance, id);
            }
            ___time = NukeScale.BeforeStep(age, Time.deltaTime);
            _holdPieces++;
            _cloudHeld = true;
        }

        static void CloudOut(object __instance)
        {
            if (!_cloudHeld)
                return;
            try
            {
                if (__instance is Component component
                    && LastAge.TryGetValue(component.GetInstanceID(), out float age))
                    LiftCap(component, age);
            }
            catch { }
            _cloudHeld = false;
            if (_holdPieces > 0) _holdPieces--;
        }

        static void WaveIn(object __instance, ref float ___blastTime, ref float ___blastPropagation, ref float ___dustOpacity)
        {
            _waveHeld = false;
            if (!TryAge(__instance, out float age)) return;
            ClearInfluenced(__instance);
            float kt = 0.0001f;
            if (__instance is Component component)
            {
                if (component.GetType().GetField("yieldKilotons", Flags)?.GetValue(component) is float y && y > 0f)
                    kt = y;
                int id = component.GetInstanceID();
                if (LastAge.TryGetValue(id, out float prev) && age < prev - 0.02f)
                    Revive(component);
                LastAge[id] = age;
            }
            float dt = Time.deltaTime;
            ___blastTime = NukeScale.BeforeStep(age, dt);
            ___blastPropagation = NukeScale.BeforeStep(NukeScale.Propagation(kt, age), 340f * dt);
            float dust = NukeScale.DustOpacity(kt, age);
            ___dustOpacity = dust >= 1f ? dust : dust + dt * 0.1f;
            _holdPieces++;
            _waveHeld = true;
        }

        static void WaveOut()
        {
            if (!_waveHeld) return;
            _waveHeld = false;
            if (_holdPieces > 0) _holdPieces--;
        }

        static void WaveStarted(object __instance)
        {
            if (Plugin.CurrentDoc == null) return;
            if (__instance is not Component component) return;
            if (!UnderWatch(component.gameObject)) return;
            ClearInfluenced(__instance);
        }

        static bool SkipMapScorch()
        {
            return Plugin.CurrentDoc == null;
        }

        struct RiseSample
        {
            public float Age;
            public float Height;
            public bool Used;
        }

        static bool TryCap(object cloudComponent, out Component? renderer, out AnimationCurve? curve)
        {
            renderer = null;
            curve = null;
            var type = cloudComponent.GetType();
            _cloudField ??= type.GetField("cloud", Flags);
            object? cloud = _cloudField?.GetValue(cloudComponent);
            if (cloud == null) return false;
            var cloudType = cloud.GetType();
            _meshField ??= cloudType.GetField("meshRenderer", Flags);
            _riseCurveField ??= cloudType.GetField("riseRateOverTime", Flags);
            renderer = _meshField?.GetValue(cloud) as Component;
            curve = _riseCurveField?.GetValue(cloud) as AnimationCurve;
            return renderer != null && curve != null;
        }

        static void RememberCap(object cloudComponent, int id)
        {
            if (CapBase.ContainsKey(id)) return;
            if (!TryCap(cloudComponent, out var renderer, out _)) return;
            if (renderer == null) return;
            // Store the point in datum space. FloatingOrigin moves every root,
            // and a saved Unity position would stay next to the camera.
            CapBase[id] = DatumLocal(renderer.transform.position);
        }

        static void LiftCap(Component cloudComponent, float age)
        {
            int id = cloudComponent.GetInstanceID();
            if (!CapBase.TryGetValue(id, out Vector3 basis)) return;
            if (!TryCap(cloudComponent, out var renderer, out var curve)) return;
            if (renderer == null || curve == null) return;
            if (!CapRise.TryGetValue(id, out RiseSample sample))
                sample = new RiseSample();
            if (!sample.Used || age < sample.Age - 0.02f)
            {
                sample.Height = Integrate(curve, 0f, age);
                sample.Age = age;
                sample.Used = true;
            }
            else if (age > sample.Age)
            {
                sample.Height += Integrate(curve, sample.Age, age);
                sample.Age = age;
            }
            CapRise[id] = sample;
            renderer.transform.position = DatumWorld(basis + Vector3.up * sample.Height);
        }

        static Vector3 DatumLocal(Vector3 world)
        {
            var datum = Plugin.Datum;
            return datum != null ? datum.InverseTransformPoint(world) : world;
        }

        static Vector3 DatumWorld(Vector3 local)
        {
            var datum = Plugin.Datum;
            return datum != null ? datum.TransformPoint(local) : local;
        }

        static float Integrate(AnimationCurve curve, float from, float to)
        {
            if (to <= from) return 0f;
            const float step = 1f;
            float t = from;
            float prev = curve.Evaluate(from);
            float sum = 0f;
            while (t + step < to)
            {
                t += step;
                float next = curve.Evaluate(t);
                sum += (prev + next) * 0.5f * step;
                prev = next;
            }
            float end = curve.Evaluate(to);
            sum += (prev + end) * 0.5f * (to - t);
            return sum;
        }

        static bool ZeroWind(ref Vector3 __result)
        {
            if (_holdPieces <= 0 || Plugin.CurrentDoc == null) return true;
            __result = Vector3.zero;
            return false;
        }

        static bool KeepPiece(UnityEngine.Object obj)
        {
            if (_holdPieces <= 0 || Plugin.CurrentDoc == null || obj == null) return true;
            GameObject? go = obj as GameObject;
            if (go == null && obj is Component component) go = component.gameObject;
            if (go == null || !UnderWatch(go)) return true;
            int root = WatchedRoot(go);
            if (root == 0 || !KeepRoots.Contains(root)) return true;
            if (obj is GameObject && root == go.GetInstanceID()) return true;
            return false;
        }
    }
}
