using BepInEx.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NOReplay
{
    internal class RecordFileWriter
    {
        private StreamWriter _writer;
        private object _stateLock = new object();
        private volatile bool _shouldFlush = false;
        private volatile bool _shouldClose = false;
        private ConcurrentQueue<string> _lineQueue;
        private Task _writeTask;

        public RecordFileWriter(StreamWriter writer)
        {
            _writer = writer;
            _lineQueue = new ConcurrentQueue<string>();
            _writeTask = Task.Run(() => _writeLoop());
        }

        private void _writeLoop()
        {
            Record.Log?.LogDebug("Starting ACMI StreamWriter Thread.");
            bool flushThisLoop = false;
            bool closeThisLoop = false;
            while (true)
            {
                lock (_stateLock)
                {
                    flushThisLoop = _shouldFlush;
                    closeThisLoop = _shouldClose;
                }

                if (closeThisLoop)
                {
                    while (!_lineQueue.IsEmpty)
                    {
                        //Empty the line queue
                        string newLine;
                        _lineQueue.TryDequeue(out newLine);
                        _writer.WriteLine(_lineQueue);
                    }
                    _writer.Flush();
                    _writer.Close();
                    Record.Log?.LogDebug("Exiting ACMI StreamWriter Thread.");
                    return;
                }

                if (flushThisLoop)
                {
                    Record.Log?.LogDebug("ACMI Flush started.");
                    _writer.Flush();
                    Record.Log?.LogDebug("ACMI Flush complete.");
                    lock (_stateLock)
                    {
                        _shouldFlush = false;
                        flushThisLoop = false;
                    }
                }

                var linesWrittenThisLoop = 0;
                while (!_lineQueue.IsEmpty && linesWrittenThisLoop < 100)
                {
                    string newLine;
                    _lineQueue.TryDequeue(out newLine);
                    _writer.WriteLine(newLine);
                    linesWrittenThisLoop++;
                }
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
        }

        public void Close()
        {
            //Set the close flag and wait for the task to finish.
            lock (_stateLock)
            {
                _shouldClose = true;
            }
            _writeTask.Wait();
        }

        public void WriteLine(string line)
        {
            _lineQueue.Enqueue(line);
        }
    }
}
