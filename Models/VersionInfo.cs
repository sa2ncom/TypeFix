using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TypeFix
{
    public sealed class VersionInfo
    {
        [JsonPropertyName("latestVersion")]
        public string LatestVersion { get; set; } = "";

        [JsonPropertyName("minRequiredVersion")]
        public string MinRequiredVersion { get; set; } = "";

        [JsonPropertyName("isMandatory")]
        public bool IsMandatory { get; set; }

        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = "https://typefix.ir/download";

        [JsonPropertyName("releaseDate")]
        public string ReleaseDate { get; set; } = "";

        [JsonPropertyName("changelog")]
        public Dictionary<string, List<string>>? Changelog { get; set; }

        public IReadOnlyList<string> GetChangelog(string languageCode)
        {
            if (Changelog == null || Changelog.Count == 0)
                return Array.Empty<string>();

            if (TryGetNonEmpty(languageCode, out var exact))
                return exact;

            if (TryGetNonEmpty("en", out var enList))
                return enList;

            if (TryGetNonEmpty("fa", out var faList))
                return faList;

            foreach (var list in Changelog.Values)
            {
                if (list is { Count: > 0 })
                    return list;
            }

            return Array.Empty<string>();
        }

        private bool TryGetNonEmpty(string code, out IReadOnlyList<string> items)
        {
            if (Changelog != null && !string.IsNullOrWhiteSpace(code))
            {
                foreach (var pair in Changelog)
                {
                    if (string.Equals(pair.Key, code, StringComparison.OrdinalIgnoreCase) && pair.Value is { Count: > 0 })
                    {
                        items = pair.Value;
                        return true;
                    }
                }
            }

            items = Array.Empty<string>();
            return false;
        }
    }

    public enum UpdateStatus
    {
        UpToDate,
        OptionalAvailable,
        MandatoryAvailable,
        CheckFailed
    }

    public sealed class UpdateCheckResult
    {
        public UpdateStatus Status { get; init; }
        public VersionInfo? Info { get; init; }
        public Version? CurrentVersion { get; init; }
        public Version? NewVersion { get; init; }
        public string? ErrorMessage { get; init; }
    }
}
