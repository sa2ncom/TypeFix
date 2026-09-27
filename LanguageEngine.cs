using System;
using System.Collections.Generic;
using TypeFix.Models;

namespace TypeFix
{
    public class LanguageEngine
    {
        private volatile ConversionState _state = new()
        {
            Layouts = new List<CompiledLayout>(),
            NormalizePersian = false
        };

        /// <summary>
        /// Compiles one layout file and publishes it for Convert.
        /// Only this layout is used until the next Apply.
        /// </summary>
        public bool Apply(LayoutConfig config)
        {
            EnsureShiftLayers(config);
            var layouts = CompileLayouts(config);
            _state = new ConversionState
            {
                Layouts = layouts,
                NormalizePersian = ContainsPersianLetters(config)
            };
            return layouts.Count > 0;
        }

        public string Convert(string input)
        {
            // Snapshot so Apply mid-convert keeps using the previous layout.
            var state = _state;
            var layouts = state.Layouts;

            if (string.IsNullOrEmpty(input) || layouts.Count == 0)
                return input;

            // Arabic ي/ك → Persian ی/ک only for Persian layouts. An Arabic layout needs those letters.
            if (state.NormalizePersian)
                input = NormalizePersianLetters(input);

            CompiledLayout? best = null;
            bool bidirectional = false;
            bool fromLayout1To2 = true;
            int bestScore = 0;

            foreach (var layout in layouts)
            {
                CountUniqueLayoutChars(input, layout.Set1, layout.Set2, out int count1, out int count2);

                if (count1 == 0 && count2 == 0)
                    continue;

                // Both scripts present: map each character to the other layout.
                // A one-script tie cannot happen here (one count would be zero).
                bool mixed = count1 > 0 && count2 > 0;
                int score = Math.Max(count1, count2);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = layout;
                    bidirectional = mixed;
                    fromLayout1To2 = count1 > count2;
                }
            }

            if (best == null || bestScore == 0)
                return input;

            // Both scripts in one selection: each character maps to the other layout.
            // "hdk d; ljk ٍدلمهسا isj" → "این یک متن English هست".
            if (bidirectional)
                return MapEachScript(input, best);

            return MapOneDirection(input, best, fromLayout1To2);
        }

        /// <summary>
        /// Maps every layout-1-only character to layout 2 and every layout-2-only character to layout 1.
        /// Characters in neither layout, and glyphs shared by both (such as '.' and '/'), stay put.
        /// </summary>
        private static string MapEachScript(string input, CompiledLayout layout)
        {
            char[] buffer = new char[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                bool in1 = layout.Set1.Contains(c);
                bool in2 = layout.Set2.Contains(c);

                if (in1 && !in2)
                    buffer[i] = layout.Map1To2.TryGetValue(c, out char to2) ? to2 : c;
                else if (in2 && !in1)
                    buffer[i] = layout.Map2To1.TryGetValue(c, out char to1) ? to1 : c;
                else
                    buffer[i] = c;
            }

            return new string(buffer);
        }

        private static string MapOneDirection(string input, CompiledLayout layout, bool fromLayout1To2)
        {
            Dictionary<char, char> map = fromLayout1To2 ? layout.Map1To2 : layout.Map2To1;
            HashSet<char> srcSet = fromLayout1To2 ? layout.Set1 : layout.Set2;
            HashSet<char> dstSet = fromLayout1To2 ? layout.Set2 : layout.Set1;

            char[] buffer = new char[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (dstSet.Contains(c) && !srcSet.Contains(c))
                {
                    buffer[i] = c;
                    continue;
                }

                buffer[i] = map.TryGetValue(c, out char mapped) ? mapped : c;
            }

            return new string(buffer);
        }

        #region Compiled layouts

        private sealed class ConversionState
        {
            public required List<CompiledLayout> Layouts { get; init; }
            public required bool NormalizePersian { get; init; }
        }

        private sealed class CompiledLayout
        {
            public required HashSet<char> Set1 { get; init; }
            public required HashSet<char> Set2 { get; init; }
            public required Dictionary<char, char> Map1To2 { get; init; }
            public required Dictionary<char, char> Map2To1 { get; init; }
        }

