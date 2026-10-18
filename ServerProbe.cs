using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace TNovClient
{
    internal readonly record struct TimedProbe(bool Reachable, TimeSpan Elapsed);

    internal static class ServerProbe
    {
        internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
        internal static readonly TimeSpan SlowThreshold = TimeSpan.FromSeconds(1.5);

        private static readonly TimeSpan PositiveTtl = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan NegativeTtl = TimeSpan.FromSeconds(20);
        private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static bool IsServerReachable(string? serverPath) =>
            IsPathReachable(MarkerPath(serverPath));

        public static bool IsPathReachable(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            if (TryGetCached(path, out bool cached))
                return cached;

            Dictionary<string, TimedProbe> results = ProbeMany(new[] { path }, Timeout);
            return results.TryGetValue(path, out TimedProbe probe) && probe.Reachable;
        }

        public static Dictionary<string, TimedProbe> ProbeMany(IEnumerable<string> paths, TimeSpan timeout)
        {
            var distinct = paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var tasks = distinct.ToDictionary(
                path => path,
                path => Task.Run(() => TimedExists(path)),
                StringComparer.OrdinalIgnoreCase);

            if (tasks.Count > 0)
                Task.WaitAll(tasks.Values.ToArray(), timeout);

            var results = new Dictionary<string, TimedProbe>(StringComparer.OrdinalIgnoreCase);
            foreach ((string path, Task<TimedProbe> task) in tasks)
            {
                TimedProbe probe = task.IsCompletedSuccessfully
                    ? task.Result
                    : new TimedProbe(false, timeout);

                results[path] = probe;
                Cache[path] = new CacheEntry(DateTime.UtcNow, probe.Reachable);
                //Log($"{path}: reachable={probe.Reachable} in {probe.Elapsed.TotalMilliseconds:0} ms");
            }

            return results;
        }

        public static void Invalidate(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            Cache.TryRemove(path, out _);
            Cache.TryRemove(MarkerPath(path), out _);
        }

        public static void RememberReachable(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            Cache[path] = new CacheEntry(DateTime.UtcNow, true);
        }

        public static string MarkerPath(string? serverPath) =>
            Path.Combine(ClientPaths.NormalizeServerPath(serverPath ?? string.Empty), "clientusage.txt");

        private static bool TryGetCached(string path, out bool reachable)
        {
            reachable = false;
            if (!Cache.TryGetValue(path, out CacheEntry entry))
                return false;

            TimeSpan ttl = entry.Reachable ? PositiveTtl : NegativeTtl;
            if (DateTime.UtcNow - entry.AtUtc > ttl)
                return false;

            reachable = entry.Reachable;
            return true;
        }

        private static TimedProbe TimedExists(string path)
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                bool exists = File.Exists(path) || Directory.Exists(path);
                sw.Stop();
                return new TimedProbe(exists, sw.Elapsed);
            }
            catch (Exception ex)
            {
                sw.Stop();
                Debug.WriteLine($"ServerProbe failed ({path}): {ex.Message}");
                return new TimedProbe(false, sw.Elapsed);
            }
        }

        private static void Log(string message)
        {
            Debug.WriteLine("TNovClient server probe: " + message);
            try
            {
                Directory.CreateDirectory(ClientPaths.LogsFolder);
                File.AppendAllText(
                    Path.Combine(ClientPaths.LogsFolder, "server-probe.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }

        private readonly record struct CacheEntry(DateTime AtUtc, bool Reachable);
    }
}
