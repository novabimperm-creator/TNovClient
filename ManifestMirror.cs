using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace TNovClient
{
    /// <summary>
    /// Локальное зеркало actual\ по манифесту выпуска (TNovReleaser): файлы со ссылками на
    /// GitHub Releases и SHA-256. Скачиваются только файлы, чей хэш отличается от локального.
    /// Манифест офиса — общий на шаре ({ServerPath}manifest.json), личной — на GitHub Pages.
    /// Дальше обновление плагина и самообновление клиента работают с зеркалом как с шарой
    /// (см. ClientPaths.UpdateRoot). Сеть недоступна — работаем с тем, что уже скачано.
    /// </summary>
    internal static class ManifestMirror
    {
        private const int SupportedSchema = 1;
        private const string StateFileName = "state.json";

        /// <summary>Файлы данных, которые плагин читает из ServerPath (в личной — папка TNovClient).</summary>
        private static readonly string[] DataFiles = { "releases", "tnovapi.json" };

        // Копию на шаре читать дёшево; Pages — редко: личная и офис вне сети компании.
        private static readonly TimeSpan OfficeCooldown = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan RemoteCooldown = TimeSpan.FromHours(3);
        private static readonly TimeSpan ShareReadTimeout = TimeSpan.FromSeconds(30);

        // Копию на шаре обновляет тот клиент офиса, который первым заметил, что она старше
        // RelayInterval: на Pages ходит один клиент на офис, а не каждый.
        private static readonly TimeSpan RelayInterval = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan RelayLockTimeout = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan RelayTimeout = TimeSpan.FromSeconds(90);

        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static readonly HttpClient Http = CreateHttpClient();
        private static readonly Dictionary<string, DateTime> LastSyncUtc = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, (long Length, DateTime WriteUtc, string Sha256)> HashCache =
            new(StringComparer.OrdinalIgnoreCase);

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            string version = typeof(ManifestMirror).Assembly.GetName().Version?.ToString(3) ?? "0";
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"TNovClient/{version}");
            return client;
        }

        public static string MirrorRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TNovClient", "mirror");

        public static string ActualFolder => Path.Combine(MirrorRoot, "actual");

        private static string StateFile => Path.Combine(MirrorRoot, StateFileName);

        /// <summary>В зеркале уже есть что ставить.</summary>
        public static bool HasContent =>
            Directory.Exists(ActualFolder) && Directory.EnumerateFileSystemEntries(ActualFolder).Any();

        /// <summary>Зеркало собрано по манифесту этой конфигурации.</summary>
        public static bool IsReady(TNovConfig config)
        {
            MirrorState? state = LoadState();
            return state != null && HasContent
                && string.Equals(state.Source, ClientPaths.ManifestLocation(config), StringComparison.OrdinalIgnoreCase);
        }

        private sealed class Manifest
        {
            [JsonProperty("schema")] public int Schema { get; set; }
            [JsonProperty("channel")] public string? Channel { get; set; }
            [JsonProperty("release")] public string? Release { get; set; }
            [JsonProperty("files")] public List<ManifestFile> Files { get; set; } = new();
        }

        private sealed class ManifestFile
        {
            [JsonProperty("path")] public string Path { get; set; } = "";
            [JsonProperty("url")] public string Url { get; set; } = "";
            [JsonProperty("sha256")] public string Sha256 { get; set; } = "";
            [JsonProperty("size")] public long Size { get; set; }
            [JsonProperty("version")] public string? Version { get; set; }
        }

        private sealed class MirrorState
        {
            public string Source { get; set; } = "";
            public string? ETag { get; set; }
            public string? Release { get; set; }
        }

        /// <summary>
        /// Синхронизировать зеркало, если подошёл срок (или force). true — что-то изменилось.
        /// </summary>
        public static async Task<bool> SyncIfDueAsync(TNovConfig? config, bool force = false, CancellationToken token = default)
        {
            if (config == null)
                return false;

            if (!await Gate.WaitAsync(0, token))
                return false;
            try
            {
                string location = ClientPaths.ManifestLocation(config);
                // В офисе (шара доступна) манифест читается с шары; вне офиса — с Pages, редко.
                bool inOffice = config.IsCorp
                    && string.Equals(location, OfficeServers.CorpManifestUrl, StringComparison.OrdinalIgnoreCase)
                    && ServerProbe.IsServerReachable(config.ServerPath);
                TimeSpan cooldown = inOffice ? OfficeCooldown : RemoteCooldown;
                if (!force && LastSyncUtc.TryGetValue(location, out DateTime last) && DateTime.UtcNow - last < cooldown)
                    return false;
                LastSyncUtc[location] = DateTime.UtcNow;

                MirrorState? state = LoadState();
                bool sameSource = state != null && HasContent
                    && string.Equals(state.Source, location, StringComparison.OrdinalIgnoreCase);

                string? text = null, etag = null;
                if (inOffice)
                    text = await ReadShareCopyAsync(config, token);
                // Копии на шаре нет (её ещё никто не создал, нет прав на запись) — напрямую.
                if (text == null)
                {
                    (text, etag) = await ReadManifestAsync(location, sameSource ? state!.ETag : null, token);
                    if (text == null)
                        return false;
                }

                Manifest? manifest = JsonConvert.DeserializeObject<Manifest>(text);
                if (manifest == null || manifest.Files.Count == 0)
                {
                    // Пустой манифест — скорее ошибка публикации, чем пустой выпуск: зеркало не трогаем.
                    Log($"манифест {location} пуст — зеркало не изменено");
                    return false;
                }
                if (manifest.Schema > SupportedSchema)
                {
                    Log($"манифест {location}: схема {manifest.Schema} новее поддерживаемой ({SupportedSchema})");
                    return false;
                }

                var remote = new Dictionary<string, ManifestFile>(StringComparer.OrdinalIgnoreCase);
                foreach (ManifestFile file in manifest.Files)
                {
                    string? relative = ToRelativePath(file.Path);
                    if (relative == null || string.IsNullOrWhiteSpace(file.Url) || string.IsNullOrWhiteSpace(file.Sha256))
                    {
                        Log($"пропущена запись манифеста: {file.Path}");
                        continue;
                    }
                    remote[relative] = file;
                }

                int downloaded = 0, deleted = 0;

                // Сначала всё изменённое — во временную папку, и только если скачалось всё,
                // переносим в зеркало: иначе клиент мог бы поставить смесь старых и новых DLL.
                string staging = Path.Combine(MirrorRoot, "staging");
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);

                var pending = new List<string>();
                foreach ((string relative, ManifestFile file) in remote)
                {
                    if (Matches(Path.Combine(ActualFolder, relative), file))
                        continue;
                    await DownloadAsync(config, relative, file, Path.Combine(staging, relative), token);
                    pending.Add(relative);
                }

                foreach (string relative in pending)
                {
                    string local = Path.Combine(ActualFolder, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(local)!);
                    File.Move(Path.Combine(staging, relative), local, overwrite: true);
                    downloaded++;
                }
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);

                if (Directory.Exists(ActualFolder))
                {
                    foreach (string path in Directory.EnumerateFiles(ActualFolder, "*", SearchOption.AllDirectories).ToList())
                    {
                        string relative = Path.GetRelativePath(ActualFolder, path);
                        if (remote.ContainsKey(relative))
                            continue;
                        try
                        {
                            File.Delete(path);
                            deleted++;
                        }
                        catch (Exception ex) { Log($"не удалён {relative}: {ex.Message}"); }
                    }
                }

                SaveState(new MirrorState { Source = location, ETag = etag, Release = manifest.Release });
                CopyDataFiles(config);

                if (downloaded > 0 || deleted > 0)
                    Log($"зеркало обновлено ({manifest.Release}): скачано {downloaded}, удалено {deleted}");
                return downloaded > 0 || deleted > 0;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                Log("синхронизация не удалась: " + ex.Message);
                return false;
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>Текст манифеста (https или путь к файлу); null — не изменился (304) или недоступен.</summary>
        private static async Task<(string? Text, string? ETag)> ReadManifestAsync(
            string location, string? etag, CancellationToken token)
        {
            if (!IsHttp(location))
            {
                Task<string?> read = Task.Run(() => File.Exists(location) ? File.ReadAllText(location) : null, token);
                string? text = await read.WaitAsync(ShareReadTimeout, token);
                if (text == null)
                    Log($"нет манифеста {location}");
                return (text, null);
            }

            (HttpStatusCode status, string? body, string? newEtag) = await GetConditionalAsync(location, etag, token);
            if (status == HttpStatusCode.NotModified)
                return (null, null);
            if (body == null)
            {
                Log($"манифест {location}: {(int)status}");
                return (null, null);
            }
            return (body, newEtag);
        }

        private static async Task<(HttpStatusCode Status, string? Body, string? ETag)> GetConditionalAsync(
            string url, string? etag, CancellationToken token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(etag) && EntityTagHeaderValue.TryParse(etag, out EntityTagHeaderValue? parsed))
                request.Headers.IfNoneMatch.Add(parsed);
            using HttpResponseMessage response = await Http.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
                return (response.StatusCode, null, null);
            return (response.StatusCode, await response.Content.ReadAsStringAsync(token), response.Headers.ETag?.ToString());
        }

        /// <summary>
        /// Копия корпоративного манифеста на шаре офиса; если она устарела — сначала обновить её
        /// с Pages (это делает один клиент, остальные ждут его результата). null — копии нет.
        /// </summary>
        private static async Task<string?> ReadShareCopyAsync(TNovConfig config, CancellationToken token)
        {
            string copy = ClientPaths.ShareManifestCopy(config);
            try
            {
                await RelayAsync(config, copy, token).WaitAsync(RelayTimeout, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { Log("копия манифеста на шаре не обновлена: " + ex.Message); }

            Task<string?> read = Task.Run(() => File.Exists(copy) ? File.ReadAllText(copy) : null, token);
            return await read.WaitAsync(ShareReadTimeout, token);
        }

        /// <summary>
        /// Обновить копию манифеста на шаре, если она старше RelayInterval. Замок manifest.lock
        /// (CreateNew) пропускает одного клиента; брошенный замок старше RelayLockTimeout снимается.
        /// Не изменился (304) — копия только «трогается», чтобы следующие клиенты не ходили на Pages.
        /// Вместе с манифестом на шару кладётся журнал releases — его читает плагин.
        /// </summary>
        private static async Task RelayAsync(TNovConfig config, string copy, CancellationToken token)
        {
            if (IsFresh(copy))
                return;

            string share = Path.GetDirectoryName(copy)!;
            string lockFile = Path.Combine(share, "manifest.lock");
            string etagFile = Path.Combine(share, "manifest.etag");
            try
            {
                if (File.Exists(lockFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(lockFile) > RelayLockTimeout)
                    File.Delete(lockFile);
            }
            catch (IOException) { }

            FileStream gate;
            try
            {
                gate = new FileStream(lockFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException)
            {
                return; // Обновляет другой клиент.
            }

            await using (gate)
            {
                if (IsFresh(copy))
                    return;

                string? etag = File.Exists(copy) && File.Exists(etagFile) ? File.ReadAllText(etagFile).Trim() : null;
                (HttpStatusCode status, string? body, string? newEtag) =
                    await GetConditionalAsync(OfficeServers.CorpManifestUrl, etag, token);
                if (status == HttpStatusCode.NotModified)
                {
                    File.SetLastWriteTimeUtc(copy, DateTime.UtcNow);
                    return;
                }
                if (body == null)
                {
                    Log($"копия манифеста: Pages ответил {(int)status}");
                    return;
                }

                Manifest? manifest = JsonConvert.DeserializeObject<Manifest>(body);
                if (manifest == null || manifest.Files.Count == 0)
                {
                    Log("копия манифеста: на Pages пустой манифест — копия не изменена");
                    return;
                }

                await CopyJournalToShareAsync(manifest, share, token);

                string temp = copy + ".tmp";
                await File.WriteAllTextAsync(temp, body, token);
                File.Move(temp, copy, overwrite: true);
                if (newEtag != null)
                    await File.WriteAllTextAsync(etagFile, newEtag, token);
                else if (File.Exists(etagFile))
                    File.Delete(etagFile);
                Log($"копия манифеста на шаре обновлена ({manifest.Release})");
            }
        }

        private static bool IsFresh(string copy) =>
            File.Exists(copy) && DateTime.UtcNow - File.GetLastWriteTimeUtc(copy) < RelayInterval;

        /// <summary>Журнал releases из манифеста — на шару ({ServerPath}releases), если он изменился.</summary>
        private static async Task CopyJournalToShareAsync(Manifest manifest, string share, CancellationToken token)
        {
            ManifestFile? journal = manifest.Files.FirstOrDefault(file =>
                string.Equals(file.Path, "releases", StringComparison.OrdinalIgnoreCase));
            if (journal == null)
                return;

            string target = Path.Combine(share, "releases");
            if (Matches(target, journal))
                return;

            byte[] data = await Http.GetByteArrayAsync(journal.Url, token);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(data)), journal.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Log("журнал releases: хэш не совпал — на шару не записан");
                return;
            }

            string temp = target + ".tmp";
            await File.WriteAllBytesAsync(temp, data, token);
            File.Move(temp, target, overwrite: true);
        }

        private static async Task DownloadAsync(TNovConfig config, string relative, ManifestFile file, string localPath, CancellationToken token)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            var errors = new List<string>();

            try
            {
                using (HttpResponseMessage response = await Http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        await using FileStream output = File.Create(localPath);
                        await response.Content.CopyToAsync(output, token);
                    }
                    else
                    {
                        errors.Add($"{(int)response.StatusCode}");
                    }
                }

                if (File.Exists(localPath))
                {
                    if (Matches(localPath, file))
                        return;
                    File.Delete(localPath);
                    errors.Add("хэш не совпал");
                }
            }
            catch (HttpRequestException ex) { errors.Add(ex.Message); }
            catch (TaskCanceledException) when (!token.IsCancellationRequested) { errors.Add("таймаут"); }

            // Резерв офиса: GitHub недоступен — тот же файл с шары, если его туда продублировал
            // релизер. Устаревшую копию отсекает хэш.
            if (config.IsCorp && ServerProbe.IsServerReachable(config.ServerPath))
            {
                string shareFile = Path.Combine(ClientPaths.NormalizeServerPath(config.ServerPath), "actual", relative);
                if (await Task.Run(() => File.Exists(shareFile) && Matches(shareFile, file), token))
                {
                    File.Copy(shareFile, localPath, overwrite: true);
                    if (Matches(localPath, file))
                    {
                        Log($"{relative}: GitHub недоступен ({string.Join(" | ", errors)}), взят с шары");
                        return;
                    }
                }
            }

            throw new HttpRequestException($"скачивание {relative}: {string.Join(" | ", errors)}");
        }

        private static bool Matches(string path, ManifestFile file) =>
            File.Exists(path)
            && new FileInfo(path).Length == file.Size
            && string.Equals(ComputeSha256(path), file.Sha256, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// SHA-256 файла (hex, нижний регистр). Кэш по размеру и времени записи — повторные
        /// проверки каждые несколько секунд не читают файлы заново.
        /// </summary>
        public static string ComputeSha256(string path)
        {
            var info = new FileInfo(path);
            if (HashCache.TryGetValue(path, out var cached)
                && cached.Length == info.Length && cached.WriteUtc == info.LastWriteTimeUtc)
                return cached.Sha256;

            using FileStream stream = File.OpenRead(path);
            string sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            HashCache[path] = (info.Length, info.LastWriteTimeUtc, sha);
            return sha;
        }

        /// <summary>Путь манифеста («2022/TNov/x.dll») в относительный путь Windows; выход за actual\ — null.</summary>
        private static string? ToRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            string relative = path.Replace('/', '\\').Trim('\\');
            if (Path.IsPathRooted(relative)
                || relative.Split('\\').Any(part => part.Length == 0 || part == "." || part == ".."
                                                    || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                return null;
            return relative;
        }

        private static bool IsHttp(string location) =>
            location.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || location.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// releases и tnovapi.json плагин читает из ServerPath. В личной конфигурации это
        /// папка TNovClient, поэтому копируем их туда из зеркала.
        /// </summary>
        private static void CopyDataFiles(TNovConfig config)
        {
            if (config.IsCorp || string.IsNullOrWhiteSpace(config.ServerPath))
                return;

            string target = ClientPaths.NormalizeServerPath(config.ServerPath);
            foreach (string name in DataFiles)
            {
                string source = Path.Combine(ActualFolder, name);
                if (!File.Exists(source))
                    continue;
                try
                {
                    string destination = Path.Combine(target, name);
                    if (File.Exists(destination)
                        && string.Equals(ComputeSha256(destination), ComputeSha256(source), StringComparison.Ordinal))
                        continue;
                    Directory.CreateDirectory(target);
                    File.Copy(source, destination, overwrite: true);
                }
                catch (Exception ex) { Log($"не скопирован {name}: {ex.Message}"); }
            }
        }

        private static MirrorState? LoadState()
        {
            try
            {
                if (File.Exists(StateFile))
                    return JsonConvert.DeserializeObject<MirrorState>(File.ReadAllText(StateFile));
            }
            catch (Exception ex) { Log("состояние зеркала не прочитано: " + ex.Message); }
            return null;
        }

        private static void SaveState(MirrorState state)
        {
            try
            {
                Directory.CreateDirectory(MirrorRoot);
                File.WriteAllText(StateFile, JsonConvert.SerializeObject(state, Formatting.Indented));
            }
            catch (Exception ex) { Log("состояние зеркала не записано: " + ex.Message); }
        }

        private static void Log(string message)
        {
            Debug.WriteLine("TNovClient mirror: " + message);
            try
            {
                Directory.CreateDirectory(ClientPaths.LogsFolder);
                File.AppendAllText(
                    Path.Combine(ClientPaths.LogsFolder, "mirror.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }
}
