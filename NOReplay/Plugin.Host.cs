using System;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;

namespace NOReplay
{
    public partial class Plugin
    {
        static bool _worldStarting;
        static float _worldReadyAt;
        static object? _cachedNm;

        static void CallStartReplaySession()
        {
            DumpEnums();
            CallStartHost();
            try { ApplyEmptyMissionInWorld(); }
            catch (Exception ex) { Log.LogError("Apply Mission: " + ex); }
            DumpNetworkState("sofort");
            TryActivateServer();
            _worldStarting = true;
            _worldReadyAt = Time.unscaledTime + 2.5f;
            Log.LogInfo("Replay-Welt startet. Warte 2.5s auf Server/SOM.");
        }

        static void TickWorldStart()
        {
            if (!_worldStarting) return;
            if (MainMenuVisible()) return;
            if (Time.unscaledTime < _worldReadyAt) return;
            _worldStarting = false;
            TryActivateServer();
            DumpNetworkState("nach Szenenwechsel");
            Log.LogInfo("Server aktiv=" + IsServerActive() + " — StartMission kommt vom Spiel (OnStartServer).");
        }

        static void DumpEnums()
        {
            DumpEnumNames("SocketType");
            DumpEnumNames("GameState");
            DumpEnumNames("HostOptions");
        }

        static void DumpEnumNames(string typeName)
        {
            var t = FindGameType(typeName);
            if (t == null) { Log.LogInfo("Enum fehlt: " + typeName); return; }
            if (t.IsEnum)
            {
                var names = Enum.GetNames(t);
                Log.LogInfo(typeName + " = " + string.Join(", ", names));
                return;
            }
            Log.LogInfo(typeName + " ist kein Enum (" + t.FullName + ")");
        }

        static void CallStartHost()
        {
            var nmType = FindGameType("NetworkManagerNuclearOption");
            var optType = FindGameType("HostOptions");
            var gsType = FindGameType("GameState");
            var sockType = FindGameType("SocketType");
            var mmType = FindGameType("MissionManager");
            if (nmType == null || optType == null || gsType == null || sockType == null || mmType == null)
            {
                Log.LogError("Typ fehlt.");
                return;
            }
            UnityEngine.Object? nm = FindNetworkManager(nmType);
            Log.LogInfo("Nutze NM=" + nm);
            if (nm == null) return;
            _cachedNm = nm;
            var sflags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            object? mission = mmType.GetProperty("CurrentMission", sflags)?.GetValue(null);
            if (mission == null)
            {
                Log.LogError("CurrentMission fehlt, StartHost abgebrochen.");
                return;
            }
            object? map = mission.GetType().GetField("MapKey")?.GetValue(mission);
            Log.LogInfo("Official Start: MapKey=" + map + " Mission=" + mission);
            object sock = ParseEnumPrefer(sockType, "Offline");
            object gs = ParseEnumPrefer(gsType, "SinglePlayer", "Singleplayer");
            object opt = Activator.CreateInstance(optType, sock, gs, map)!;
            var start = nmType.GetMethod("StartHost", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            start!.Invoke(nm, new[] { opt });
            Log.LogInfo("StartHost zurück (wie SinglePlayerMenu).");
        }

        static object ParseEnumPrefer(Type enumType, params string[] names)
        {
            foreach (var n in names)
            {
                try { return Enum.Parse(enumType, n, true); }
                catch { }
            }
            var all = Enum.GetNames(enumType);
            Log.LogInfo("Fallback Enum " + enumType.Name + " -> " + all[0]);
            return Enum.Parse(enumType, all[0]);
        }

        static UnityEngine.Object? FindNetworkManager(Type nmType)
        {
            foreach (var o in Resources.FindObjectsOfTypeAll(nmType))
                if (o != null && o.name == "networkManager") return o;
            foreach (var o in Resources.FindObjectsOfTypeAll(nmType))
                if (o != null) return o;
            return null;
        }

        static bool _mapsDumped;
        static void DumpGameMaps()
        {
            if (_mapsDumped) return;
            _mapsDumped = true;
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var loaderType = FindGameType("MapLoader");
                if (loaderType == null)
                {
                    Log.LogInfo("MapLoader Typ fehlt.");
                    return;
                }
                foreach (var o in Resources.FindObjectsOfTypeAll(loaderType))
                {
                    if (o == null) continue;
                    object? maps = loaderType.GetField("Maps", flags)?.GetValue(o);
                    if (maps is not Array arr) continue;
                    Log.LogInfo("MapLoader Maps=" + arr.Length);
                    foreach (var item in arr)
                    {
                        if (item == null) continue;
                        var t = item.GetType();
                        object? details = t.GetField("Details", flags)?.GetValue(item)
                                       ?? t.GetProperty("Details", flags)?.GetValue(item);
                        string pref = "";
                        string name = "";
                        if (details != null)
                        {
                            pref = details.GetType().GetField("PrefabName")?.GetValue(details) as string ?? "";
                            name = details.GetType().GetField("MapName")?.GetValue(details) as string ?? "";
                        }
                        Log.LogInfo("  Map PrefabName='" + pref + "' MapName='" + name + "'");
                    }
                    object? def = loaderType.GetField("DefaultMap", flags)?.GetValue(o);
                    Log.LogInfo("  DefaultMap=" + def);
                }
            }
            catch (Exception ex) { Log.LogInfo("DumpGameMaps: " + ex.Message); }
        }

