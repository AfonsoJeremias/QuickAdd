using System.Text.RegularExpressions;

namespace QuickAdd.Services
{
    /// <summary>Turns folder/file names like "Half.Life_2 [FitGirl Repack] (v1.2.3)" into "Half Life 2".</summary>
    public static class NameService
    {
        const RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        public static string Clean(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            var s = raw;

            s = Regex.Replace(s, @"\[[^\]]*\]", " ");                                   // [FitGirl Repack]
            s = Regex.Replace(s, @"\((?:v|ver\.?|version|build)?\s*\d[^)]*\)", " ", I); // (v1.2.3) (2019)
            s = Regex.Replace(s, @"\b(?:v|ver\.?|version)\s?\d+(?:[._]\d+)*[a-z]?\b", " ", I);
            s = Regex.Replace(s, @"\bbuild\s?\d+\b", " ", I);
            s = Regex.Replace(s, @"\b(?:fitgirl|repack|gog|portable|win64|win32|x64|x86|setup)\b", " ", I);

            // dots / underscores used as spaces
            if (s.IndexOf(' ') < 0 && (CountOf(s, '_') > 0 || CountOf(s, '.') >= 2)) s = s.Replace('_', ' ').Replace('.', ' ');
            s = s.Replace('_', ' ');

            s = Regex.Replace(s, @"\s+", " ").Trim(' ', '-', '_', '.');
            return string.IsNullOrWhiteSpace(s) ? raw.Trim() : s;
        }

        static int CountOf(string s, char c)
        {
            int n = 0;
            foreach (var ch in s) if (ch == c) n++;
            return n;
        }
    }
}
