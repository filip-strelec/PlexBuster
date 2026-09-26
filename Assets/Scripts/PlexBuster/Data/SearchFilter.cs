using System;
using System.Globalization;
using System.Text;

namespace PlexBuster.Data
{
    /// <summary>
    /// Free-text matching for the search terminal: every typed word must appear somewhere in the title,
    /// year, cast, director or genres. Case and accents are ignored, so "cudo" finds "Čudo".
    /// </summary>
    public static class SearchFilter
    {
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            foreach (var c in text.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(c switch { 'Đ' or 'đ' => 'd', 'Ø' or 'ø' => 'o', 'Ł' or 'ł' => 'l', _ => char.ToLowerInvariant(c) });
            }
            return sb.ToString();
        }

        /// <summary>Everything about an item a search can match, normalised once and cached by the caller.</summary>
        public static string Haystack(LibraryItem item) => Normalize(string.Join(" ",
            item.Title, item.SortTitle, item.Year > 0 ? item.Year.ToString() : "",
            string.Join(" ", item.Actors), string.Join(" ", item.Directors), string.Join(" ", item.Genres)));

        /// <param name="normalizedQuery">A query already passed through <see cref="Normalize"/>.</param>
        public static bool Matches(string haystack, string normalizedQuery)
        {
            if (string.IsNullOrWhiteSpace(normalizedQuery)) return true;
            foreach (var word in normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (!haystack.Contains(word, StringComparison.Ordinal)) return false;
            return true;
        }
    }
}
