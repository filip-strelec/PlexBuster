using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PlexBuster.Data
{
    public readonly struct SubtitleCue
    {
        public readonly double Start;
        public readonly double End;
        public readonly string Text;

        public SubtitleCue(double start, double end, string text)
        {
            Start = start;
            End = end;
            Text = text;
        }
    }

    /// <summary>
    /// Subtitle files as timed cues: SRT, WebVTT and ASS/SSA, decoded as UTF-8 or (older Croatian files)
    /// Windows-1250. Formatting tags are removed, except italics.
    /// </summary>
    public class Subtitles
    {
        static readonly Regex Timing = new(
            @"(?<h1>\d+):(?<m1>\d{2}):(?<s1>\d{2})[,.](?<f1>\d{1,3})\s*-->\s*(?<h2>\d+):(?<m2>\d{2}):(?<s2>\d{2})[,.](?<f2>\d{1,3})",
            RegexOptions.Compiled);
        static readonly Regex VttShortTiming = new(
            @"^(?<m1>\d{2}):(?<s1>\d{2})\.(?<f1>\d{3})\s*-->\s*(?<m2>\d{2}):(?<s2>\d{2})\.(?<f2>\d{3})", RegexOptions.Compiled);
        static readonly Regex MarkupTags = new(@"</?(?!i>|/i>)[a-zA-Z][^>]*>|\{[^}]*\}", RegexOptions.Compiled);

        readonly List<SubtitleCue> cues;

        Subtitles(List<SubtitleCue> cues)
        {
            cues.Sort((a, b) => a.Start.CompareTo(b.Start));
            this.cues = cues;
        }

        public int Count => cues.Count;

        /// <summary>The text showing at <paramref name="seconds"/> into the title, or null.</summary>
        public string TextAt(double seconds)
        {
            // Last cue starting at or before the time; overlapping cues are joined.
            int lo = 0, hi = cues.Count - 1, found = -1;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                if (cues[mid].Start <= seconds)
                {
                    found = mid;
                    lo = mid + 1;
                }
                else hi = mid - 1;
            }
            if (found < 0) return null;

            string text = null;
            for (var i = found; i >= 0 && found - i < 4; i--)
                if (cues[i].End > seconds)
                    text = text == null ? cues[i].Text : cues[i].Text + "\n" + text;
            return text;
        }

        public static Subtitles Parse(byte[] data, string format)
        {
            var text = Decode(data).Replace("\r\n", "\n").Replace('\r', '\n');
            return format is "ass" or "ssa" ? ParseAss(text) : ParseSrtOrVtt(text);
        }

        /// <summary>UTF-8 (or UTF-16 with a byte order mark) when it is valid; otherwise Windows-1250.</summary>
        public static string Decode(byte[] data)
        {
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE) return Encoding.Unicode.GetString(data, 2, data.Length - 2);
            if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
            var start = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
            try
            {
                return new UTF8Encoding(false, true).GetString(data, start, data.Length - start);
            }
            catch (DecoderFallbackException)
            {
                var chars = new char[data.Length];
                for (var i = 0; i < data.Length; i++)
                    chars[i] = data[i] < 0x80 ? (char)data[i] : Windows1250High[data[i] - 0x80];
                return new string(chars);
            }
        }

        // Windows-1250 (Central European) bytes 0x80-0xFF. Unity's runtime doesn't reliably ship code pages.
        const string Windows1250High =
            "€\u0081‚\u0083„…†‡\u0088‰Š‹ŚŤŽŹ" +
            "\u0090‘’“”•–—\u0098™š›śťžź" +
            " ˇ˘Ł¤Ą¦§¨©Ş«¬­®Ż" +
            "°±˛ł´µ¶·¸ąş»Ľ˝ľż" +
            "ŔÁÂĂÄĹĆÇČÉĘËĚÍÎĎ" +
            "ĐŃŇÓÔŐÖ×ŘŮÚŰÜÝŢß" +
            "ŕáâăäĺćçčéęëěíîď" +
            "đńňóôőö÷řůúűüýţ˙";

        /// <summary>SRT and WebVTT: blocks of a timing line followed by text lines, separated by blank lines.</summary>
        static Subtitles ParseSrtOrVtt(string text)
        {
            var cues = new List<SubtitleCue>();
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                double start, end;
                var match = Timing.Match(lines[i]);
                if (match.Success)
                {
                    start = Seconds(match, "h1", "m1", "s1", "f1");
                    end = Seconds(match, "h2", "m2", "s2", "f2");
                }
                else
                {
                    var shortMatch = VttShortTiming.Match(lines[i]);
                    if (!shortMatch.Success) continue;
                    start = Seconds(shortMatch, null, "m1", "s1", "f1");
                    end = Seconds(shortMatch, null, "m2", "s2", "f2");
                }

                var body = new StringBuilder();
                while (i + 1 < lines.Length && lines[i + 1].Trim().Length > 0)
                {
                    if (body.Length > 0) body.Append('\n');
                    body.Append(lines[++i].Trim());
                }
                var clean = Clean(body.ToString());
                if (clean.Length > 0 && end > start) cues.Add(new SubtitleCue(start, end, clean));
            }
            return new Subtitles(cues);
        }

        /// <summary>ASS/SSA: "Dialogue:" lines, fields laid out by the [Events] section's Format line.</summary>
        static Subtitles ParseAss(string text)
        {
            var cues = new List<SubtitleCue>();
            int startField = 1, endField = 2, textField = 9;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
                {
                    var names = line.Substring(7).Split(',');
                    for (var f = 0; f < names.Length; f++)
                    {
                        var name = names[f].Trim().ToLowerInvariant();
                        if (name == "start") startField = f;
                        else if (name == "end") endField = f;
                        else if (name == "text") textField = f;
                    }
                    continue;
                }
                if (!line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase)) continue;

                // The text is the last field and may itself contain commas.
                var fields = line.Substring(9).Split(new[] { ',' }, textField + 1);
                if (fields.Length <= textField) continue;
                if (!TryAssTime(fields[startField], out var start) || !TryAssTime(fields[endField], out var end)) continue;
                var body = Clean(fields[textField].Replace("\\N", "\n").Replace("\\n", "\n").Replace("\\h", " "));
                if (body.Length > 0 && end > start) cues.Add(new SubtitleCue(start, end, body));
            }
            return new Subtitles(cues);
        }

        static bool TryAssTime(string value, out double seconds)
        {
            // h:mm:ss.cc
            seconds = 0;
            var parts = value.Trim().Split(':');
            if (parts.Length != 3) return false;
            if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m) ||
                !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) return false;
            seconds = h * 3600 + m * 60 + s;
            return true;
        }

        static double Seconds(Match m, string h, string min, string s, string f)
        {
            var fraction = m.Groups[f].Value;
            return (h != null ? int.Parse(m.Groups[h].Value) * 3600 : 0) + int.Parse(m.Groups[min].Value) * 60 +
                   int.Parse(m.Groups[s].Value) + int.Parse(fraction) / Math.Pow(10, fraction.Length);
        }

        /// <summary>Removes markup other than italics, and escapes anything TextMeshPro would read as a tag.</summary>
        static string Clean(string text)
        {
            var stripped = MarkupTags.Replace(text, "").Trim();
            if (stripped.Length == 0) return stripped;
            // Keep <i>…</i>; neutralise any other '<' so TextMeshPro shows it as text.
            return stripped.Replace("<i>", "\u0001").Replace("</i>", "\u0002").Replace("<", "\u0003")
                .Replace("\u0003", "<noparse><</noparse>").Replace("\u0001", "<i>").Replace("\u0002", "</i>");
        }
    }
}
