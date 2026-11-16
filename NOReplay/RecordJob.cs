using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace NOReplay
{
    /// <summary>
    /// One ACMI update. The main thread fills numbers and already-built
    /// property strings. The file thread turns that into text. No Unity
    /// object is stored here.
    /// </summary>
    internal sealed class RecordJob
    {
        public double Time;
        public long Id;
        public bool HasPose;
        public float X, Y, Z, Qx, Qy, Qz, Qw;
        public int PropCount;
        string[] _keys = new string[8];
        string[] _vals = new string[8];

        static readonly ConcurrentBag<RecordJob> Pool = new ConcurrentBag<RecordJob>();
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static RecordJob Rent()
        {
            if (!Pool.TryTake(out var job)) job = new RecordJob();
            job.Time = 0;
            job.Id = 0;
            job.HasPose = false;
            job.PropCount = 0;
            return job;
        }

        public static void Return(RecordJob job)
        {
            if (job == null) return;
            int n = job.PropCount;
            for (int i = 0; i < n; i++)
            {
                job._keys[i] = null;
                job._vals[i] = null;
            }
            job.PropCount = 0;
            Pool.Add(job);
        }

        public void AddProp(string key, string value)
        {
            int n = PropCount;
            if (n >= _keys.Length)
            {
                int size = _keys.Length * 2;
                var keys = new string[size];
                var vals = new string[size];
                for (int i = 0; i < n; i++)
                {
                    keys[i] = _keys[i];
                    vals[i] = _vals[i];
                }
                _keys = keys;
                _vals = vals;
            }
            _keys[n] = key;
            _vals[n] = value ?? "";
            PropCount = n + 1;
        }

        public void Write(System.IO.TextWriter writer, StringBuilder sb, ref double lastTime)
        {
            if (double.IsNaN(lastTime) || Time != lastTime)
            {
                writer.Write('#');
                writer.WriteLine(Time.ToString(Inv));
                lastTime = Time;
            }
            sb.Clear();
            sb.Append(Id.ToString("X", Inv));
            if (HasPose)
            {
                sb.Append(",T=");
                sb.Append(X.ToString(Inv));
                sb.Append('|');
                sb.Append(Y.ToString(Inv));
                sb.Append('|');
                sb.Append(Z.ToString(Inv));
                sb.Append(",Q=");
                sb.Append(Qx.ToString(Inv));
                sb.Append('|');
                sb.Append(Qy.ToString(Inv));
                sb.Append('|');
                sb.Append(Qz.ToString(Inv));
                sb.Append('|');
                sb.Append(Qw.ToString(Inv));
            }
            int n = PropCount;
            for (int i = 0; i < n; i++)
            {
                sb.Append(',');
                sb.Append(_keys[i]);
                sb.Append('=');
                sb.Append(_vals[i]);
            }
            writer.Write(sb);
            writer.WriteLine();
        }
    }
}
