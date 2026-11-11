using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using NuclearOption.SavedMission;
using NuclearOption.SceneLoading;
using UnityEngine;

namespace NOReplay
{
    internal class RecordWriter
    {
        private RecordFileWriter output;
        private readonly DateTime reference;
        public static TimeSpan lastUpdate;
        public static DateTime lastFlushTime;
        internal string filename;
        internal MapKey currentMapKey;
        internal RecordWriter(DateTime reference)
        {
            string dir = RecordConfig.OutputPath;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string missionName = MissionManager.CurrentMission?.Name ?? "Mission";
            foreach (char c in Path.GetInvalidFileNameChars())
                missionName = missionName.Replace(c, '_');
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            filename = RecordingPath.Acmi(dir, missionName, stamp, File.Exists);
            var created = new FileStream(filename, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            output = new RecordFileWriter(new StreamWriter(created, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)));
            //sb = new StringBuilder();
            this.reference = reference;
            lastUpdate = TimeSpan.MinValue;
            currentMapKey = MapSettingsManager.i.MapLoader.CurrentMap;

            Record.Log?.LogDebug("MAP NAME IS " + currentMapKey.Path);
            output.WriteLine("FileType=text/acmi/tacview");
            output.WriteLine("FileVersion=2.2");

            Dictionary<string, string> initProps = new()
            {
                { "ReferenceTime", reference.ToString("s") + "Z" },
                { "DataSource", $"Nuclear Option {Application.version}" },
                { "DataRecorder", $"NOBlackBox 0.3.8.5" },
                { "Author", Record.Player?.name.Replace(",", "\\,") ?? "Server" },
                { "RecordingTime", DateTime.Now.ToString("s") + "Z" },
                { "MapId", $"NuclearOption.{currentMapKey.Path}"},
            };

            Mission mission = MissionManager.CurrentMission;
            initProps.Add("Title", mission.Name.Replace(",", "\\,"));

            /*
            if (mission.missionSettings.description != null)
            {
                string briefing = mission.missionSettings.description.Replace(",", "\\,");
                if (briefing != "")
                    initProps.Add("Briefing", briefing);
            }
            */
            try
            {
                var li = UnityEngine.Object.FindObjectOfType<LevelInfo>();
                if (li != null)
                {
                    initProps.Add("TimeOfDay", li.timeOfDay.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                    initProps.Add("Conditions", li.conditions.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                    initProps.Add("CloudHeight", li.cloudHeight.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                    initProps.Add("WindSpeed", li.windSpeed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                    initProps.Add("WindTurbulence", li.windTurbulence.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                    if (li.moonPhase.HasValue)
                        initProps.Add("MoonPhase", li.moonPhase.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            catch { }
            output.WriteLine($"0,{StringifyProps(initProps)}");
            output.Flush();

            lastFlushTime = DateTime.Now;
        }

        ~RecordWriter()
        {
            Close();
        }

        public void UpdateObject(RecordObject aObject, DateTime updateTime)
        {
            if (_shut || aObject.props.Count == 0)
                return;

            TimeSpan diff = updateTime - reference;
            if (diff != lastUpdate)
            {
                lastUpdate = diff;
                //output.WriteLine("#" + diff.TotalSeconds);
                WriteLine("#" + diff.TotalSeconds.ToString(CultureInfo.InvariantCulture));
            }

            //output.WriteLine($"{aObject.id:X},{StringifyProps(props)}");
            WriteLine($"{aObject.tacviewId:X},{StringifyProps(aObject.props)}");
            //Record.Log.LogDebug($"Time Elapsed = {diff.TotalSeconds}");
        }

        internal void RemoveObject(RecordObject aObject, DateTime updateTime)
        {
            if (_shut) return;
            TimeSpan diff = updateTime - reference;
            if (diff != lastUpdate)
            {
                lastUpdate = diff;
                //output.WriteLine("#" + diff.TotalSeconds);
                WriteLine("#" + diff.TotalSeconds.ToString(CultureInfo.InvariantCulture));
            }

            WriteLine($"-{aObject.tacviewId:X}");
        }

        internal void WriteDestroyedEvent(RecordObject aObject, DateTime updateTime)
        {
            if (_shut) return;
            TimeSpan diff = updateTime - reference;
            if (diff != lastUpdate)
            {
                lastUpdate = diff;
                //output.WriteLine("#" + diff.TotalSeconds);
                WriteLine("#" + diff.TotalSeconds.ToString(CultureInfo.InvariantCulture));
            }

            if (RecordConfig.DestructionEvents.Value == true)
            {
                WriteLine("#" + diff.TotalSeconds.ToString(CultureInfo.InvariantCulture));
                if (aObject.destroyedEvent)
                {
                    WriteLine($"0,Event=Message|{aObject.tacviewId:X}|Has been destroyed.");
                }
            }
        }

        internal void WriteRepairedEvent(RecordObject aObject, DateTime updateTime)
        {
            if (_shut) return;
            TimeSpan diff = updateTime - reference;
            if (diff != lastUpdate)
            {
                lastUpdate = diff;
                //output.WriteLine("#" + diff.TotalSeconds);
                WriteLine("#" + diff.TotalSeconds.ToString(CultureInfo.InvariantCulture));
            }

            if (RecordConfig.DestructionEvents.Value == true)
            {
                WriteLine("#" + diff.TotalSeconds.ToString(CultureInfo.InvariantCulture));
                if (aObject.destroyedEvent)
                {
                    WriteLine($"0,Event=Message|{aObject.tacviewId:X}|Has been repaired.");
                }
            }
        }

        static readonly StringBuilder propText = new(256);

        private string StringifyProps(Dictionary<string, string> props)
        {
            propText.Clear();
            bool first = true;
            foreach (var kv in props)
            {
                if (!first) propText.Append(',');
                first = false;
                propText.Append(kv.Key).Append('=').Append(kv.Value ?? "");
            }
            return propText.ToString();
        }

        internal void WriteGlobal(string body, DateTime updateTime)
        {
            if (_shut || string.IsNullOrEmpty(body)) return;
            TimeSpan diff = updateTime - reference;
            if (diff != lastUpdate)
            {
                lastUpdate = diff;
                WriteLine("#" + diff.TotalSeconds.ToString(CultureInfo.InvariantCulture));
            }
            WriteLine("0," + body);
        }

        internal void WriteLine(string line)
        {
            if (_shut) return;
            if ((DateTime.Now - lastFlushTime).TotalSeconds > RecordConfig.AutoSaveInterval)
            {
                output.Flush();
                lastFlushTime = DateTime.Now;
                //output.Close();
                //output = new StreamWriter(filename, append: true);
            }
            output.WriteLine(line);
        }

        internal void Flush()
        {
            if (_shut) return;
            output.Flush();
        }

        int _packed;
        volatile bool _shut;

        internal void Close()
        {
            // OnDisable and OnDestroy both close. A second zip truncates the
            // archive after the first one deletes the text file, which is an
            // empty recording.
            if (System.Threading.Interlocked.Exchange(ref _packed, 1) != 0) return;
            _shut = true;
            try { output.Close(); }
            catch (Exception ex) { Record.Log?.LogError("ACMI close: " + ex.Message); }
            System.Threading.Tasks.Task.Run(Pack);
        }

        void Pack()
        {
            try
            {
                long bytes = new FileInfo(filename).Exists ? new FileInfo(filename).Length : 0;
                Record.Log?.LogInfo("ACMI " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes");
                if (bytes < 64)
                {
                    Record.Log?.LogError("ACMI blieb leer");
                    return;
                }
                string zipPath = RecordZip.ZipFileAsync(filename).GetAwaiter().GetResult();
                RecordZip.DeleteFileAsync(filename).GetAwaiter().GetResult();
                Record.Log?.LogInfo("ACMI gepackt " + zipPath);
            }
            catch (Exception ex) { Record.Log?.LogError("ACMI zip: " + ex.Message); }
        }
    }
}