        static void ApplyReplayMission()
        {
            ApplyNamedUserMission("NOReplay");
        }

        static void ApplyEmptyMissionInWorld()
        {
            ApplyNamedUserMission("Heartland Empty");
        }

        static void ApplyNamedUserMission(string missionName)
        {
            var keyType = FindGameType("MissionKey");
            var groupType = FindGameType("MissionGroup");
            var saveType = FindGameType("MissionSaveLoad");
            var mmType = FindGameType("MissionManager");
            var nmType = FindGameType("NetworkManagerNuclearOption");
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var sflags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            object key = Activator.CreateInstance(keyType)!;
            object userGroup = groupType.GetProperty("User", sflags)!.GetValue(null)!;
            keyType.GetField("Name", flags)!.SetValue(key, missionName);
            keyType.GetField("Key", flags)!.SetValue(key, missionName);
            keyType.GetField("Group", flags)!.SetValue(key, userGroup);
            object?[] args = { key, null, null };
            bool ok = (bool)saveType.GetMethod("TryLoad", sflags)!.Invoke(null, args)!;
            Log.LogInfo("TryLoad ok=" + ok + " err=" + args[2]);
            if (!ok || args[1] == null) return;
            mmType.GetMethod("SetMission", sflags)!.Invoke(null, new[] { args[1], false });
            Log.LogInfo("SetMission " + missionName + ", StartMission nach Szenenload.");
        }

        static void TryStartLoadedMission()
        {
            var mmType = FindGameType("MissionManager");
            if (mmType == null) return;
            var sflags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            mmType.GetMethod("StartMission", sflags)?.Invoke(null, null);
            Log.LogInfo("StartMission aufgerufen.");
        }

        static void DumpNetworkState(string tag)
        {
            Log.LogInfo("=== Netz " + tag + " ===");
            DumpTypeInstances("ServerObjectManager");
            DumpTypeInstances("ClientObjectManager");
            DumpTypeInstances("NetworkServer");
            DumpTypeInstances("NetworkClient");
            var nmType = FindGameType("NetworkManagerNuclearOption");
            if (nmType == null) return;
            var nm = FindNetworkManager(nmType);
            if (nm == null) { Log.LogInfo("kein NM"); return; }
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var f in nmType.GetFields(flags))
            {
                if (f.Name.Contains("BackingField")) continue;
                object? v = null;
                try { v = f.GetValue(nm); } catch { continue; }
                if (v == null) continue;
                string tn = f.FieldType.Name;
                if (tn.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0
                    || tn.IndexOf("Client", StringComparison.OrdinalIgnoreCase) >= 0
                    || tn.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0
                    || tn.IndexOf("Socket", StringComparison.OrdinalIgnoreCase) >= 0
                    || tn.IndexOf("ObjectManager", StringComparison.OrdinalIgnoreCase) >= 0)
                    Log.LogInfo("  NM." + f.Name + " = " + v + " (" + tn + ")");
            }
            foreach (var p in nmType.GetProperties(flags))
            {
                if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                object? v = null;
                try { v = p.GetValue(nm); } catch { continue; }
                if (v == null) continue;
                string tn = p.PropertyType.Name;
                if (tn.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0
                    || tn.IndexOf("Client", StringComparison.OrdinalIgnoreCase) >= 0
                    || tn.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0
                    || tn.IndexOf("ObjectManager", StringComparison.OrdinalIgnoreCase) >= 0)
                    Log.LogInfo("  NM." + p.Name + " = " + v + " (" + tn + ")");
            }
            DumpFlags(GetNmMember(nm, "Server"), "Server");
            DumpFlags(GetNmMember(nm, "Client"), "Client");
            DumpFlags(GetNmMember(nm, "ServerObjectManager"), "SOM");
        }

