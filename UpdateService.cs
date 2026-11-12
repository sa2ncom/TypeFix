using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TypeFix
{
    public static class UpdateService
    {
        public const string PrimaryVersionUrl = "https://typefix.ir/api/version";
        public const string DefaultDownloadUrl = "https://typefix.ir/download";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(6)
        };

        public static Version CurrentVersion
        {
            get
            {
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                return NormalizeVersion(ver ?? new Version(1, 0, 1, 0));
            }
        }

        public static string CurrentVersionString => FormatVersion(CurrentVersion);

        public static string FormatVersion(Version? version)
        {
            if (version == null)
                return "v1.0.1";

            Version normalized = NormalizeVersion(version);
            return normalized.Revision > 0
                ? $"v{normalized.Major}.{normalized.Minor}.{normalized.Build}.{normalized.Revision}"
                : $"v{normalized.Major}.{normalized.Minor}.{normalized.Build}";
        }

        private static string[] GetUpdateEndpoints()
        {
#if DEBUG
            string? debugOverride = Environment.GetEnvironmentVariable("TYPEFIX_UPDATE_URL")?.Trim();
            if (!string.IsNullOrEmpty(debugOverride) &&
                Uri.TryCreate(debugOverride, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                return [uri.ToString(), PrimaryVersionUrl];
            }
#endif
            return [PrimaryVersionUrl];
        }

        public static async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        {
            Version current = CurrentVersion;
            string[] endpoints = GetUpdateEndpoints();

            VersionInfo? info = null;
            Version? latestVersion = null;

            foreach (string endpoint in endpoints)
            {
                string? json = await TryFetchVersionJsonAsync(endpoint, cancellationToken).ConfigureAwait(false);
                if (TryParsePayload(json, out info, out latestVersion))
                    break;
            }

            if (info == null || latestVersion == null)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateStatus.CheckFailed,
                    CurrentVersion = current,
                    ErrorMessage = "Unable to reach update server"
                };
            }

            bool isMandatory = info.IsMandatory;
            if (!isMandatory && !string.IsNullOrWhiteSpace(info.MinRequiredVersion))
            {
                if (TryParseVersion(info.MinRequiredVersion, out var minRequired) && current < minRequired)
                {
                    isMandatory = true;
                }
            }

            if (latestVersion > current)
            {
                return new UpdateCheckResult
                {
                    Status = isMandatory ? UpdateStatus.MandatoryAvailable : UpdateStatus.OptionalAvailable,
                    Info = info,
                    CurrentVersion = current,
                    NewVersion = latestVersion
                };
            }

            return new UpdateCheckResult
            {
                Status = UpdateStatus.UpToDate,
                Info = info,
                CurrentVersion = current,
                NewVersion = latestVersion
            };
        }

        public static void OpenDownloadPage(string? downloadUrl = null)
        {
            try
            {
                string target = string.IsNullOrWhiteSpace(downloadUrl) ? DefaultDownloadUrl : downloadUrl.Trim();
                if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                {
                    target = DefaultDownloadUrl;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore failure to open browser
            }
        }

        private static bool TryParsePayload(string? json, out VersionInfo? info, out Version? latestVersion)
        {
            info = null;
            latestVersion = null;

            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                var parsed = JsonSerializer.Deserialize<VersionInfo>(json, JsonOptions);
                if (parsed == null || string.IsNullOrWhiteSpace(parsed.LatestVersion))
                    return false;

                if (!TryParseVersion(parsed.LatestVersion, out var latest))
                    return false;

                info = parsed;
                latestVersion = latest;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<string?> TryFetchVersionJsonAsync(string url, CancellationToken ct)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", $"TypeFix/{CurrentVersionString}");
                request.Headers.Add("Accept", "application/json");

                using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return null;

                string? mediaType = response.Content.Headers.ContentType?.MediaType;
                if (!string.IsNullOrEmpty(mediaType) &&
                    !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase) &&
                    !mediaType.Contains("text/plain", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // Safe: network failure, DNS error, timeout, server offline
                return null;
            }
        }

        private static bool TryParseVersion(string input, out Version version)
        {
            string clean = input.Trim().TrimStart('v', 'V');
            if (Version.TryParse(clean, out var parsed))
            {
                version = NormalizeVersion(parsed);
                return true;
            }

            version = new Version(0, 0, 0, 0);
            return false;
        }

        private static Version NormalizeVersion(Version v)
        {
            int major = Math.Max(0, v.Major);
            int minor = Math.Max(0, v.Minor);
            int build = Math.Max(0, v.Build);
            int revision = Math.Max(0, v.Revision);
            return new Version(major, minor, build, revision);
        }
    }
}
