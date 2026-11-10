using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Rebuilds the kill feed and the message list from replay time. The
    /// game's own KillFeed method still applies the viewer's line count.
    /// </summary>
    internal static class FeedPlayback
    {
        static float _prev = -1f;
        static int _shown = -1;
        static int _sig = int.MinValue;
        static bool _force;
        static bool _playErr;
        static readonly HashSet<int> _loggedCounts = new HashSet<int>();
        static MethodInfo _kill, _msg;
        static Component _gameplay, _board;
        static object _killFeed, _msgFeed;
        static PropertyInfo _killCount, _msgCount;
        static MethodInfo _killDrop, _msgDrop, _killRefresh, _msgRefresh;
        static bool _feedsCached;
        static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        public static void Tick(ReplayDocument doc, float time, bool paused)
        {
            if (doc == null) return;
            RecordFeed.SetReplay(true);
            try
            {
                float dt = _prev >= 0f ? time - _prev : 0f;
                bool sound = false;
                if (doc.Feeds != null)
                {
                    for (int i = 0; i < doc.Feeds.Count; i++)
                    {
                        var item = doc.Feeds[i];
                        if (item.Kind != FeedLog.Kind.Mission || !item.Sound) continue;
                        if (FeedLog.Crossed(_prev, time, item.Time, dt, paused))
                        {
                            sound = true;
                            break;
                        }
                    }
                }
                int shown = Show(doc, time);
                if (sound) PlayRadio();
                if (_loggedCounts.Add(shown))
                {
                    _shown = shown;
                    Plugin.Log?.LogInfo("Feed play t=" + time.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                        + " lines=" + shown
                        + " sound=" + (sound ? 1 : 0));
                }
                _prev = time;
            }
            catch (Exception ex)
            {
                if (!_playErr)
                {
                    _playErr = true;
                    Plugin.Log?.LogWarning("Feed play: " + ex.Message);
                }
            }
        }

        public static void Rebuild()
        {
            if (Plugin.CurrentDoc == null) return;
            _force = true;
            try { Show(Plugin.CurrentDoc, Plugin.Clock.Time); }
            catch (Exception ex)
            {
                if (!_playErr)
                {
                    _playErr = true;
                    Plugin.Log?.LogWarning("Feed play: " + ex.Message);
                }
            }
        }

        public static void Reset()
        {
            _prev = -1f;
            _shown = -1;
            _sig = int.MinValue;
            _force = false;
            _playErr = false;
            _loggedCounts.Clear();
            _feedsCached = false;
            _gameplay = null;
            _board = null;
            RecordFeed.SetReplay(false);
        }

        static int Show(ReplayDocument doc, float time)
        {
            if (!EnsureFeeds()) return 0;
            float minValue = 0f;
            try { minValue = PlayerSettings.killFeedMinValue; }
            catch { }
            int filters = 0;
            try
            {
                filters = FilterFor(0) | (FilterFor(1) << 3) | (FilterFor(2) << 6)
                    | (FilterFor(3) << 9) | (FilterFor(4) << 12);
            }
            catch { }
            int toggle = (Plugin.ShowKillFeed ? 1 : 0) | (Plugin.ShowChat ? 2 : 0) | (Plugin.ShowSystem ? 4 : 0);
            int sig = toggle * 397 ^ filters ^ (int)(minValue * 10f);
            int n = 0;
            int count = doc.Feeds != null ? doc.Feeds.Count : 0;
            for (int i = 0; i < count; i++)
            {
                var item = doc.Feeds[i];
                if (!Wanted(item.Kind)) continue;
                int chars = (item.A != null ? item.A.Length : 0) + (item.B != null ? item.B.Length : 0);
                if (!FeedLog.Visible(item.Time, time, item.Kind, chars)) continue;
                if (item.Kind == FeedLog.Kind.Kill)
                {
                    if (!FeedLog.PassesKill(item, FilterFor(item.KillType), minValue)) continue;
                }
                sig = sig * 31 + i + 1;
                n++;
            }
            if (!_force && sig == _sig) return _shown < 0 ? n : _shown;
            _force = false;
            _sig = sig;
            _shown = n;
            Drain(_killFeed, _killCount, _killDrop);
            Drain(_msgFeed, _msgCount, _msgDrop);
            if (n == 0)
            {
                Refresh(_killFeed, _killRefresh);
                Refresh(_msgFeed, _msgRefresh);
                return 0;
            }
            if (_kill == null) _kill = _gameplay.GetType().GetMethod("KillFeed", new[] { typeof(string) });
            if (_msg == null) _msg = _gameplay.GetType().GetMethod("GameMessage", new[] { typeof(string) });
            int placed = 0;
            for (int i = 0; i < count; i++)
            {
                var item = doc.Feeds[i];
                if (!Wanted(item.Kind)) continue;
                int chars = (item.A != null ? item.A.Length : 0) + (item.B != null ? item.B.Length : 0);
                if (!FeedLog.Visible(item.Time, time, item.Kind, chars)) continue;
                if (item.Kind == FeedLog.Kind.Kill)
                {
                    if (!FeedLog.PassesKill(item, FilterFor(item.KillType), minValue)) continue;
                    try { _kill?.Invoke(_gameplay, new object[] { FeedLog.FormatKill(item) }); } catch { }
                }
                else
                {
                    string line = item.Kind == FeedLog.Kind.Mission ? item.B : FeedLog.FormatChat(item);
                    if (string.IsNullOrEmpty(line)) continue;
                    try { _msg?.Invoke(_gameplay, new object[] { line }); } catch { }
                }
                placed++;
            }
            Refresh(_killFeed, _killRefresh);
            Refresh(_msgFeed, _msgRefresh);
            _shown = placed;
            return placed;
        }

        static bool EnsureFeeds()
        {
            if (_feedsCached && _board != null && _gameplay != null) return _killFeed != null;
            _gameplay = Singleton("GameplayUI");
            _board = Singleton("MessageUI");
            if (_gameplay == null || _board == null) return false;
            _killFeed = _board.GetType().GetField("killFeed", Flags)?.GetValue(_board);
            _msgFeed = _board.GetType().GetField("messageFeed", Flags)?.GetValue(_board);
            CacheFeed(_killFeed, out _killCount, out _killDrop, out _killRefresh);
            CacheFeed(_msgFeed, out _msgCount, out _msgDrop, out _msgRefresh);
            _feedsCached = _killFeed != null && _msgFeed != null;
            return _feedsCached;
        }

        static void CacheFeed(object feed, out PropertyInfo count, out MethodInfo drop, out MethodInfo refresh)
        {
            count = null;
            drop = null;
            refresh = null;
            if (feed == null) return;
            var t = feed.GetType();
            count = t.GetProperty("NbLines", Flags);
            drop = t.GetMethod("Dequeue", Flags);
            refresh = t.GetMethod("RefreshUI", Flags);
        }

        static bool Wanted(FeedLog.Kind kind)
        {
            if (kind == FeedLog.Kind.Kill) return Plugin.ShowKillFeed;
            if (kind == FeedLog.Kind.Global || kind == FeedLog.Kind.Ally) return Plugin.ShowChat;
            return Plugin.ShowSystem;
        }

        static void Refresh(object feed, MethodInfo refresh)
        {
            if (feed == null || refresh == null) return;
            try { refresh.Invoke(feed, null); } catch { }
        }

        static int FilterFor(int killType)
        {
            try
            {
                switch (killType)
                {
                    case 0: return (int)PlayerSettings.killFeedAircraft;
                    case 1: return (int)PlayerSettings.killFeedVehicle;
                    case 2: return (int)PlayerSettings.killFeedBuilding;
                    case 3: return (int)PlayerSettings.killFeedMunition;
                    case 4: return (int)PlayerSettings.killFeedShip;
                }
            }
            catch { }
            return 0;
        }

        static void Drain(object feed, PropertyInfo count, MethodInfo drop)
        {
            if (feed == null || count == null || drop == null) return;
            int guard = 0;
            while (count.GetValue(feed) is int n && n > 0 && guard < 40)
            {
                drop.Invoke(feed, null);
                guard++;
            }
        }

        static void PlayRadio()
        {
            try
            {
                var assets = Plugin.FindGameType("GameAssets");
                var inst = assets?.GetProperty("i", Flags)?.GetValue(null) ?? assets?.GetField("i", Flags)?.GetValue(null);
                var clip = inst?.GetType().GetField("radioStatic", Flags)?.GetValue(inst);
                var sound = Plugin.FindGameType("SoundManager");
                var play = sound?.GetMethod("PlayInterfaceOneShot", Flags);
                if (clip != null && play != null) play.Invoke(null, new[] { clip });
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Feed sound: " + ex.Message);
            }
        }

        static Component Singleton(string name)
        {
            var t = Plugin.FindGameType(name);
            if (t == null) return null;
            // SceneSingleton<T>.i is a static field, not a property.
            object inst = ReadStatic(t, "i");
            if (inst == null && t.BaseType != null)
                inst = ReadStatic(t.BaseType, "i");
            return inst as Component;
        }

        static object ReadStatic(Type type, string name)
        {
            var prop = type.GetProperty(name, Flags);
            if (prop != null) return prop.GetValue(null);
            return type.GetField(name, Flags)?.GetValue(null);
        }
    }
}
