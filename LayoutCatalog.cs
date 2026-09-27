using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TypeFix.Models;

namespace TypeFix
{
    public enum LayoutImportStatus
    {
        Imported,
        Duplicate,
        Invalid
    }

    public readonly record struct LayoutImportItem(LayoutImportStatus Status, string Name);

    /// <summary>
    /// Built-in Persian ↔ English layout, plus layouts the user imported.
    /// Imported files live under %AppData%\TypeFix\layouts so the published app is one exe.
    /// </summary>
    public sealed class LayoutCatalog
    {
        public const string BuiltInId = "en-fa";

        private static readonly (string Resource, string Id, string FallbackName)[] BuiltInSources =
        [
            ("TypeFix.TypeFixLayouts.json", BuiltInId, "فارسی ↔ English"),
            ("TypeFix.Layouts.en-ar.json", "en-ar", "العربية ↔ English"),
            ("TypeFix.Layouts.en-fr.json", "en-fr", "Français ↔ English"),
            ("TypeFix.Layouts.en-it.json", "en-it", "Italiano ↔ English"),
            ("TypeFix.Layouts.en-es.json", "en-es", "Español ↔ English"),
            ("TypeFix.Layouts.en-tr.json", "en-tr", "Türkçe ↔ English"),
            ("TypeFix.Layouts.en-zh.json", "en-zh", "中文 ↔ English"),
            ("TypeFix.Layouts.en-ko.json", "en-ko", "한국어 ↔ English")
        ];

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly List<LayoutProfile> _builtIns;
        private readonly List<LayoutProfile> _user = new();
        private string _activeId;

        public LayoutCatalog()
        {
            _builtIns = LoadBuiltIns();
            LoadUserFiles();
            TryMigrateSidecar();
            _activeId = AppSettings.Load().ActiveLayoutId;
            if (Find(_activeId) == null)
                _activeId = BuiltInId;
            Publish();
        }

        public IReadOnlyList<LayoutProfile> Profiles { get; private set; } = Array.Empty<LayoutProfile>();

        public string ActiveId => _activeId;

        public LayoutProfile Active => Find(_activeId) ?? _builtIns[0];

        public string Label(LayoutProfile profile)
        {
            if (profile.IsBuiltIn && profile.Id == BuiltInId)
                return UiLanguage.Get("LayoutBuiltInName");

            return profile.DisplayName;
        }

        public event Action? Changed;

        public void SetActive(string id)
        {
            if (string.Equals(id, _activeId, StringComparison.Ordinal))
                return;
            if (Find(id) == null)
                return;

            _activeId = id;
            AppSettings.SaveActiveLayout(id);
            Changed?.Invoke();
        }

        public IReadOnlyList<LayoutImportItem> ImportPaths(IEnumerable<string> paths)
        {
            var results = new List<LayoutImportItem>();
            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                try
                {
                    if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        results.AddRange(ImportZip(path));
                    else
                        results.Add(ImportFile(path));
                }
                catch
                {
                    results.Add(new LayoutImportItem(LayoutImportStatus.Invalid, Path.GetFileName(path)));
                }
            }

            return results;
        }

        public bool Remove(string id)
        {
            var profile = _user.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
            if (profile == null)
                return false;

            try
            {
                if (!string.IsNullOrEmpty(profile.FilePath) && File.Exists(profile.FilePath))
                    File.Delete(profile.FilePath);
            }
            catch
            {
                return false;
            }

            _user.Remove(profile);
            if (string.Equals(_activeId, id, StringComparison.Ordinal))
            {
                _activeId = BuiltInId;
                AppSettings.SaveActiveLayout(_activeId);
            }

            Publish();
            Changed?.Invoke();
            return true;
        }

