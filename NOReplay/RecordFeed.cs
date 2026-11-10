using System;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Chat;
using NuclearOption.Networking;
using NuclearOption.UIStyleSystem;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Records the kill feed, the chat this client actually receives, and
    /// mission lines after the faction filter. Live handlers stay quiet
    /// during playback so the replay is the only writer.
    /// </summary>
    internal static class RecordFeed
    {
        static bool _patched;
        static float _missionAt = -1f;
        static string _missionText = "";
        static bool _listLooked;
        static MethodInfo _listContains;
        static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        public static void Install()
        {
            if (_patched) return;
            var harmony = new Harmony("noreplay.feed");
            int n = 0;
            n += Patch(harmony, "NuclearOption.Chat.ChatManager", "UserCode_TargetReceiveMessage_1307761090", nameof(BeforeChat));
            n += Patch(harmony, "NuclearOption.Chat.ChatManager", "UserCode_RpcServerMessage_1244201393", nameof(BeforeServer));
            n += Patch(harmony, "NuclearOption.Chat.ChatManager", "UserCode_RpcTargetServerMessage__002D537879009", nameof(BeforeServer));
            n += Patch(harmony, "MessageManager", "UserCode_RpcKillMessage_635947223", nameof(BeforeKill));
            n += Patch(harmony, "MissionMessages", "ShowMessgeLocal", nameof(BeforeMission));
            n += Patch(harmony, "MessageFeed", "Update", nameof(SkipFeedUpdate));
            _patched = n > 0;
            Plugin.Log?.LogInfo("Feed-Record methods=" + n);
        }

        static int Patch(Harmony harmony, string typeName, string method, string prefix)
        {
            var t = Plugin.FindGameType(typeName);
            var m = t?.GetMethod(method, Flags);
            if (m == null)
            {
                Plugin.Log?.LogInfo("Feed-Record missing " + typeName + "." + method);
                return 0;
            }
            try
            {
                harmony.Patch(m, prefix: new HarmonyMethod(typeof(RecordFeed), prefix));
                return 1;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogInfo("Feed-Record " + method + " " + ex.Message);
                return 0;
            }
        }

        static bool SkipFeedUpdate()
        {
            return Plugin.CurrentDoc == null;
        }

        static bool BeforeChat(string message, Player player, bool allChat)
        {
            if (Plugin.CurrentDoc != null) return false;
            if (!Record.Active || player == null) return true;
            try
            {
                if (!PlayerSettings.chatEnabled) return true;
                if (Silenced(player)) return true;
                string name = player.GetDisplayName(PlayerNameContext.ChatOrLeaderboard).ProfanityFilter();
                message = (message ?? "").ProfanityFilter();
                Color color = ChatColor(player.HQ, allChat);
                int r = Band(color.r), g = Band(color.g), b = Band(color.b);
                string body = (allChat ? "G|" : "A|")
                    + r.ToString(CultureInfo.InvariantCulture) + "|"
                    + g.ToString(CultureInfo.InvariantCulture) + "|"
                    + b.ToString(CultureInfo.InvariantCulture) + "|"
                    + FeedLog.Escape(name) + "|"
                    + FeedLog.Escape(message ?? "");
                Write(body);
                Record.Log?.LogInfo("Feed rec chat " + (allChat ? "global" : "ally") + " " + name);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Feed chat: " + ex.Message);
            }
            return true;
        }

        static bool BeforeServer(string message, bool runTtsIfEnabled)
        {
            if (Plugin.CurrentDoc != null) return false;
            if (!Record.Active) return true;
            try
            {
                if (!PlayerSettings.chatEnabled || string.IsNullOrEmpty(message)) return true;
                Write("S|" + FeedLog.Escape(message.ProfanityFilter()));
                Record.Log?.LogInfo("Feed rec server");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Feed server: " + ex.Message);
            }
            _ = runTtsIfEnabled;
            return true;
        }

        static bool BeforeKill(PersistentID killerID, PersistentID killedID, KillType killedType)
        {
            if (Plugin.CurrentDoc != null) return false;
            if (!Record.Active) return true;
            try
            {
                PersistentUnit victim = null;
                PersistentUnit killer = null;
                if (killedID.IsValid) UnitRegistry.TryGetPersistentUnit(killedID, out victim);
                if (killerID.IsValid) UnitRegistry.TryGetPersistentUnit(killerID, out killer);
                string victimName = victim != null && !string.IsNullOrEmpty(victim.unitName) ? victim.unitName : killedID.ToString();
                string killerName = killer != null && !string.IsNullOrEmpty(killer.unitName) ? killer.unitName : "";
                float value = 0f;
                try
                {
                    if (victim != null && victim.definition != null)
                        value = victim.definition.value;
                }
                catch { }
                Color vc = SideColor(victim);
                Color kc = SideColor(killer);
                int flags = 0;
                if (IsLocalPlayer(killer)) flags |= 1;
                if (IsLocalPlayer(victim)) flags |= 2;
                if (IsFriendly(killer)) flags |= 4;
                if (IsFriendly(victim)) flags |= 8;
                if (IsEnemy(killer)) flags |= 16;
                if (IsEnemy(victim)) flags |= 32;
                string body = "K|" + ((int)killedType).ToString(CultureInfo.InvariantCulture)
                    + "|" + value.ToString("0.##", CultureInfo.InvariantCulture)
                    + "|" + flags.ToString(CultureInfo.InvariantCulture)
                    + "|" + Band(kc.r) + "|" + Band(kc.g) + "|" + Band(kc.b)
                    + "|" + Band(vc.r) + "|" + Band(vc.g) + "|" + Band(vc.b)
                    + "|" + FeedLog.Escape(killerName)
                    + "|" + FeedLog.Escape(victimName);
                Write(body);
                Record.Log?.LogInfo("Feed rec kill " + victimName
                    + (killerName.Length > 0 ? " by " + killerName : "")
                    + " type=" + killedType);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Feed kill: " + ex.Message);
            }
            return true;
        }

        static bool BeforeMission(string message, bool playsound, FactionHQ filterFaction)
        {
            if (Plugin.CurrentDoc != null) return false;
            if (!Record.Active || string.IsNullOrEmpty(message)) return true;
            try
            {
                if (!LocalFaction(filterFaction)) return true;
                if (message == _missionText && Time.time - _missionAt < 0.2f) return true;
                _missionText = message;
                _missionAt = Time.time;
                Write("M|" + (playsound ? "1" : "0") + "|" + FeedLog.Escape(message));
                Record.Log?.LogInfo("Feed rec mission sound=" + (playsound ? 1 : 0));
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Feed mission: " + ex.Message);
            }
            return true;
        }

        // The Player overload sits beside Contains(CSteamID). Calling it makes
        // the compiler demand the Steamworks assembly, so the check goes through reflection.
        static bool Silenced(Player player)
        {
            try
            {
                if (!_listLooked)
                {
                    _listLooked = true;
                    var ext = Plugin.FindGameType("AllowBanListExtensions");
                    var list = Plugin.FindGameType("AllowBanList");
                    if (ext != null && list != null)
                        _listContains = ext.GetMethod("Contains", Flags, null, new[] { list, typeof(Player) }, null);
                    if (_listContains == null)
                        Plugin.Log?.LogInfo("Feed mute check missing");
                }
                if (_listContains == null) return false;
                if (Listed(GameManager.MutedList, player)) return true;
                if (Listed(GameManager.BlockList, player)) return true;
            }
            catch { }
            return false;
        }

        static bool Listed(object list, Player player)
        {
            return list != null && _listContains.Invoke(null, new[] { list, player }) is bool hit && hit;
        }

        static void Write(string body)
        {
            var engine = Record.Host != null ? Record.Host.GetComponent<RecordEngine>() : null;
            engine?.WriteGlobalEvent("Feed=" + body);
        }

        static Color ChatColor(FactionHQ hq, bool allChat)
        {
            var cm = NetworkSceneSingleton<ChatManager>.i;
            if (cm == null) return Color.white;
            if (hq == null)
                return FieldColor(cm, "noFaction", Color.white);
            if (!allChat)
                return FieldColor(cm, "alliedChat", Color.white);
            Color baseColor = hq.faction != null ? hq.faction.color : Color.white;
            Color.RGBToHSV(baseColor, out float h, out float s, out float v);
            float sat = 1f;
            var f = typeof(ChatManager).GetField("allChatSaturation", Flags);
            if (f?.GetValue(cm) is float scale) sat = scale;
            return Color.HSVToRGB(h, s * sat, v);
        }

        static Color FieldColor(object obj, string name, Color fallback)
        {
            var f = obj.GetType().GetField(name, Flags);
            if (f?.GetValue(obj) is Color c) return c;
            return fallback;
        }

        static Color SideColor(PersistentUnit unit)
        {
            try
            {
                FactionHQ hq = unit != null ? unit.GetHQ() : null;
                if (hq == null)
                    return ThemeManager.Active.ColorTheme.ChatNeutral;
                if (GameManager.GetLocalHQ(out var local))
                    return local == hq
                        ? ThemeManager.Active.ColorTheme.ChatFriendly
                        : ThemeManager.Active.ColorTheme.ChatHostile;
                return hq.faction != null ? hq.faction.color : Color.white;
            }
            catch
            {
                return Color.white;
            }
        }

        static bool LocalFaction(FactionHQ filter)
        {
            if (filter == null) return true;
            if (GameManager.GetLocalHQ(out var local)) return filter == local;
            return false;
        }

        static bool IsLocalPlayer(PersistentUnit unit)
        {
            return unit != null && GameManager.IsLocalPlayer(unit.player);
        }

        static bool IsFriendly(PersistentUnit unit)
        {
            return unit != null && GameManager.IsLocalHQ(unit.GetHQ());
        }

        static bool IsEnemy(PersistentUnit unit)
        {
            if (unit != null && unit.HasHQ(out var hq) && GameManager.GetLocalHQ(out var local))
                return hq != local;
            return false;
        }

        static int Band(float channel)
        {
            int v = (int)Math.Round(channel * 255f);
            if (v < 0) return 0;
            if (v > 255) return 255;
            return v;
        }
    }
}
