using System.Collections.Generic;
using System.Linq;
using System.Text;
using PlexBuster.Data;
using TMPro;
using UnityEngine;

namespace PlexBuster.Store
{
    /// <summary>
    /// The printed back of a rental case: synopsis, cast, runtime, rating. Only held tapes need one,
    /// so a couple of pooled displays are moved onto whichever tapes are in the player's hands.
    /// </summary>
    public class BackCover : MonoBehaviour
    {
        const float CaseHalfDepth = 0.015f;
        const int MaxSummaryLength = 520;

        static readonly Stack<BackCover> Pool = new();

        TextMeshPro text;

        public static BackCover Attach(VhsTape tape, Material backing)
        {
            BackCover cover = null;
            while (cover == null && Pool.Count > 0) cover = Pool.Pop(); // skip any destroyed with their tape
            if (cover == null) cover = Create(backing);

            cover.transform.SetParent(tape.transform, false);
            cover.transform.SetLocalPositionAndRotation(new Vector3(0, 0, -CaseHalfDepth - 0.0005f), Quaternion.identity);
            cover.text.text = Describe(tape.Item);
            cover.gameObject.SetActive(true);
            return cover;
        }

        public void Detach()
        {
            gameObject.SetActive(false);
            transform.SetParent(null, false);
            Pool.Push(this);
        }

        static BackCover Create(Material backing)
        {
            var go = new GameObject("BackCover");
            var cover = go.AddComponent<BackCover>();

            // The backing faces -Z (the back of the case); Unity's quad already faces that way.
            var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
            card.name = "Card";
            Destroy(card.GetComponent<Collider>());
            card.transform.SetParent(go.transform, false);
            card.transform.localScale = new Vector3(0.12f, 0.19f, 1f);
            card.GetComponent<MeshRenderer>().sharedMaterial = backing;

            cover.text = Signage.CreateText(go.transform, "Text", new Vector3(0, 0, -0.0005f), Quaternion.identity,
                0.1f, new Color(0.08f, 0.08f, 0.1f), new Vector2(0.106f, 0.176f), TextAlignmentOptions.TopLeft);
            cover.text.textWrappingMode = TextWrappingModes.Normal;
            cover.text.enableAutoSizing = true;
            cover.text.fontSizeMin = 0.035f;
            cover.text.fontSizeMax = 0.11f;
            cover.text.lineSpacing = -10f;
            return cover;
        }

        static string Describe(LibraryItem item)
        {
            if (item == null) return "";
            var sb = new StringBuilder();
            sb.Append("<size=150%><b>").Append(Plain(item.Title)).Append("</b></size>\n");

            var facts = new List<string>();
            if (item.Year > 0) facts.Add(item.Year.ToString());
            if (item.Kind == MediaKind.Show)
            {
                if (item.SeasonCount > 0) facts.Add(item.SeasonCount == 1 ? "1 season" : $"{item.SeasonCount} seasons");
                if (item.EpisodeCount > 0) facts.Add($"{item.EpisodeCount} episodes");
            }
            else if (item.DurationMs > 0)
            {
                var minutes = (int)(item.DurationMs / 60000);
                facts.Add(minutes >= 60 ? $"{minutes / 60}h {minutes % 60:00}m" : $"{minutes}m");
            }
            if (!string.IsNullOrEmpty(item.ContentRating)) facts.Add(Plain(item.ContentRating));
            if (item.BestRating > 0) facts.Add($"Rating {item.BestRating:0.0}/10");
            sb.Append(string.Join("  ·  ", facts)).Append('\n');

            if (item.Genres.Count > 0) sb.Append("<i>").Append(Plain(string.Join(" / ", item.Genres))).Append("</i>\n");
            if (!string.IsNullOrEmpty(item.Tagline)) sb.Append("\n<i>\"").Append(Plain(item.Tagline)).Append("\"</i>\n");
            if (!string.IsNullOrEmpty(item.Summary))
            {
                var summary = item.Summary.Length > MaxSummaryLength ? item.Summary.Substring(0, MaxSummaryLength).TrimEnd() + "…" : item.Summary;
                sb.Append('\n').Append(Plain(summary)).Append('\n');
            }
            if (item.Actors.Count > 0) sb.Append("\n<b>Starring</b> ").Append(Plain(string.Join(", ", item.Actors.Take(4))));
            if (item.Directors.Count > 0) sb.Append("\n<b>Directed by</b> ").Append(Plain(string.Join(", ", item.Directors)));
            return sb.ToString();
        }

        /// <summary>Stops TextMeshPro from treating anything in library text as markup.</summary>
        static string Plain(string value) => "<noparse>" + value + "</noparse>";
    }
}
