using System;
using System.Globalization;
using System.IO;

namespace NOReplay
{
    /// <summary>
    /// Picks a recording path that does not replace an existing text file or zip.
    /// The first file in a second has no suffix. Later ones are _2, _3, ...
    /// </summary>
    internal static class RecordingPath
    {
        public static string Acmi(string directory, string missionName, string stamp, Func<string, bool> exists)
        {
            string stem = Path.Combine(directory, missionName + "_" + stamp);
            for (int n = 1; n < 10000; n++)
            {
                string suffix = n == 1 ? "" : "_" + n.ToString(CultureInfo.InvariantCulture);
                string acmi = stem + suffix + ".acmi";
                string zip = stem + suffix + ".zip.acmi";
                if (!exists(acmi) && !exists(zip)) return acmi;
            }
            throw new IOException("No free recording name for " + stem);
        }

        public static string ZipBeside(string acmiPath, Func<string, bool> exists)
        {
            string zip = Path.ChangeExtension(acmiPath, ".zip.acmi");
            if (!exists(zip)) return zip;
            string dir = Path.GetDirectoryName(acmiPath) ?? "";
            string name = Path.GetFileName(acmiPath);
            const string ext = ".acmi";
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - ext.Length);
            for (int n = 2; n < 10000; n++)
            {
                string candidate = Path.Combine(dir, name + "_" + n.ToString(CultureInfo.InvariantCulture) + ".zip.acmi");
                if (!exists(candidate)) return candidate;
            }
            throw new IOException("No free zip name for " + acmiPath);
        }
    }
}
