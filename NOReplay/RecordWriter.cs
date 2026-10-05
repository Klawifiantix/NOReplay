using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
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

            string basename = Path.Combine(dir, DateTime.Now.ToString("s").Replace(":", "-"));
            basename += "_" + MissionManager.CurrentMission.Name + "_" + MapSettingsManager.i.MapLoader.CurrentMap.Path;
            filename = basename + ".acmi";
            int postfix = 0;
            while (File.Exists(filename))
                filename = basename + $" ({++postfix}).acmi";

            output = new RecordFileWriter(File.CreateText(filename));
            //sb = new StringBuilder();
            this.reference = reference;
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
            if (aObject.props.Count == 0)
                return;

            TimeSpan diff = updateTime - reference;
            if (diff != lastUpdate)
            {
                lastUpdate = diff;
                //output.WriteLine("#" + diff.TotalSeconds);
                WriteLine("#" + diff.TotalSeconds);
            }

            //output.WriteLine($"{aObject.id:X},{StringifyProps(props)}");
            WriteLine($"{aObject.tacviewId:X},{StringifyProps(aObject.props)}");
            //Record.Log.LogDebug($"Time Elapsed = {diff.TotalSeconds}");
        }

        internal void RemoveObject(RecordObject aObject, DateTime updateTime)
        {

            TimeSpan diff = updateTime - reference;
            if (diff != lastUpdate)
            {
                lastUpdate = diff;
                //output.WriteLine("#" + diff.TotalSeconds);
                WriteLine("#" + diff.TotalSeconds);
            }

            WriteLine($"-{aObject.tacviewId:X}");
        }

        internal void WriteDestroyedEvent(RecordObject aObject, DateTime updateTime)
        {
            TimeSpan diff = updateTime - reference;
            if (diff != lastUpdate)
            {
                lastUpdate = diff;
                //output.WriteLine("#" + diff.TotalSeconds);
                WriteLine("#" + diff.TotalSeconds);
            }

            if (RecordConfig.DestructionEvents.Value == true)
            {
                WriteLine("#" + diff.TotalSeconds);
                if (aObject.destroyedEvent)
                {
                    WriteLine($"0,Event=Message|{aObject.tacviewId:X}|Has been destroyed.");
                }
            }
        }

        internal void WriteRepairedEvent(RecordObject aObject, DateTime updateTime)
        {
            TimeSpan diff = updateTime - reference;
            if (diff != lastUpdate)
            {
                lastUpdate = diff;
                //output.WriteLine("#" + diff.TotalSeconds);
                WriteLine("#" + diff.TotalSeconds);
            }

            if (RecordConfig.DestructionEvents.Value == true)
            {
                WriteLine("#" + diff.TotalSeconds);
                if (aObject.destroyedEvent)
                {
                    WriteLine($"0,Event=Message|{aObject.tacviewId:X}|Has been repaired.");
                }
            }
        }

        private string StringifyProps(Dictionary<string, string> props)
        {
            string[] propStrings = props.Select(x => x.Key + "=" + x.Value/*.Replace(",", "\\,")*/).ToArray();
            return string.Join(",", propStrings);
        }

        internal void WriteLine(string line)
        {
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
            output.Flush();
        }

        internal async void Close()
        {
            output.Close();
            string zipPath = await RecordZip.ZipFileAsync(filename);
            Record.Log?.LogDebug($"saving compressed acmi {zipPath}");
            await RecordZip.DeleteFileAsync(filename);
            Record.Log?.LogDebug($"deleted uncompressed acmi {filename}");
        }
    }
}
