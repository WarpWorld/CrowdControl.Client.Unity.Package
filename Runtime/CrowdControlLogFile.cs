using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using UnityEngine;

namespace CrowdControl.Client.Unity
{
    /// <summary>
    /// Writes Crowd Control log lines to a file on a background thread.
    /// </summary>
    /// <remarks>
    /// Log messages can originate on the WebSocket message pump, which must never block. Writes are therefore queued
    /// and drained by a dedicated thread; <see cref="Write"/> never performs file I/O and never throws. The writer
    /// flushes every line, so the tail of the log survives a hang or a force-kill, which is usually the situation the
    /// file was enabled to diagnose in the first place.
    /// </remarks>
    internal static class CrowdControlLogFile
    {
        /// <summary>Default file name used when no path is configured.</summary>
        private const string DEFAULT_FILE_NAME = "crowdcontrol.log";

        /// <summary>Default subfolder of <see cref="Application.persistentDataPath"/> used when no path is configured.</summary>
        private const string DEFAULT_FOLDER = "CrowdControl";

        /// <summary>How long to wait for the writer thread to drain and exit on close.</summary>
        private static readonly TimeSpan SHUTDOWN_TIMEOUT = TimeSpan.FromSeconds(2);

        private static readonly object s_lock = new();

        private static BlockingCollection<string>? s_queue;
        private static Thread? s_thread;

        /// <summary>Gets the full path currently being written to, or <see langword="null"/> if the sink is closed.</summary>
        public static string? CurrentPath { get; private set; }

        /// <summary>
        /// Resolves a user-supplied log path into an absolute file path.
        /// </summary>
        /// <param name="configuredPath">
        /// An absolute path, a path relative to <see cref="Application.persistentDataPath"/>, or empty for the default.
        /// A path naming a directory rather than a file receives the default file name.
        /// </param>
        /// <returns>An absolute file path.</returns>
        public static string ResolvePath(string? configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
                return Path.Combine(Application.persistentDataPath, DEFAULT_FOLDER, DEFAULT_FILE_NAME);

            string path = configuredPath!.Trim();

            if (!Path.IsPathRooted(path))
                path = Path.Combine(Application.persistentDataPath, path);

            //a trailing separator, or an existing directory, means they gave us a folder rather than a file
            if (Directory.Exists(path) || string.IsNullOrEmpty(Path.GetFileName(path)))
                path = Path.Combine(path, DEFAULT_FILE_NAME);

            return path;
        }

        /// <summary>
        /// Opens the sink at the given path, rotating any existing file to <c>.prev</c>.
        /// </summary>
        /// <param name="path">The absolute file path to write to.</param>
        /// <returns><see langword="true"/> if the sink was opened.</returns>
        public static bool Open(string path)
        {
            lock (s_lock)
            {
                if (s_queue != null) return true;

                StreamWriter writer;
                try
                {
                    string? directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory!);

                    RotateExisting(path);

                    writer = new StreamWriter(path, append: false) { AutoFlush = true };
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Crowd Control could not open the log file at \"{path}\": {ex.Message}");
                    return false;
                }

                BlockingCollection<string> queue = new(new ConcurrentQueue<string>());
                s_queue = queue;
                CurrentPath = path;

                s_thread = new Thread(() => WriteLoop(writer, queue))
                {
                    IsBackground = true,
                    Name = "CrowdControl Log File"
                };
                s_thread.Start();

                return true;
            }
        }

        /// <summary>Queues a line for writing. Never blocks and never throws.</summary>
        /// <param name="line">The line to write.</param>
        public static void Write(string line)
        {
            BlockingCollection<string>? queue = s_queue;
            if (queue == null) return;

            try { queue.TryAdd(line); }
            catch { /* closed while writing */ }
        }

        /// <summary>Drains the queue and closes the file.</summary>
        public static void Close()
        {
            Thread? thread;
            lock (s_lock)
            {
                if (s_queue == null) return;

                try { s_queue.CompleteAdding(); }
                catch { /* already completed */ }

                s_queue = null;
                thread = s_thread;
                s_thread = null;
                CurrentPath = null;
            }

            try { thread?.Join(SHUTDOWN_TIMEOUT); }
            catch { /* nothing useful to do */ }
        }

        /// <summary>Moves an existing log to a <c>.prev</c> sibling so the previous run is not lost.</summary>
        /// <param name="path">The log path about to be opened.</param>
        private static void RotateExisting(string path)
        {
            try
            {
                if (!File.Exists(path)) return;

                string previous = Path.ChangeExtension(path, "prev");
                if (File.Exists(previous)) File.Delete(previous);
                File.Move(path, previous);
            }
            catch { /* a rotation failure must not stop logging */ }
        }

        /// <summary>Drains queued lines to the file until the sink is closed.</summary>
        /// <param name="writer">The open writer, disposed when the loop exits.</param>
        /// <param name="queue">The queue to drain.</param>
        private static void WriteLoop(StreamWriter writer, BlockingCollection<string> queue)
        {
            try
            {
                foreach (string line in queue.GetConsumingEnumerable())
                {
                    try { writer.WriteLine(line); }
                    catch { /* keep draining; a failed line must not kill the writer */ }
                }
            }
            catch (ObjectDisposedException) { /* disposed while waiting */ }
            catch (InvalidOperationException) { /* completed while waiting */ }
            finally
            {
                try { writer.Dispose(); }
                catch { /* nothing useful to do */ }
                try { queue.Dispose(); }
                catch { /* nothing useful to do */ }
            }
        }
    }
}
