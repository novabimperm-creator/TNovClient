using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace TNovClient
{
    /// <summary>
    /// Локальное зеркало папки actual\ из публичной папки Google Drive (личная конфигурация).
    /// Drive API v3 по API-ключу: список файлов с md5, скачивание изменённых, удаление лишних.
    /// Дальше обновление плагина и самообновление клиента работают с зеркалом как с шарой
    /// (см. ClientPaths.UpdateRoot). Сеть недоступна — работаем с тем, что уже скачано.
    /// </summary>
    internal static class GoogleDriveMirror
    {
        private const string ApiBase = "https://www.googleapis.com/drive/v3/files";
        private const string FolderMimeType = "application/vnd.google-apps.folder";
        private const string StateFileName = "mirror-state.json";

        /// <summary>Что берём из папки на Drive; остальное (desktop\ и т.п.) пропускаем.</summary>
        private static readonly HashSet<string> TopLevelNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "addin", "2022", "2027", "client", "clientversion.txt", "releases", "tnovapi.json"
        };

        /// <summary>Файлы данных, которые плагин читает из ServerPath (в личной — папка TNovClient).</summary>
        private static readonly string[] DataFiles = { "releases", "tnovapi.json" };

        private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(15);
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static readonly HttpClient Http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            string version = typeof(GoogleDriveMirror).Assembly.GetName().Version?.ToString(3) ?? "0";
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"TNovClient/{version}");
            return client;
        }
        private static DateTime _lastSyncUtc = DateTime.MinValue;
        private static DateTime _apiDownloadBlockedUntilUtc = DateTime.MinValue;

        public static string MirrorRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TNovClient", "mirror");

        public static string ActualFolder => Path.Combine(MirrorRoot, "actual");

        private static string StateFile => Path.Combine(MirrorRoot, StateFileName);

        /// <summary>В зеркале уже есть что ставить.</summary>
        public static bool HasContent =>
            Directory.Exists(ActualFolder) && Directory.EnumerateFileSystemEntries(ActualFolder).Any();

        private sealed class DriveFile
        {
            public string Id = "";
            public string Md5 = "";
            public DateTime Modified;
        }

        private sealed class LocalState
        {
            public string Md5 { get; set; } = "";
            public long Length { get; set; }
            public DateTime WriteUtc { get; set; }
        }

        /// <summary>
        /// Синхронизировать зеркало, если подошёл срок (или force). true — что-то изменилось.
        /// Без внешнего источника в конфиге ничего не делает.
        /// </summary>
        public static async Task<bool> SyncIfDueAsync(TNovConfig? config, bool force = false, CancellationToken token = default)
        {
            UpdateSourceConfig? source = config?.UpdateSource;
            if (source == null || !source.IsGoogleDrive)
                return false;

            if (!await Gate.WaitAsync(0, token))
                return false;
            try
            {
                if (!force && DateTime.UtcNow - _lastSyncUtc < Cooldown)
                    return false;
                _lastSyncUtc = DateTime.UtcNow;

                var remote = new Dictionary<string, DriveFile>(StringComparer.OrdinalIgnoreCase);
                await ListFolderAsync(source, source.FolderId!, "", remote, token);
                if (remote.Count == 0)
                {
                    // Пустой ответ — скорее ошибка доступа к папке, чем пустой релиз: зеркало не трогаем.
                    Log("на Drive нет файлов (проверьте доступ к папке) — зеркало не изменено");
                    return false;
                }

                Dictionary<string, LocalState> state = LoadState();
                int downloaded = 0, deleted = 0;

                // Сначала всё изменённое — во временную папку, и только если скачалось всё,
                // переносим в зеркало: иначе клиент мог бы поставить смесь старых и новых DLL.
                string staging = Path.Combine(MirrorRoot, "staging");
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);

                var pending = new List<string>();
                foreach ((string relative, DriveFile file) in remote)
                {
                    if (IsUpToDate(Path.Combine(ActualFolder, relative), relative, file.Md5, state))
                        continue;
                    await DownloadAsync(source, file, Path.Combine(staging, relative), token);
                    pending.Add(relative);
                }

                foreach (string relative in pending)
                {
                    string local = Path.Combine(ActualFolder, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(local)!);
                    File.Move(Path.Combine(staging, relative), local, overwrite: true);
                    var info = new FileInfo(local);
                    state[relative] = new LocalState { Md5 = remote[relative].Md5, Length = info.Length, WriteUtc = info.LastWriteTimeUtc };
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
                            state.Remove(relative);
                            deleted++;
                        }
                        catch (Exception ex) { Log($"не удалён {relative}: {ex.Message}"); }
                    }
                }

                SaveState(state);
                CopyDataFiles(config!);

                if (downloaded > 0 || deleted > 0)
                    Log($"зеркало обновлено: скачано {downloaded}, удалено {deleted}");
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

        private static async Task ListFolderAsync(UpdateSourceConfig source, string folderId, string prefix,
            Dictionary<string, DriveFile> result, CancellationToken token)
        {
            string? pageToken = null;
            do
            {
                string url = ApiBase
                    + "?q=" + Uri.EscapeDataString($"'{folderId}' in parents and trashed=false")
                    + "&fields=" + Uri.EscapeDataString("nextPageToken,files(id,name,mimeType,md5Checksum,modifiedTime)")
                    + "&pageSize=1000&supportsAllDrives=true&includeItemsFromAllDrives=true"
                    + "&key=" + Uri.EscapeDataString(source.ApiKey!)
                    + (pageToken == null ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken));

                using HttpResponseMessage response = await Http.GetAsync(url, token);
                string body = await response.Content.ReadAsStringAsync(token);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"Drive API {(int)response.StatusCode}: {Shorten(body)}");

                JObject json = JObject.Parse(body);
                foreach (JToken item in json["files"] ?? new JArray())
                {
                    string name = (string?)item["name"] ?? "";
                    if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                        continue;
                    if (prefix.Length == 0 && !TopLevelNames.Contains(name))
                        continue;

                    string relative = prefix.Length == 0 ? name : Path.Combine(prefix, name);
                    // Updater копирует client\ в папку TNovClient целиком: корпоративный конфиг
                    // из комплекта клиента затёр бы личный.
                    if (string.Equals(relative, Path.Combine("client", "TNovConfig.json"), StringComparison.OrdinalIgnoreCase))
                        continue;
                    string mime = (string?)item["mimeType"] ?? "";
                    if (mime == FolderMimeType)
                    {
                        await ListFolderAsync(source, (string)item["id"]!, relative, result, token);
                        continue;
                    }

                    // Документы Google (без md5) — не файлы релиза.
                    string md5 = (string?)item["md5Checksum"] ?? "";
                    if (md5.Length == 0)
                        continue;

                    var file = new DriveFile
                    {
                        Id = (string)item["id"]!,
                        Md5 = md5,
                        Modified = (DateTime?)item["modifiedTime"] ?? DateTime.MinValue
                    };
                    // На Drive допустимы одноимённые файлы — берём самый свежий.
                    if (!result.TryGetValue(relative, out DriveFile? existing) || existing.Modified < file.Modified)
                        result[relative] = file;
                }

                pageToken = (string?)json["nextPageToken"];
            }
            while (!string.IsNullOrEmpty(pageToken));
        }

        private static async Task DownloadAsync(UpdateSourceConfig source, DriveFile file, string localPath, CancellationToken token)
        {
            // Drive API (alt=media) иногда отвечает 403 «automated queries» — Google режет
            // скачивание по API с адреса сети, хотя список файлов отдаёт. Тогда берём файл
            // по публичной ссылке; подмену (HTML вместо файла) отсекает проверка md5.
            string id = Uri.EscapeDataString(file.Id);
            string[] urls =
            {
                $"{ApiBase}/{id}?alt=media&supportsAllDrives=true&key={Uri.EscapeDataString(source.ApiKey!)}",
                $"https://drive.usercontent.google.com/download?id={id}&export=download&confirm=t"
            };
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            string name = Path.GetFileName(localPath);
            var errors = new List<string>();

            // API уже отказал — час не тратим на него время (на каждый файл лишний запрос).
            int first = DateTime.UtcNow < _apiDownloadBlockedUntilUtc ? 1 : 0;
            for (int i = first; i < urls.Length; i++)
            {
                string url = urls[i];
                try
                {
                    using (HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            if (i == 0 && response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                                _apiDownloadBlockedUntilUtc = DateTime.UtcNow.AddHours(1);
                            string body = await response.Content.ReadAsStringAsync(token);
                            errors.Add($"{(int)response.StatusCode} {Shorten(StripHtml(body))}");
                            continue;
                        }
                        await using FileStream output = File.Create(localPath);
                        await response.Content.CopyToAsync(output, token);
                    }

                    if (string.Equals(ComputeMd5(localPath), file.Md5, StringComparison.OrdinalIgnoreCase))
                        return;
                    File.Delete(localPath);
                    errors.Add("md5 не совпал");
                }
                catch (HttpRequestException ex) { errors.Add(ex.Message); }
            }

            throw new HttpRequestException($"скачивание {name}: {string.Join(" | ", errors)}");
        }

        private static bool IsUpToDate(string localPath, string relative, string remoteMd5, Dictionary<string, LocalState> state)
        {
            if (!File.Exists(localPath))
                return false;

            var info = new FileInfo(localPath);
            if (state.TryGetValue(relative, out LocalState? known)
                && known.Length == info.Length && known.WriteUtc == info.LastWriteTimeUtc)
            {
                return string.Equals(known.Md5, remoteMd5, StringComparison.OrdinalIgnoreCase);
            }

            // Состояния нет (первый запуск, ручная правка) — считаем md5 по файлу.
            string md5 = ComputeMd5(localPath);
            state[relative] = new LocalState { Md5 = md5, Length = info.Length, WriteUtc = info.LastWriteTimeUtc };
            return string.Equals(md5, remoteMd5, StringComparison.OrdinalIgnoreCase);
        }

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
                        && new FileInfo(destination).Length == new FileInfo(source).Length
                        && File.GetLastWriteTimeUtc(destination) >= File.GetLastWriteTimeUtc(source))
                        continue;
                    Directory.CreateDirectory(target);
                    File.Copy(source, destination, overwrite: true);
                }
                catch (Exception ex) { Log($"не скопирован {name}: {ex.Message}"); }
            }
        }

        private static Dictionary<string, LocalState> LoadState()
        {
            try
            {
                if (File.Exists(StateFile))
                {
                    var loaded = JsonConvert.DeserializeObject<Dictionary<string, LocalState>>(File.ReadAllText(StateFile));
                    if (loaded != null)
                        return new Dictionary<string, LocalState>(loaded, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex) { Log("состояние зеркала не прочитано: " + ex.Message); }
            return new Dictionary<string, LocalState>(StringComparer.OrdinalIgnoreCase);
        }

        private static void SaveState(Dictionary<string, LocalState> state)
        {
            try
            {
                Directory.CreateDirectory(MirrorRoot);
                File.WriteAllText(StateFile, JsonConvert.SerializeObject(state, Formatting.Indented));
            }
            catch (Exception ex) { Log("состояние зеркала не записано: " + ex.Message); }
        }

        private static string ComputeMd5(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(MD5.HashData(stream)).ToLowerInvariant();
        }

        private static string StripHtml(string text) =>
            System.Text.RegularExpressions.Regex.Replace(
                System.Text.RegularExpressions.Regex.Replace(text, "<(style|script)[^>]*>.*?</\\1>|<[^>]+>", " ",
                    System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                "\\s+", " ").Trim();

        private static string Shorten(string text) =>
            text.Length <= 300 ? text : text.Substring(0, 300) + "…";

        private static void Log(string message)
        {
            Debug.WriteLine("TNovClient drive-mirror: " + message);
            try
            {
                Directory.CreateDirectory(ClientPaths.LogsFolder);
                File.AppendAllText(
                    Path.Combine(ClientPaths.LogsFolder, "drive-mirror.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }
}
