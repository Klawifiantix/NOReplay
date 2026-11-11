using System.Collections.Generic;
using System.IO;
using NOReplay;

namespace NOReplay.OrientationTests
{
    static class RecordingPathTests
    {
        public static int Failed;

        public static void Run()
        {
            FirstNameIsFree();
            OccupiedTextOrZipTakesTheNextSuffix();
            ZipBesideSkipsAnExistingArchive();
            if (Failed == 0) System.Console.WriteLine("RecordingPath ok");
        }

        static void FirstNameIsFree()
        {
            string path = RecordingPath.Acmi("replays", "Race", "2026-11-11_18-00-01", _ => false);
            Expect(path.EndsWith("Race_2026-11-11_18-00-01.acmi"), "a free second keeps the plain name");
        }

        static void OccupiedTextOrZipTakesTheNextSuffix()
        {
            var taken = new HashSet<string>
            {
                Path.Combine("replays", "Race_2026-11-11_18-00-01.acmi"),
                Path.Combine("replays", "Race_2026-11-11_18-00-01_2.zip.acmi")
            };
            string path = RecordingPath.Acmi("replays", "Race", "2026-11-11_18-00-01", taken.Contains);
            Expect(path.EndsWith("Race_2026-11-11_18-00-01_3.acmi"), "an existing text or zip pushes the suffix to _3");
        }

        static void ZipBesideSkipsAnExistingArchive()
        {
            string acmi = Path.Combine("replays", "Race_2026-11-11_18-00-01.acmi");
            string zip = RecordingPath.ZipBeside(acmi,
                p => p.EndsWith("Race_2026-11-11_18-00-01.zip.acmi"));
            Expect(zip.EndsWith("Race_2026-11-11_18-00-01_2.zip.acmi"), "zip does not replace an archive that is already there");
        }

        static void Expect(bool ok, string name)
        {
            if (ok) System.Console.WriteLine("ok  " + name);
            else
            {
                Failed++;
                System.Console.WriteLine("FAIL " + name);
            }
        }
    }
}
