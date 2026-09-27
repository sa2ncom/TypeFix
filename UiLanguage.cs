using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace TypeFix
{
    public sealed class LanguageOption
    {
        public required string Code { get; init; }
        public required string NativeName { get; init; }
    }

    public static class UiLanguage
    {
        public const string English = "en";
        public const string Persian = "fa";
        public const string Arabic = "ar";
        public const string French = "fr";
        public const string Italian = "it";
        public const string Spanish = "es";
        public const string Turkish = "tr";
        public const string Chinese = "zh";
        public const string Korean = "ko";

        private static readonly string[] Known =
        [
            English, Persian, Arabic, French, Italian, Spanish, Turkish, Chinese, Korean
        ];

        public static IReadOnlyList<LanguageOption> Options { get; } =
        [
            new LanguageOption { Code = English, NativeName = "English" },
            new LanguageOption { Code = Persian, NativeName = "فارسی" },
            new LanguageOption { Code = Arabic, NativeName = "العربية" },
            new LanguageOption { Code = French, NativeName = "Français" },
            new LanguageOption { Code = Italian, NativeName = "Italiano" },
            new LanguageOption { Code = Spanish, NativeName = "Español" },
            new LanguageOption { Code = Turkish, NativeName = "Türkçe" },
            new LanguageOption { Code = Chinese, NativeName = "中文" },
            new LanguageOption { Code = Korean, NativeName = "한국어" }
        ];

        public static string Current { get; private set; } = English;

        public static bool IsRightToLeft => Current is Persian or Arabic;

        public static bool UsesVazirmatn => Current is Persian or Arabic;

        public static string UiFontName => Current switch
        {
            Chinese => "Microsoft YaHei UI, Microsoft YaHei, Segoe UI",
            Korean => "Malgun Gothic, Segoe UI",
            _ => "Segoe UI"
        };

        public static System.Windows.FlowDirection Flow =>
            IsRightToLeft ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;

        public static void Initialize()
        {
            Apply(AppSettings.Load().Language, persist: false);
        }

        public static void Apply(string? code, bool persist = true)
        {
            string language = Normalize(code);
            var app = System.Windows.Application.Current;
            if (app == null)
                return;

            var dictionaries = app.Resources.MergedDictionaries;
            var next = new System.Windows.ResourceDictionary
            {
                Source = new Uri($"Localization/Strings.{language}.xaml", UriKind.Relative)
            };

            dictionaries.Insert(0, next);

            for (int i = dictionaries.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(dictionaries[i], next))
                    continue;

                string source = dictionaries[i].Source?.OriginalString ?? "";
                if (source.Contains("Strings.", StringComparison.OrdinalIgnoreCase))
                    dictionaries.RemoveAt(i);
            }

            Current = language;

            if (persist)
                AppSettings.SaveLanguage(language);
        }

        public static string Get(string key)
        {
            if (System.Windows.Application.Current?.TryFindResource(key) is string text && text.Length > 0)
                return text;

            return key;
        }

        public static string Format(string key, params object[] args)
        {
            string template = Get(key);
            for (int i = 0; i < args.Length; i++)
                template = template.Replace("{" + i + "}", args[i]?.ToString() ?? "", StringComparison.Ordinal);

            return template;
        }

        internal static string Normalize(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return English;

            foreach (string known in Known)
            {
                if (string.Equals(code, known, StringComparison.OrdinalIgnoreCase))
                    return known;
            }

            return English;
        }
    }

    public static class AppSettings
    {
        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TypeFix",
            "settings.json");

        public static AppSettingsData Load()
        {
            var data = new AppSettingsData();
            try
            {
                if (File.Exists(FilePath))
                {
                    var parsed = JsonSerializer.Deserialize<AppSettingsData>(File.ReadAllText(FilePath));
                    if (parsed != null)
                        data = parsed;
                }
            }
            catch
            {
                // Fall through to defaults.
            }

            if (string.IsNullOrWhiteSpace(data.Language))
                data.Language = DetectSystemLanguage();
            if (string.IsNullOrWhiteSpace(data.ActiveLayoutId))
                data.ActiveLayoutId = LayoutCatalog.BuiltInId;
            return data;
        }

        public static void SaveLanguage(string language)
        {
            Update(data => data.Language = language);
        }

        public static void SaveActiveLayout(string id)
        {
            Update(data => data.ActiveLayoutId = id);
        }

        private static void Update(Action<AppSettingsData> change)
        {
            try
            {
                string? directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var data = Load();
                change(data);
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch
            {
                // The UI still switches for this session.
            }
        }

        private static string DetectSystemLanguage()
        {
            return UiLanguage.Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        }
    }

    public sealed class AppSettingsData
    {
        public string Language { get; set; } = UiLanguage.English;
        public string ActiveLayoutId { get; set; } = LayoutCatalog.BuiltInId;
    }
}