        private static List<CompiledLayout> CompileLayouts(LayoutConfig config)
        {
            var list = new List<CompiledLayout>(config.LayoutPairs.Count);

            foreach (var pair in config.LayoutPairs)
            {
                string l1 = pair.Layout1Chars;
                string l2 = pair.Layout2Chars;
                if (string.IsNullOrEmpty(l1) || string.IsNullOrEmpty(l2) || l1.Length != l2.Length)
                    continue;

                bool hasShift = HasValidShiftLayer(l1, pair.Layout1ShiftChars, l2, pair.Layout2ShiftChars);

                var map1To2 = BuildCharMap(l1, l2, pair.Layout1ShiftChars, pair.Layout2ShiftChars, hasShift);
                var map2To1 = BuildCharMap(l2, l1, pair.Layout2ShiftChars, pair.Layout1ShiftChars, hasShift);

                bool layout1Arabic = ContainsArabic(l1) || ContainsArabic(pair.Layout1ShiftChars);
                bool layout2Arabic = ContainsArabic(l2) || ContainsArabic(pair.Layout2ShiftChars);

                var set1 = BuildLayoutSet(l1, pair.Layout1ShiftChars);
                var set2 = BuildLayoutSet(l2, pair.Layout2ShiftChars);

                // Fold punctuation fallbacks into the dictionaries once.
                if (!layout1Arabic && layout2Arabic)
                {
                    map1To2.TryAdd('?', '؟');
                    map2To1.TryAdd('؟', '?');

                    // Windows Persian (KLID 00000429): M is ئ, backslash is پ.
                    // The shift layer still lists ئ on S (ISIRI). Unshifted M wins on the way back.
                    if (map1To2.TryGetValue('m', out char onM) && onM == 'ئ')
                    {
                        map2To1['ئ'] = 'm';
                        map1To2['\\'] = 'پ';
                        map2To1['پ'] = '\\';
                        set1.Add('\\');
                        set2.Add('پ');
                    }
                    else if (l2.Contains('پ'))
                    {
                        map1To2.TryAdd('\\', 'پ');
                        set1.Add('\\');
                    }
                }
                else if (layout1Arabic && !layout2Arabic)
                {
                    map1To2.TryAdd('؟', '?');
                    map2To1.TryAdd('?', '؟');

                    if (map2To1.TryGetValue('m', out char onM) && onM == 'ئ')
                    {
                        map1To2['ئ'] = 'm';
                        map2To1['\\'] = 'پ';
                        map1To2['پ'] = '\\';
                        set2.Add('\\');
                        set1.Add('پ');
                    }
                    else if (l1.Contains('پ'))
                    {
                        map2To1.TryAdd('\\', 'پ');
                        set2.Add('\\');
                    }
                }

                list.Add(new CompiledLayout
                {
                    Set1 = set1,
                    Set2 = set2,
                    Map1To2 = map1To2,
                    Map2To1 = map2To1
                });
            }

            return list;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Maps common Arabic letter variants to Persian forms used by ISIRI layouts:
        /// ي (U+064A) → ی (U+06CC), ك (U+0643) → ک (U+06A9).
        /// </summary>
        private static string NormalizePersianLetters(string input)
        {
            char[]? buffer = null;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                char normalized = c switch
                {
                    '\u064A' => '\u06CC', // ي → ی
                    '\u0643' => '\u06A9', // ك → ک
                    _ => c
                };

                if (normalized == c)
                    continue;

                buffer ??= input.ToCharArray();
                buffer[i] = normalized;
            }

            return buffer is null ? input : new string(buffer);
        }

        private static bool HasValidShiftLayer(string srcBase, string srcShift, string dstBase, string dstShift)
        {
            return !string.IsNullOrEmpty(srcShift)
                   && !string.IsNullOrEmpty(dstShift)
                   && srcShift.Length == srcBase.Length
                   && dstShift.Length == dstBase.Length;
        }

        private static Dictionary<char, char> BuildCharMap(
            string srcBase,
            string dstBase,
            string srcShift,
            string dstShift,
            bool hasShiftLayer)
        {
            var map = new Dictionary<char, char>(srcBase.Length * 2);

            for (int i = 0; i < srcBase.Length; i++)
            {
                char src = srcBase[i];
                char dst = dstBase[i];
                map[src] = dst;

                // Legacy configs without a shift layer: precompute Latin uppercase → mapped char.
                if (!hasShiftLayer && char.IsLetter(src) && char.IsLower(src))
                {
                    char srcUpper = char.ToUpperInvariant(src);
                    char dstMapped = char.IsLetter(dst) ? char.ToUpperInvariant(dst) : dst;
                    map.TryAdd(srcUpper, dstMapped);
                }
            }

            if (hasShiftLayer)
            {
                for (int i = 0; i < srcShift.Length; i++)
                    map[srcShift[i]] = dstShift[i];
            }

            return map;
        }

        private static HashSet<char> BuildLayoutSet(string baseChars, string? shiftChars)
        {
            var set = new HashSet<char>();

            if (!string.IsNullOrEmpty(baseChars))
            {
                foreach (char c in baseChars)
                {
                    set.Add(c);
                    char lower = char.ToLowerInvariant(c);
                    char upper = char.ToUpperInvariant(c);
                    if (lower != c)
                        set.Add(lower);
                    if (upper != c)
                        set.Add(upper);
                }
            }

            if (!string.IsNullOrEmpty(shiftChars))
            {
                foreach (char c in shiftChars)
                    set.Add(c);
            }

            return set;
        }

