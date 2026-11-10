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
        static bool _logged;
        static bool _playErr;
        static bool _emptyCleared;
        static MethodInfo _kill, _msg;
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
                if (!_logged || shown != _shown)
                {
                    _logged = true;
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
            _logged = false;
            _playErr = false;
            _emptyCleared = false;
            RecordFeed.SetReplay(false);
        }

        static int Show(ReplayDocument doc, float time)
        {
            var gameplay = Singleton("GameplayUI");
            var board = Singleton("MessageUI");
            if (gameplay == null || board == null) return 0;
            if ((doc.Feeds == null || doc.Feeds.Count == 0) && _emptyCleared) return 0;
            Drain(board, "killFeed");
            Drain(board, "messageFeed");
            if (doc.Feeds == null || doc.Feeds.Count == 0)
            {
                if (!_emptyCleared)
                {
                    Refresh(board, "killFeed");
                    Refresh(board, "messageFeed");
                    _emptyCleared = true;
                }
                return 0;
            }
            _emptyCleared = false;
            float minValue = 0f;
            try { minValue = PlayerSettings.killFeedMinValue; }
            catch { }
            int n = 0;
            if (_kill == null) _kill = gameplay.GetType().GetMethod("KillFeed", new[] { typeof(string) });
            if (_msg == null) _msg = gameplay.GetType().GetMethod("GameMessage", new[] { typeof(string) });
            var kill = _kill;
            var msg = _msg;
            for (int i = 0; i < doc.Feeds.Count; i++)
            {
                var item = doc.Feeds[i];
                if (!Wanted(item.Kind)) continue;
                int chars = (item.A != null ? item.A.Length : 0) + (item.B != null ? item.B.Length : 0);
                if (!FeedLog.Visible(item.Time, time, item.Kind, chars)) continue;
                if (item.Kind == FeedLog.Kind.Kill)
                {
                    int which = FilterFor(item.KillType);
                    if (!FeedLog.PassesKill(item, which, minValue)) continue;
                    try { kill?.Invoke(gameplay, new object[] { FeedLog.FormatKill(item) }); } catch { }
                }
                else
                {
                    string line = item.Kind == FeedLog.Kind.Mission ? item.B : FeedLog.FormatChat(item);
                    if (string.IsNullOrEmpty(line)) continue;
                    try { msg?.Invoke(gameplay, new object[] { line }); } catch { }
                }
                n++;
            }
            Refresh(board, "killFeed");
            Refresh(board, "messageFeed");
            return n;
        }

        static bool Wanted(FeedLog.Kind kind)
        {
            if (kind == FeedLog.Kind.Kill) return Plugin.ShowKillFeed;
            if (kind == FeedLog.Kind.Global || kind == FeedLog.Kind.Ally) return Plugin.ShowChat;
            return Plugin.ShowSystem;
        }

        static void Refresh(Component board, string field)
        {
            var feed = board.GetType().GetField(field, Flags)?.GetValue(board);
            feed?.GetType().GetMethod("RefreshUI", Flags)?.Invoke(feed, null);
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

        static void Drain(Component board, string field)
        {
            var feed = board.GetType().GetField(field, Flags)?.GetValue(board);
            if (feed == null) return;
            var count = feed.GetType().GetProperty("NbLines", Flags);
            var drop = feed.GetType().GetMethod("Dequeue", Flags);
            if (count == null || drop == null) return;
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
