using System;
using System.Collections.Generic;
using System.IO;

namespace NGUAdvisor.Managers
{
    // Bounded tail reader for the advisor's own log files.
    //
    // Every disk-backed log view (LogSliver on combat.log / pitspin.log, the LOGS SESSION tab on
    // inject.log) refreshes on a timer while its page is open and only ever shows the last handful of
    // lines. The obvious implementation — ReadLine the whole file into a List, then walk it backwards —
    // costs the FULL file on every tick. inject.log grows for the whole session, and pitspin.log and
    // cards.log are opened in APPEND mode (Main.Start), so they carry every session ever: those reads
    // grow without bound while the cost per tick is fixed work the user never sees.
    //
    // So: seek to a bounded window at the end of the file and parse only that. Shared read (the writers
    // keep these files open, so FileShare.ReadWrite is mandatory).
    public static class LogTail
    {
        // Bytes to read back from EOF per requested line before giving up on filling `count`. Advisor log
        // lines are a timestamp plus a sentence; 512 covers them with room to spare.
        private const int BytesPerLine = 512;
        private const int MinWindow = 8 * 1024;

        // Newest-first, at most `count` non-blank lines. Empty list on any failure (a log view has
        // nothing useful to say about its own read errors).
        public static List<string> Read(string path, int count)
        {
            var newestFirst = new List<string>();
            try
            {
                if (count <= 0 || !File.Exists(path))
                    return newestFirst;

                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long window = Math.Max(MinWindow, (long)count * BytesPerLine);
                    long start = Math.Max(0, fs.Length - window);
                    fs.Seek(start, SeekOrigin.Begin);

                    using (var sr = new StreamReader(fs))
                    {
                        // Seeking lands mid-line: the first line read is a fragment of a line whose start
                        // we skipped, so drop it. At offset 0 nothing was skipped and it is a real line.
                        if (start > 0)
                            sr.ReadLine();

                        // Ring of the last `count` lines — avoids materialising the whole window.
                        var ring = new string[count];
                        int written = 0;
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (line.Trim().Length == 0)
                                continue;
                            ring[written % count] = line;
                            written++;
                        }

                        int have = Math.Min(written, count);
                        for (int i = 1; i <= have; i++)
                            newestFirst.Add(ring[(written - i) % count]);
                    }
                }
            }
            catch
            {
                newestFirst.Clear();
            }
            return newestFirst;
        }
    }
}
