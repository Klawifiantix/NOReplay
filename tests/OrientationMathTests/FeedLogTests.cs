using System;
using System.Collections.Generic;
using System.IO;
using NOReplay;

namespace NOReplay.OrientationTests
{
    static class FeedLogTests
    {
        public static int Failed;

        public static void Run()
        {
            RoundTrip();
            EmptyFileShowsNothing();
            LineCountIsSeparateFromCapture();
            FiltersUseTheViewerSetting();
            WindowFollowsReplayTime();
            SoundOnlyOnAShortForwardStep();
            AllyPrefixIsInsideTheColour();
            RecordedDominationFile();
            SystemLineStaysPlain();
            if (Failed == 0) System.Console.WriteLine("FeedLog ok");
        }

        static void RoundTrip()
        {
            string raw = "K|0|25|5|10|20|30|40|50|60|" + FeedLog.Escape("Ace, 1") + "|" + FeedLog.Escape("Bandit|2");
            Expect(FeedLog.TryParse(raw, 4f, out var item), "kill line parses");
            Expect(item.Time == 4f && item.Kind == FeedLog.Kind.Kill && item.KillType == 0, "time and type");
            Expect(item.A == "Ace, 1" && item.B == "Bandit|2", "names keep comma and bar");
            Expect(FeedLog.FormatKill(item).IndexOf("shot down") >= 0, "aircraft verb");
            string chat = "A|1|2|3|" + FeedLog.Escape("Sam") + "|" + FeedLog.Escape("hello, ally");
            Expect(FeedLog.TryParse(chat, 2f, out var ally) && ally.Kind == FeedLog.Kind.Ally, "ally chat parses");
            Expect(ally.B == "hello, ally", "ally text");
            string shown = FeedLog.FormatChat(ally);
            Expect(shown.StartsWith("<color=#") && shown.IndexOf("(ally)Sam:") >= 0, "ally prefix is coloured with the name");
        }

        static void EmptyFileShowsNothing()
        {
            Expect(!FeedLog.TryParse(null, 1f, out _), "missing line is ignored");
            Expect(!FeedLog.TryParse("Nope", 1f, out _), "unknown line is ignored");
        }

        static void LineCountIsSeparateFromCapture()
        {
            var item = Kill(0, 100f, 1 | 2);
            Expect(FeedLog.PassesKill(item, 4, 0f), "all still passes when the line count is a display setting");
            Expect(!FeedLog.PassesKill(item, 0, 0f), "filter none hides the line");
        }

        static void FiltersUseTheViewerSetting()
        {
            var mine = Kill(0, 50f, 1);
            Expect(FeedLog.PassesKill(mine, 1, 0f), "player filter keeps a local kill");
            Expect(!FeedLog.PassesKill(mine, 1, 80f), "min value still drops a cheap kill");
            var other = Kill(1, 50f, 16);
            Expect(FeedLog.PassesKill(other, 3, 0f), "enemy filter keeps an enemy kill");
            Expect(!FeedLog.PassesKill(other, 2, 0f), "friendly filter drops an enemy kill");
        }

        static void WindowFollowsReplayTime()
        {
            Expect(!FeedLog.Visible(10f, 9f, FeedLog.Kind.Kill, 10), "a future line stays hidden");
            Expect(FeedLog.Visible(10f, 12f, FeedLog.Kind.Kill, 10), "a fresh kill is on the feed");
            Expect(!FeedLog.Visible(10f, 20f, FeedLog.Kind.Kill, 10), "a kill leaves after its hold");
            Expect(FeedLog.Visible(10f, 16f, FeedLog.Kind.Mission, 4), "mission text stays longer than a kill");
        }

        static void SoundOnlyOnAShortForwardStep()
        {
            Expect(FeedLog.Crossed(1f, 1.2f, 1.1f, 0.2f, false), "walking forward crosses the click");
            Expect(!FeedLog.Crossed(1f, 1.2f, 1.1f, 0.2f, true), "pause does not click");
            Expect(!FeedLog.Crossed(5f, 1f, 1.1f, -4f, false), "reverse does not click");
            Expect(!FeedLog.Crossed(0f, 8f, 1.1f, 8f, false), "a seek does not click");
        }

        static void AllyPrefixIsInsideTheColour()
        {
            var item = new FeedLog.Item
            {
                Kind = FeedLog.Kind.Ally,
                A = "Sam",
                B = "hi",
                KR = 1,
                KG = 2,
                KB = 3
            };
            string line = FeedLog.FormatChat(item);
            int open = line.IndexOf("<color=#");
            int prefix = line.IndexOf("(ally)Sam:");
            int close = line.IndexOf("</color>");
            Expect(open >= 0 && prefix > open && close > prefix, "the ally prefix sits inside the colour tag");
        }

        static void RecordedDominationFile()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-feed.acmi");
            Expect(File.Exists(path), "synthetic acmi is beside the test");
            if (!File.Exists(path)) return;
            var feeds = new List<FeedLog.Item>();
            FeedLog.Collect(File.ReadAllText(path), feeds);
            int chat = 0, kill = 0, mission = 0;
            for (int i = 0; i < feeds.Count; i++)
            {
                if (feeds[i].Kind == FeedLog.Kind.Kill) kill++;
                else if (feeds[i].Kind == FeedLog.Kind.Mission) mission++;
                else chat++;
                if (i > 0) Expect(feeds[i].Time > feeds[i - 1].Time, "feed times increase");
            }
            Expect(chat == 5 && kill == 4 && mission == 0, "5 chat and 4 kills, chat=" + chat + " kill=" + kill);
            Expect(feeds.Count > 0 && feeds[0].B == "hello" && feeds[0].Time > 11f && feeds[0].Time < 12f, "first chat is hello at 11.8s");
        }

        static void SystemLineStaysPlain()
        {
            Expect(FeedLog.TryParse("S|" + FeedLog.Escape("Sam joined the game"), 3f, out var item), "system line parses");
            Expect(item.Kind == FeedLog.Kind.Server && item.B == "Sam joined the game" && item.Time == 3f, "system text is kept");
            Expect(FeedLog.FormatChat(item) == "Sam joined the game", "system replay text is the stored line");
        }

        static FeedLog.Item Kill(int type, float value, int flags)
        {
            return new FeedLog.Item
            {
                Kind = FeedLog.Kind.Kill,
                KillType = type,
                Value = value,
                Flags = flags,
                A = "A",
                B = "B"
            };
        }

        static void Expect(bool ok, string message)
        {
            if (ok) return;
            Failed++;
            System.Console.WriteLine("FAIL " + message);
        }
    }
}