        static object? GetNmMember(UnityEngine.Object nm, string name)
        {
            var t = nm.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try { return t.GetProperty(name, flags)?.GetValue(nm) ?? t.GetField(name, flags)?.GetValue(nm); }
            catch { return null; }
        }

        static void DumpFlags(object? obj, string label)
        {
            if (obj == null) { Log.LogInfo("  " + label + " = null"); return; }
            var t = obj.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            string[] names = { "Active", "active", "IsActive", "Started", "IsRunning", "LocalPlayer", "Player" };
            foreach (var n in names)
            {
                object? v = null;
                try { v = t.GetProperty(n, flags)?.GetValue(obj); } catch { }
                if (v == null)
                    try { v = t.GetField(n, flags)?.GetValue(obj); } catch { }
                if (v != null)
                    Log.LogInfo("  " + label + "." + n + "=" + v);
            }
        }

        static bool IsServerActive()
        {
            var nmType = FindGameType("NetworkManagerNuclearOption");
            if (nmType == null) return false;
            var nm = FindNetworkManager(nmType);
            if (nm == null) return false;
            var server = GetNmMember(nm, "Server");
            if (server == null) return false;
            var t = server.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var n in new[] { "Active", "Started", "IsActive" })
            {
                try
                {
                    var p = t.GetProperty(n, flags);
                    if (p != null && p.GetValue(server) is bool b && b) return true;
                }
                catch { }
            }
            return false;
        }

        static void TryActivateServer()
        {
            var nmType = FindGameType("NetworkManagerNuclearOption");
            if (nmType == null) return;
            var nm = FindNetworkManager(nmType);
            if (nm == null) return;
            if (IsServerActive()) { Log.LogInfo("Server schon aktiv."); return; }
            var server = GetNmMember(nm, "Server");
            if (server == null) { Log.LogWarning("NM.Server null"); return; }
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var m in server.GetType().GetMethods(flags))
            {
                if (m.Name != "Start" && m.Name != "Listen") continue;
                if (m.GetParameters().Length != 0) continue;
                try
                {
                    m.Invoke(server, null);
                    Log.LogInfo("Server." + m.Name + "() aufgerufen.");
                }
                catch (Exception ex) { Log.LogWarning("Server." + m.Name + ": " + ex.Message); }
            }
            Log.LogInfo("Server aktiv nach Start-Versuch=" + IsServerActive());
        }

        static void DumpTypeInstances(string typeName)
        {
            var t = FindGameType(typeName);
            if (t == null)
            {
                Log.LogInfo("  Typ fehlt: " + typeName);
                return;
            }
            var all = Resources.FindObjectsOfTypeAll(t);
            Log.LogInfo("  " + typeName + " count=" + all.Length);
            for (int i = 0; i < all.Length && i < 4; i++)
            {
                var o = all[i];
                if (o == null) continue;
                Log.LogInfo("    [" + i + "] " + o.name + " type=" + o.GetType().FullName);
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var p in o.GetType().GetProperties(flags))
                {
                    if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                    if (p.Name != "Active" && p.Name != "IsRunning" && p.Name != "active"
                        && p.Name != "Started" && p.Name != "LocalPlayer" && p.Name != "Player")
                        continue;
                    try { Log.LogInfo("      ." + p.Name + "=" + p.GetValue(o)); } catch { }
                }
            }
        }

        static object? _livePlayer;
        static float _livePlayerAt = -10f;

        static object? FindLocalNetworkPlayer()
        {
            if (_livePlayer != null && Time.unscaledTime - _livePlayerAt < 1f)
                return _livePlayer;
            _livePlayer = null;
            var playerType = FindGameType("Player");
            if (playerType == null || !typeof(UnityEngine.Object).IsAssignableFrom(playerType))
                return null;
            foreach (var o in Resources.FindObjectsOfTypeAll(playerType))
            {
                if (o == null) continue;
                if (o.name.IndexOf("Unspawned", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                _livePlayer = o;
                _livePlayerAt = Time.unscaledTime;
                return o;
            }
            _livePlayerAt = Time.unscaledTime;
            return null;
        }
    }
}