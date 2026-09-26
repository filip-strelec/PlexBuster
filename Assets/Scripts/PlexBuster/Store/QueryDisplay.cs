using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PlexBuster.Data;
using UnityEngine;

namespace PlexBuster.Store
{
    /// <summary>
    /// Fills a run of shelves with the result of one library query, e.g. "Genre: Horror, A-Z"
    /// or "Recently added". Tapes are spawned a few per frame to avoid hitches in VR.
    /// </summary>
    public class QueryDisplay : MonoBehaviour
    {
        [SerializeField] VhsTape tapePrefab;
        [SerializeField] List<ShelfUnit> shelves = new();

        [Header("Query")]
        [SerializeField] FilterType filter = FilterType.Genre;
        [SerializeField, Tooltip("Title of the filter value, e.g. \"Horror\". Empty picks the first value.")]
        string value = "Horror";
        [SerializeField] SortOrder sort = SortOrder.Title;
        [SerializeField] bool includeMovies = true;
        [SerializeField] bool includeShows = true;
        [SerializeField] bool showOnStart = true;

        [SerializeField, Min(1)] int tapesPerFrame = 24;

        Coroutine filling;

        public int Capacity => shelves.Sum(s => s.Capacity);

        async void Start()
        {
            if (!showOnStart) return;
            var services = StoreServices.Instance;
            await services.WhenReady();
            if (this == null) return;

            try
            {
                var query = new LibraryQuery
                {
                    Filter = filter, Sort = sort, Limit = Capacity,
                    IncludeMovies = includeMovies, IncludeShows = includeShows,
                };
                if (filter != FilterType.All && filter != FilterType.RecentlyAdded)
                {
                    var values = await services.Library.GetFilterValuesAsync(filter, services.LifetimeToken);
                    query.Value = values.FirstOrDefault(v => string.Equals(v.Title, value, StringComparison.OrdinalIgnoreCase))
                                  ?? values.FirstOrDefault();
                    if (query.Value == null)
                    {
                        Debug.LogWarning($"[Store] {name}: no {filter} values in the library.", this);
                        return;
                    }
                }

                var items = await services.Library.QueryAsync(query, services.LifetimeToken);
                if (this == null) return;
                Debug.Log($"[Store] {name}: {query} -> {items.Count} items", this);
                Show(items, services.Posters);
            }
            catch (OperationCanceledException) { }
        }

        public void Show(IReadOnlyList<LibraryItem> items, PosterCache posters)
        {
            if (filling != null) StopCoroutine(filling);
            filling = StartCoroutine(Fill(items, posters));
        }

        IEnumerator Fill(IReadOnlyList<LibraryItem> items, PosterCache posters)
        {
            foreach (var shelf in shelves) shelf.Clear();

            var shelfIndex = 0;
            for (var i = 0; i < items.Count; i++)
            {
                while (shelfIndex < shelves.Count && shelves[shelfIndex].IsFull) shelfIndex++;
                if (shelfIndex >= shelves.Count) break;

                shelves[shelfIndex].AddTape(items[i], tapePrefab, posters);
                if ((i + 1) % tapesPerFrame == 0) yield return null;
            }
            filling = null;
        }
    }
}
