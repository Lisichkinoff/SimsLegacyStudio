using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace SimsLegacy
{
    internal static class AppUpdates
    {
        internal const string Repository = "Lisichkinoff/SimsLegacyStudio";
        internal const string AssetName = "SimsLegacyStudio.exe";
        internal static readonly Version CurrentVersion = typeof(AppUpdates).Assembly.GetName().Version;
        private const string ReleaseApi = "https://api.github.com/repos/" + Repository + "/releases";
        private const long MaxAssetSize = 64 * 1024 * 1024;

        internal sealed class Release
        {
            public string tag_name { get; set; }
            public Asset[] assets { get; set; }
        }

        internal sealed class Asset
        {
            public string name { get; set; }
            public long size { get; set; }
            public string digest { get; set; }
        }

        internal sealed class Update
        {
            internal readonly Version Version;
            internal readonly Asset Asset;

            internal Update(Version version, Asset asset)
            {
                Version = version;
                Asset = asset;
            }
        }

        internal static Update Check()
        {
            Release release;
            using (var response = Send(new Uri(ReleaseApi + "/latest"), "application/vnd.github+json"))
            using (var reader = new StreamReader(response.GetResponseStream()))
                release = new JavaScriptSerializer().Deserialize<Release>(reader.ReadToEnd());

            Version version;
            var tag = release == null ? "" : release.tag_name ?? "";
            if (!Regex.IsMatch(tag, @"\Av(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z") ||
                !Version.TryParse(tag.Substring(1) + ".0", out version))
                throw new InvalidDataException("Тег последнего релиза должен иметь формат v1.2.3.");
            if (version <= CurrentVersion) return null;

            foreach (var asset in release.assets ?? new Asset[0])
                if (asset != null && asset.name == AssetName && asset.size > 0 &&
                    asset.size <= MaxAssetSize && IsDigest(asset.digest))
                    return new Update(version, asset);

            throw new InvalidDataException("В новом релизе отсутствует EXE с контрольной суммой SHA-256.");
        }

        internal static void Download(Update update, string path)
        {
            var endpoint = new Uri("https://github.com/" + Repository + "/releases/download/v" +
                update.Version.ToString(3) + "/" + AssetName);
            using (var response = OpenAsset(endpoint))
            using (var source = response.GetResponseStream())
            using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[65536];
                long total = 0;
                int count;
                while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += count;
                    if (total > update.Asset.size)
                        throw new InvalidDataException("Размер скачанного файла не совпадает с размером релиза.");
                    target.Write(buffer, 0, count);
                }
                if (total != update.Asset.size)
                    throw new InvalidDataException("Файл обновления скачан не полностью.");
            }
            Verify(path, update.Version, update.Asset.digest);
        }

        internal static void Verify(string path, Version version, string digest)
        {
            if (!IsDigest(digest)) throw new InvalidDataException("Отсутствует контрольная сумма обновления.");
            using (var file = File.OpenRead(path))
            using (var hash = SHA256.Create())
            {
                var actual = "sha256:" + BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "");
                if (!string.Equals(digest, actual, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Контрольная сумма скачанного файла не совпадает с релизом.");
            }
            var assembly = AssemblyName.GetAssemblyName(path);
            if (assembly.Name != Path.GetFileNameWithoutExtension(AssetName) || assembly.Version != version)
                throw new InvalidDataException("Версия скачанного EXE не совпадает с тегом релиза.");
        }

        private static bool IsDigest(string value)
        {
            return value != null && Regex.IsMatch(value, @"\Asha256:[a-fA-F0-9]{64}\z");
        }

        private static HttpWebResponse OpenAsset(Uri uri)
        {
            var response = Send(uri, "application/octet-stream");
            if (response.StatusCode == HttpStatusCode.OK) return response;
            using (response)
            {
                Uri redirect;
                if (response.StatusCode != HttpStatusCode.Found ||
                    !Uri.TryCreate(response.Headers[HttpResponseHeader.Location], UriKind.Absolute, out redirect) ||
                    redirect.Scheme != Uri.UriSchemeHttps || !redirect.IsDefaultPort || redirect.UserInfo.Length != 0 ||
                    (redirect.Host != "release-assets.githubusercontent.com" && redirect.Host != "objects.githubusercontent.com"))
                    throw new InvalidDataException("GitHub вернул неизвестный адрес файла обновления.");
                return Send(redirect, "application/octet-stream");
            }
        }

        private static HttpWebResponse Send(Uri uri, string accept)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.AllowAutoRedirect = false;
            request.UserAgent = "SimsLegacyStudio/" + CurrentVersion.ToString(3);
            request.Accept = accept;
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            if (uri.Host == "api.github.com")
                request.Headers["X-GitHub-Api-Version"] = "2022-11-28";
            try { return (HttpWebResponse)request.GetResponse(); }
            catch (WebException ex)
            {
                using (var response = ex.Response as HttpWebResponse)
                {
                    if (response != null && ((int)response.StatusCode == 429 ||
                        response.Headers["X-RateLimit-Remaining"] == "0"))
                        throw new IOException("GitHub временно ограничил число запросов. Повторите проверку позже.");
                }
                throw new IOException("Не удалось получить обновления с GitHub. Проверьте подключение и попробуйте ещё раз.");
            }
        }
    }
}