        /// <summary>
        /// Counts characters that belong to only one layout.
        /// Shared glyphs (e.g. '.' '/' and overlapping shift symbols) are ignored for direction.
        /// </summary>
        private static void CountUniqueLayoutChars(
            string input,
            HashSet<char> set1,
            HashSet<char> set2,
            out int count1,
            out int count2)
        {
            count1 = 0;
            count2 = 0;

            foreach (char c in input)
            {
                bool in1 = set1.Contains(c);
                bool in2 = set2.Contains(c);

                if (in1 && in2)
                    continue;

                if (in1)
                    count1++;
                if (in2)
                    count2++;
            }
        }

        private static bool ContainsArabic(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            foreach (char c in text)
            {
                if (c >= '\u0600' && c <= '\u06FF')
                    return true;
            }

            return false;
        }

        /// <summary>ISIRI-style default that put پ on M. Windows Persian puts ئ there.</summary>
        private const string LegacyPersianMIsPeh = "ضصثقفغعهخحجچشسیبلاتنمکگظطزرذدپو./";

        /// <summary>
        /// Upgrades older JSON configs that only had the unshifted layer,
        /// and moves M from پ to ئ for the built-in English–Persian pair.
        /// </summary>
        public static bool EnsureShiftLayers(LayoutConfig cfg)
        {
            var defaults = CreateDefaultConfig().LayoutPairs[0];
            bool changed = false;

            foreach (var pair in cfg.LayoutPairs)
            {
                bool missingShift = string.IsNullOrEmpty(pair.Layout1ShiftChars)
                                    || string.IsNullOrEmpty(pair.Layout2ShiftChars)
                                    || pair.Layout1ShiftChars.Length != pair.Layout1Chars.Length
                                    || pair.Layout2ShiftChars.Length != pair.Layout2Chars.Length;

                if (!missingShift)
                    continue;

                // Only auto-fill for the known English↔Persian base layer.
                if (pair.Layout1Chars == defaults.Layout1Chars
                    && (pair.Layout2Chars == defaults.Layout2Chars || pair.Layout2Chars == LegacyPersianMIsPeh))
                {
                    pair.Layout1ShiftChars = defaults.Layout1ShiftChars;
                    pair.Layout2ShiftChars = defaults.Layout2ShiftChars;
                    changed = true;
                }
            }

            foreach (var pair in cfg.LayoutPairs)
            {
                // Older defaults put پ on M (ISIRI). Windows Persian puts ئ there.
                if (pair.Layout1Chars == defaults.Layout1Chars
                    && pair.Layout2Chars == LegacyPersianMIsPeh)
                {
                    pair.Layout2Chars = defaults.Layout2Chars;
                    changed = true;
                }
            }

            return changed;
        }

        private static bool ContainsPersianLetters(LayoutConfig cfg)
        {
            foreach (var pair in cfg.LayoutPairs)
            {
                string all = pair.Layout1Chars + pair.Layout2Chars + pair.Layout1ShiftChars + pair.Layout2ShiftChars;
                if (all.Contains('پ') || all.Contains('چ') || all.Contains('ژ') || all.Contains('گ')
                    || all.Contains('\u06CC') || all.Contains('\u06A9'))
                    return true;
            }

            return false;
        }

        public static LayoutConfig CreateDefaultConfig()
        {
            // Unshifted letter area aligned to US QWERTY.
            // M is ئ, matching Windows Persian (KLID 00000429). پ is mapped from '\' in CompileLayouts.
            const string en = "qwertyuiop[]asdfghjkl;'zxcvbnm,./";
            const string fa = "ضصثقفغعهخحجچشسیبلاتنمکگظطزرذدئو./";

            // Shift layer: English Shift+key ↔ Persian Shift+key (ISIRI 9147).
            // Notable: C→ژ, H→آ, M→ء, B→ZWNJ, /→؟
            // ئ also appears on Shift+S here; CompileLayouts keeps the reverse map as ئ → m.
            const string enShift = "QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?";
            const string faShift =
                "\u0652\u064C\u064D\u064B\u064F\u0650\u064E\u0651][}{" +   // ًٌٍَُِّْ][}{
                "\u0624\u0626\u064A\u0625\u0623\u0622\u0629\u00BB\u00AB:\u061B" + // ؤئيإأآة»«:؛
                "\u0643\u0653\u0698\u0670\u200C\u0654\u0621><\u061F";       // كٓژٰ‌ٔء><؟

            return new LayoutConfig
            {
                Id = LayoutCatalog.BuiltInId,
                Name = "فارسی ↔ English",
                LayoutPairs =
                {
                    new LayoutPair
                    {
                        Name = "English-Persian",
                        Id = "en-fa",
                        Layout1Name = "English",
                        Layout2Name = "فارسی",
                        Layout1Chars = en,
                        Layout2Chars = fa,
                        Layout1ShiftChars = enShift,
                        Layout2ShiftChars = faShift
                    }
                }
            };
        }

        #endregion
    }
}
