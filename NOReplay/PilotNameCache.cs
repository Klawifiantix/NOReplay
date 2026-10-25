using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Pilot display names, shared by every recorded aircraft.
    /// Steam reflection and noblackbox_names.json run on a global 2s clock, not per aircraft per frame.
    /// </summary>
    internal static class PilotNameCache
    {
        static readonly object CacheLock = new();
        static readonly object FileLock = new();
        static readonly Dictionary<string, string> nameCache = new();
        static readonly Dictionary<string, object> watched = new();
        static readonly Dictionary<string, Type?> typeCache = new();
        static readonly Dictionary<Type, PropertyInfo[]> harvestProps = new();
        static readonly Dictionary<Type, PropertyInfo?> steamIdProps = new();
        static readonly List<string> bucket = new();

        static readonly Regex NamePropRx = new(
            "name|player|pilot|steam|nick|display|persona",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex Digits17 = new(@"^\d{1,17}$", RegexOptions.Compiled);
        static readonly Regex PlayerLabel = new(@"^Player(\s*\(\d+\))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex ShortDigits = new(@"^\d{1,3}$", RegexOptions.Compiled);
        static readonly Regex JsonMap = new("\"(\\d{17})\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Compiled);

        static string? cachePath;
        static float nextScan = 2f;
        static bool refreshStarted;
        static int ioBusy;
        static Dictionary<string, string>? pendingFile;
        static int saveBusy;
        static bool saveDirty;

        static bool steamTypesLooked;
        static Type? friendsType;
        static Type? csteamType;
        static MethodInfo? requestInfo;
        static MethodInfo? personaName;

        internal static void Watch(string steamId, object player)
        {
            if (string.IsNullOrEmpty(steamId) || player == null)
                return;
            lock (CacheLock)
                watched[steamId] = player;
        }

        internal static void Tick()
        {
            if (!refreshStarted)
            {
                refreshStarted = true;
                KickRefresh();
            }
            if (saveDirty)
                KickSave();
            float now = Time.unscaledTime;
            if (now < nextScan)
                return;
            nextScan = now + 2f;
            AdoptFile();
            RememberWatched();
            KickRefresh();
        }

        internal static string Resolve(string? steamId)
        {
            if (!string.IsNullOrEmpty(steamId))
            {
                lock (CacheLock)
                {
                    if (nameCache.TryGetValue(steamId, out var cached) && !IsUselessName(cached))
                        return cached;
                }
            }
            return steamId ?? "Unknown";
        }

        static void RememberWatched()
        {
            KeyValuePair<string, object>[] snap;
            lock (CacheLock)
            {
                snap = new KeyValuePair<string, object>[watched.Count];
                int n = 0;
                foreach (var kv in watched)
                    snap[n++] = kv;
            }
            for (int i = 0; i < snap.Length; i++)
            {
                string? found = TrySteamPersona(snap[i].Value) ?? TryDirectPlayerName(snap[i].Value);
                if (IsUselessName(found))
                    continue;
                lock (CacheLock)
                {
                    if (!nameCache.TryGetValue(snap[i].Key, out var old) || old != found)
                    {
                        nameCache[snap[i].Key] = found!;
                        saveDirty = true;
                    }
                }
            }
            if (saveDirty)
                KickSave();
        }

        static void KickRefresh()
        {
            if (Interlocked.CompareExchange(ref ioBusy, 1, 0) != 0)
                return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string path = CacheFile();
                    if (!File.Exists(path))
                        return;
                    string raw;
                    lock (FileLock)
                        raw = File.ReadAllText(path);
                    var parsed = ParseSimpleJsonMap(raw);
                    lock (CacheLock)
                        pendingFile = parsed;
                }
                catch { }
                finally
                {
                    Interlocked.Exchange(ref ioBusy, 0);
                }
            });
        }

        static void AdoptFile()
        {
            Dictionary<string, string>? pending;
            lock (CacheLock)
            {
                pending = pendingFile;
                pendingFile = null;
            }
            if (pending == null)
                return;
            lock (CacheLock)
            {
                foreach (var kv in pending)
                {
                    if (!IsUselessName(kv.Value))
                        nameCache[kv.Key] = kv.Value;
                }
            }
        }

        static void KickSave()
        {
            if (Interlocked.CompareExchange(ref saveBusy, 1, 0) != 0)
                return;
            Dictionary<string, string> snap;
            lock (CacheLock)
            {
                saveDirty = false;
                snap = new Dictionary<string, string>(nameCache);
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string json = ToSimpleJsonMap(snap);
                    lock (FileLock)
                        File.WriteAllText(CacheFile(), json);
                }
                catch { }
                finally
                {
                    Interlocked.Exchange(ref saveBusy, 0);
                }
            });
        }

        static string CacheFile()
        {
            if (cachePath != null)
                return cachePath;

            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low",
                "Shockfront", "NuclearOption");
            try { Directory.CreateDirectory(dir); } catch { }
            cachePath = Path.Combine(dir, "noblackbox_names.json");
            return cachePath;
        }

        static string? TryDirectPlayerName(object player)
        {
            bucket.Clear();
            HarvestObject(player, bucket);
            for (int i = 0; i < bucket.Count; i++)
            {
                if (!Digits17.IsMatch(bucket[i]))
                    return bucket[i];
            }
            return null;
        }

        static string? TrySteamPersona(object player)
        {
            try
            {
                EnsureSteamTypes();
                if (friendsType == null)
                    return null;

                Type pt = player.GetType();
                if (!steamIdProps.TryGetValue(pt, out var steamProp))
                {
                    steamProp = pt.GetProperty("SteamID");
                    steamIdProps[pt] = steamProp;
                }
                object? steamIdObj = steamProp?.GetValue(player);
                if (steamIdObj == null)
                    return null;

                ulong sid = Convert.ToUInt64(steamIdObj);
                object idArg = steamIdObj;
                if (csteamType != null)
                {
                    try { idArg = Activator.CreateInstance(csteamType, sid)!; } catch { }
                }

                requestInfo?.Invoke(null, new[] { idArg, true });
                object? raw = personaName?.Invoke(null, new[] { idArg });
                string? name = raw?.ToString();
                if (IsUselessName(name) || name!.Equals("[unknown]", StringComparison.OrdinalIgnoreCase))
                    return null;
                return name;
            }
            catch
            {
                return null;
            }
        }

        static void EnsureSteamTypes()
        {
            if (steamTypesLooked)
                return;
            steamTypesLooked = true;
            friendsType = FindType("Steamworks.SteamFriends")
                          ?? FindType("Steamworks.SteamFriends, Assembly-CSharp");
            csteamType = FindType("Steamworks.CSteamID")
                         ?? FindType("Steamworks.CSteamID, Assembly-CSharp");
            if (friendsType == null)
                return;
            requestInfo = friendsType.GetMethod("RequestUserInformation");
            personaName = friendsType.GetMethod("GetFriendPersonaName");
        }

        static Type? FindType(string name)
        {
            if (typeCache.TryGetValue(name, out var cached))
                return cached;
            Type? t = Type.GetType(name);
            if (t == null)
            {
                string shortName = name.Split(',')[0];
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    try
                    {
                        t = assemblies[i].GetType(shortName, false);
                        if (t != null)
                            break;
                    }
                    catch { }
                }
            }
            typeCache[name] = t;
            return t;
        }

        static bool IsUselessName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            string v = value.Trim();
            if (v.Contains("PlayerName")) return true;
            if (PlayerLabel.IsMatch(v)) return true;
            if (ShortDigits.IsMatch(v)) return true;
            return false;
        }

        static void Consider(List<string> into, object? value)
        {
            if (value == null) return;
            string tn = value.GetType().FullName ?? "";
            if (tn.Contains("PlayerName") && value is not string) return;
            string text = value.ToString();
            if (!IsUselessName(text))
                into.Add(text.Trim());
        }

        static void HarvestObject(object? obj, List<string> into, int depth = 0)
        {
            if (obj == null || depth > 2) return;
            Type type = obj.GetType();
            if (type == typeof(string) || type.IsPrimitive)
            {
                Consider(into, obj);
                return;
            }

            var props = PropsFor(type);
            for (int i = 0; i < props.Length; i++)
            {
                try
                {
                    object? val = props[i].GetValue(obj);
                    if (val != null && (val.GetType().FullName ?? "").Contains("PlayerName"))
                        HarvestObject(val, into, depth + 1);
                    else
                        Consider(into, val);
                }
                catch { }
            }
        }

        static PropertyInfo[] PropsFor(Type type)
        {
            if (harvestProps.TryGetValue(type, out var cached))
                return cached;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var all = type.GetProperties(flags);
            var keep = new List<PropertyInfo>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].GetIndexParameters().Length > 0) continue;
                if (!NamePropRx.IsMatch(all[i].Name)) continue;
                keep.Add(all[i]);
            }
            var arr = keep.ToArray();
            harvestProps[type] = arr;
            return arr;
        }

        static Dictionary<string, string> ParseSimpleJsonMap(string raw)
        {
            var d = new Dictionary<string, string>();
            foreach (Match m in JsonMap.Matches(raw))
                d[m.Groups[1].Value] = Regex.Unescape(m.Groups[2].Value);
            return d;
        }

        static string ToSimpleJsonMap(Dictionary<string, string> map)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            bool first = true;
            foreach (var kv in map)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(kv.Key).Append("\":\"");
                sb.Append(kv.Value.Replace("\\", "\\\\").Replace("\"", "\\\""));
                sb.Append('"');
            }
            sb.Append('}');
            return sb.ToString();
        }
    }
}
