using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NOReplay
{
    internal class RecordFileWriter
    {
        private readonly StreamWriter _writer;
        private readonly object _stateLock = new object();
        private volatile bool _shouldFlush;
        private volatile bool _shouldClose;
        private readonly ConcurrentQueue<object> _lineQueue = new ConcurrentQueue<object>();
        private readonly ManualResetEventSlim _signal = new ManualResetEventSlim(false);
        private readonly Task _writeTask;
        private readonly StringBuilder _format = new StringBuilder(256);
        private double _lastTime = double.NaN;
        private int _closed;

        public RecordFileWriter(StreamWriter writer)
        {
            _writer = writer;
            _writeTask = Task.Run((Action)_writeLoop);
        }

        void Drain()
        {
            while (_lineQueue.TryDequeue(out var item))
            {
                if (item is string line)
                    _writer.WriteLine(line);
                else if (item is RecordJob job)
                {
                    job.Write(_writer, _format, ref _lastTime);
                    RecordJob.Return(job);
                }
            }
        }

        private void _writeLoop()
        {
            Record.Log?.LogDebug("Starting ACMI StreamWriter Thread.");
            while (true)
            {
                _signal.Wait();
                Drain();

                bool flush;
                bool close;
                lock (_stateLock)
                {
                    flush = _shouldFlush;
                    close = _shouldClose;
                    if (flush)
                        _shouldFlush = false;
                }

                if (close)
                {
                    Drain();
                    _writer.Flush();
                    _writer.Close();
                    Record.Log?.LogDebug("Exiting ACMI StreamWriter Thread.");
                    return;
                }

                if (flush)
                {
                    Record.Log?.LogDebug("ACMI Flush started.");
                    _writer.Flush();
                    Record.Log?.LogDebug("ACMI Flush complete.");
                }

                _signal.Reset();
                if (!_lineQueue.IsEmpty || _shouldFlush || _shouldClose)
                    _signal.Set();
            }
        }

        public int PendingLineCount()
        {
            return _lineQueue.Count;
        }

        public void Flush()
        {
            Record.Log?.LogDebug("ACMI Flush requested.");
            lock (_stateLock)
            {
                _shouldFlush = true;
            }
            _signal.Set();
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
            {
                try { _writeTask.Wait(); } catch { }
                return;
            }
            lock (_stateLock)
            {
                _shouldClose = true;
            }
            _signal.Set();
            _writeTask.Wait();
        }

        public void WriteLine(string line)
        {
            _lineQueue.Enqueue(line);
            _signal.Set();
        }

        public void Enqueue(RecordJob job)
        {
            _lineQueue.Enqueue(job);
            _signal.Set();
        }
    }
}