        public bool ExportAll(string zipPath)
        {
            try
            {
                string? directory = Path.GetDirectoryName(zipPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                using var stream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
                foreach (var profile in Profiles)
                {
                    var entry = zip.CreateEntry(profile.Id + ".json", CompressionLevel.Optimal);
                    using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    writer.Write(JsonSerializer.Serialize(profile.Config, JsonOptions));
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void Publish()
        {
            var list = new List<LayoutProfile>(_builtIns.Count + _user.Count);
            list.AddRange(_builtIns);
            list.AddRange(_user.OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase));
            Profiles = list;
        }

        private LayoutProfile? Find(string? id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            return _builtIns.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal))
                   ?? _user.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        }

        private bool IsReservedId(string id)
        {
            return _builtIns.Any(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        }

        private IReadOnlyList<LayoutImportItem> ImportZip(string path)
        {
            var results = new List<LayoutImportItem>();
            using var zip = ZipFile.OpenRead(path);
            foreach (var entry in zip.Entries)
            {
                if (!entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (entry.Name != entry.FullName && entry.FullName.Contains("..", StringComparison.Ordinal))
                    continue;
                if (entry.Length > 1_000_000)
                {
                    results.Add(new LayoutImportItem(LayoutImportStatus.Invalid, entry.Name));
                    continue;
                }

                using var reader = new StreamReader(entry.Open());
                results.Add(ImportJson(reader.ReadToEnd(), entry.Name));
            }

            if (results.Count == 0)
                results.Add(new LayoutImportItem(LayoutImportStatus.Invalid, Path.GetFileName(path)));

            return results;
        }

        private LayoutImportItem ImportFile(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 1_000_000)
                return new LayoutImportItem(LayoutImportStatus.Invalid, Path.GetFileName(path));

            return ImportJson(File.ReadAllText(path), Path.GetFileName(path));
        }

        private LayoutImportItem ImportJson(string json, string fallbackName)
        {
            if (!TryParse(json, out var config))
                return new LayoutImportItem(LayoutImportStatus.Invalid, fallbackName);

            string signature = Signature(config);
            var existing = Profiles.FirstOrDefault(p => Signature(p.Config) == signature);
            if (existing != null)
            {
                if (!string.Equals(_activeId, existing.Id, StringComparison.Ordinal))
                    SetActive(existing.Id);
                return new LayoutImportItem(LayoutImportStatus.Duplicate, Label(existing));
            }

            string requested = SanitizeId(string.IsNullOrWhiteSpace(config.Id)
                ? Path.GetFileNameWithoutExtension(fallbackName)
                : config.Id);
            if (string.IsNullOrEmpty(requested))
                requested = "layout";

            if (IsReservedId(requested))
                requested += "-custom";

            config.Id = requested;
            if (string.IsNullOrWhiteSpace(config.Name))
                config.Name = BuildDisplayName(config);

            string filePath = Path.Combine(LayoutsDirectory(), requested + ".json");
            Directory.CreateDirectory(LayoutsDirectory());
            File.WriteAllText(filePath, JsonSerializer.Serialize(config, JsonOptions), new UTF8Encoding(false));

            var previous = _user.FirstOrDefault(p => string.Equals(p.Id, requested, StringComparison.Ordinal));
            if (previous != null)
                _user.Remove(previous);

            if (previous?.FilePath != null
                && !string.Equals(previous.FilePath, filePath, StringComparison.OrdinalIgnoreCase)
                && File.Exists(previous.FilePath))
            {
                try { File.Delete(previous.FilePath); } catch { /* keep the new file */ }
            }

            var profile = ToProfile(config, filePath, builtIn: false);
            _user.Add(profile);
            _activeId = profile.Id;
            AppSettings.SaveActiveLayout(_activeId);
            Publish();
            Changed?.Invoke();
            return new LayoutImportItem(LayoutImportStatus.Imported, Label(profile));
        }

        private static bool TryParse(string json, out LayoutConfig config)
        {
            config = new LayoutConfig();
            try
            {
                var parsed = JsonSerializer.Deserialize<LayoutConfig>(json, JsonOptions);
                if (parsed == null || !IsValid(parsed))
                    return false;

                LanguageEngine.EnsureShiftLayers(parsed);
                config = parsed;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsValid(LayoutConfig cfg)
        {
            if (cfg.LayoutPairs.Count == 0)
                return false;

            foreach (var pair in cfg.LayoutPairs)
            {
                if (string.IsNullOrEmpty(pair.Layout1Chars) || pair.Layout1Chars.Length != pair.Layout2Chars?.Length)
                    return false;

                bool shift1 = !string.IsNullOrEmpty(pair.Layout1ShiftChars);
                bool shift2 = !string.IsNullOrEmpty(pair.Layout2ShiftChars);
                if (shift1 != shift2)
                    return false;
                if (shift1
                    && (pair.Layout1ShiftChars.Length != pair.Layout1Chars.Length
                        || pair.Layout2ShiftChars.Length != pair.Layout2Chars.Length))
                    return false;
            }

            return true;
        }

        private void LoadUserFiles()
        {
            try
            {
                string dir = LayoutsDirectory();
                if (!Directory.Exists(dir))
                    return;

                foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
                {
                    try
                    {
                        if (!TryParse(File.ReadAllText(file), out var config))
                            continue;

                        string id = SanitizeId(string.IsNullOrWhiteSpace(config.Id)
                            ? Path.GetFileNameWithoutExtension(file)
                            : config.Id!);
                        if (string.IsNullOrEmpty(id) || IsReservedId(id))
                            continue;
                        if (_user.Any(p => string.Equals(p.Id, id, StringComparison.Ordinal)))
                            continue;

                        config.Id = id;
                        if (string.IsNullOrWhiteSpace(config.Name))
                            config.Name = BuildDisplayName(config);

                        _user.Add(ToProfile(config, file, builtIn: false));
                    }
                    catch
                    {
                        // Skip a broken user file; the built-in layout still works.
                    }
                }
            }
            catch
            {
                // AppData may be unavailable. The built-in layout still works.
            }
        }

        private void TryMigrateSidecar()
        {
            if (_user.Count > 0)
                return;

            try
            {
                string sidecar = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TypeFixLayouts.json");
                if (!File.Exists(sidecar))
                    return;

                string json = File.ReadAllText(sidecar);
                if (!TryParse(json, out var config))
                    return;
                if (_builtIns.Any(p => Signature(p.Config) == Signature(config)))
                    return;

                ImportJson(json, "TypeFixLayouts.json");
            }
            catch
            {
                // The built-in layout stays active.
            }
        }

        private static List<LayoutProfile> LoadBuiltIns()
        {
            var list = new List<LayoutProfile>(BuiltInSources.Length);
            foreach (var source in BuiltInSources)
            {
                LayoutConfig? config = ReadEmbedded(source.Resource);
                bool persian = source.Id == BuiltInId;
                if (config == null || !IsValid(config))
                {
                    if (!persian)
                        continue;
                    config = LanguageEngine.CreateDefaultConfig();
                }

                LanguageEngine.EnsureShiftLayers(config);
                config.Id = source.Id;
                if (string.IsNullOrWhiteSpace(config.Name))
                    config.Name = source.FallbackName;

                list.Add(ToProfile(config, filePath: null, builtIn: true));
            }

            return list;
        }

        private static LayoutConfig? ReadEmbedded(string resourceName)
        {
            try
            {
                using var stream = typeof(LayoutCatalog).Assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                    return null;

                using var reader = new StreamReader(stream);
                return JsonSerializer.Deserialize<LayoutConfig>(reader.ReadToEnd(), JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        private static LayoutProfile ToProfile(LayoutConfig config, string? filePath, bool builtIn)
        {
            string id = string.IsNullOrWhiteSpace(config.Id) ? BuiltInId : config.Id.Trim();
            string name = string.IsNullOrWhiteSpace(config.Name) ? BuildDisplayName(config) : config.Name.Trim();
            config.Id = id;
            config.Name = name;
            return new LayoutProfile
            {
                Id = id,
                DisplayName = name,
                Config = config,
                IsBuiltIn = builtIn,
                FilePath = filePath
            };
        }

        private static string BuildDisplayName(LayoutConfig config)
        {
            var pair = config.LayoutPairs.FirstOrDefault();
            if (pair == null)
                return "Layout";

            bool named = !string.IsNullOrWhiteSpace(pair.Layout1Name)
                         && !string.IsNullOrWhiteSpace(pair.Layout2Name)
                         && pair.Layout1Name is not "Layout1"
                         && pair.Layout2Name is not "Layout2";
            if (named)
                return pair.Layout1Name + " ↔ " + pair.Layout2Name;

            if (!string.IsNullOrWhiteSpace(pair.Name) && pair.Name != "Unnamed")
                return pair.Name;

            return "Layout";
        }

        private static string Signature(LayoutConfig config)
        {
            var sb = new StringBuilder();
            foreach (var pair in config.LayoutPairs)
            {
                sb.Append(pair.Layout1Chars).Append('\u001f')
                    .Append(pair.Layout2Chars).Append('\u001f')
                    .Append(pair.Layout1ShiftChars).Append('\u001f')
                    .Append(pair.Layout2ShiftChars).Append('\n');
            }

            return sb.ToString();
        }

        private static string SanitizeId(string raw)
        {
            var buffer = new StringBuilder(raw.Length);
            foreach (char c in raw.Trim().ToLowerInvariant())
            {
                if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
                    buffer.Append(c);
                else if (buffer.Length > 0 && buffer[^1] != '-')
                    buffer.Append('-');
            }

            string id = buffer.ToString().Trim('-');
            if (id.Length > 64)
                id = id[..64].Trim('-');
            return id;
        }

        private static string LayoutsDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TypeFix",
                "layouts");
        }
    }
}
