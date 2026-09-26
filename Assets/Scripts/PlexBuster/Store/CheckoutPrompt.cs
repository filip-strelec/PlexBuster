using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PlexBuster.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PlexBuster.Store
{
    /// <summary>
    /// The till screen for the tape on the checkout counter: pick a TV (every player found, the PLEX_PLAYER one
    /// first and preselected) and play the title on it, resuming where it was left off if you like.
    /// </summary>
    public class CheckoutPrompt : MonoBehaviour
    {
        const int MaxPlayers = 4;

        readonly List<Button> playerButtons = new();
        readonly CancellationTokenSource lifetime = new();
        LibraryItem item;
        IRemotePlayback playback;
        IReadOnlyList<RemotePlayer> players = Array.Empty<RemotePlayer>();
        RemotePlayer selected, playingOn;
        PlaybackPlan plan;
        string error;
        bool searching, starting;
        Action closed;
        TextMeshProUGUI subtitle, status;
        RectTransform startArea;
        Button resumeButton, startButton, searchButton, closeButton;

        public VhsTape Tape { get; private set; }

        /// <param name="localRotation">Its -Z faces the customer.</param>
        /// <param name="closed">Called when the customer presses CANCEL or DONE.</param>
        public static CheckoutPrompt Create(Transform parent, Vector3 localPosition, Quaternion localRotation, VhsTape tape, Action closed)
        {
            var canvas = WorldUI.CreateCanvas(parent, "CheckoutPrompt", new Vector2(760, 540), localPosition, localRotation);
            // Fully opaque: the neon store sign on the wall behind would glow through the usual panel.
            var background = canvas.GetComponent<Image>();
            background.color = new Color(background.color.r, background.color.g, background.color.b, 1f);
            var prompt = canvas.gameObject.AddComponent<CheckoutPrompt>();
            prompt.Tape = tape;
            prompt.item = tape.Item;
            prompt.closed = closed;
            prompt.Build(canvas);
            return prompt;
        }

        void Build(RectTransform canvas)
        {
            WorldUI.Label(WorldUI.Area(canvas, "Title", new Vector2(0, 0.86f), new Vector2(1, 1), 12),
                item.Year > 0 ? $"{item.Title} ({item.Year})" : item.Title, 46);
            subtitle = WorldUI.Label(WorldUI.Area(canvas, "Subtitle", new Vector2(0, 0.78f), new Vector2(1, 0.86f), 6), "", 28);
            WorldUI.Label(WorldUI.Area(canvas, "PlayOn", new Vector2(0, 0.72f), new Vector2(1, 0.78f), 6), "PLAY ON", 24);

            var grid = WorldUI.Area(canvas, "Players", new Vector2(0, 0.45f), new Vector2(1, 0.72f), 12);
            WorldUI.Grid(grid, new Vector2(354, 62), new Vector2(12, 10), 2);
            for (var i = 0; i < MaxPlayers; i++)
            {
                var index = i;
                var button = WorldUI.Button(grid, "", 26, () => Select(index));
                button.gameObject.SetActive(false);
                playerButtons.Add(button);
            }

            var actions = WorldUI.Area(canvas, "Actions", new Vector2(0, 0.29f), new Vector2(1, 0.45f), 6);
            resumeButton = FilledButton(actions, "Resume", new Vector2(0, 0), new Vector2(0.5f, 1), "", 30, () => Play(true));
            startButton = FilledButton(actions, "Start", new Vector2(0.5f, 0), new Vector2(1, 1), "", 30, () => Play(false));
            startArea = (RectTransform)startButton.transform.parent;

            status = WorldUI.Label(WorldUI.Area(canvas, "Status", new Vector2(0, 0.14f), new Vector2(1, 0.29f), 12), "", 26);
            status.textWrappingMode = TextWrappingModes.Normal;

            var footer = WorldUI.Area(canvas, "Footer", new Vector2(0, 0), new Vector2(1, 0.14f), 6);
            searchButton = FilledButton(footer, "Search", new Vector2(0, 0), new Vector2(0.5f, 1), "SEARCH AGAIN", 24, FindPlayers);
            closeButton = FilledButton(footer, "Close", new Vector2(0.5f, 0), new Vector2(1, 1), "", 24, () => closed?.Invoke());
            Refresh();
        }

        /// <summary>A button filling a padded area of <paramref name="parent"/>.</summary>
        static Button FilledButton(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, string text, float fontSize, Action onClick)
        {
            var button = WorldUI.Button(WorldUI.Area(parent, name, anchorMin, anchorMax, 6), text, fontSize, onClick);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return button;
        }

        async void Start()
        {
            var services = StoreServices.Instance;
            await services.WhenReady();
            if (this == null) return;
            playback = services.Playback;
            FindPlayers();

            try
            {
                plan = await playback.PlanAsync(item, lifetime.Token);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Checkout] Couldn't look up {item.Title}: {e.Message}");
                error = $"Couldn't look up this title: {e.Message}";
            }
            if (this != null) Refresh();
        }

        void OnDestroy() => lifetime.Cancel();

        async void FindPlayers()
        {
            if (searching || starting || playback == null) return;
            searching = true;
            error = null;
            Refresh();
            try
            {
                players = (await playback.FindPlayersAsync(lifetime.Token)).Take(MaxPlayers).ToList();
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Checkout] Couldn't search for TVs: {e.Message}");
                error = $"Couldn't search for TVs: {e.Message}";
                players = Array.Empty<RemotePlayer>();
            }
            finally
            {
                searching = false;
            }
            if (this == null) return;
            selected = players.FirstOrDefault(p => p.Id == selected?.Id) ?? players.FirstOrDefault();
            Refresh();
        }

        void Select(int index)
        {
            if (starting || index >= players.Count) return;
            selected = players[index];
            Refresh();
        }

        async void Play(bool resume)
        {
            if (plan == null || selected == null || starting) return;
            var player = selected;
            starting = true;
            error = null;
            playingOn = null;
            Refresh();
            try
            {
                await playback.PlayAsync(player, plan, resume, lifetime.Token);
                playingOn = player;
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Checkout] Couldn't start {item.Title} on {player}: {e.Message}");
                error = $"Couldn't start it on {player.Name}: {e.Message}";
            }
            finally
            {
                starting = false;
            }
            if (this != null) Refresh();
        }

        void Refresh()
        {
            for (var i = 0; i < playerButtons.Count; i++)
            {
                var button = playerButtons[i];
                button.gameObject.SetActive(i < players.Count);
                if (i >= players.Count) continue;
                // Two players with the same name are told apart by model and address.
                var player = players[i];
                var twin = players.Count(p => p.Name == player.Name) > 1;
                WorldUI.SetText(button, twin && !string.IsNullOrEmpty(player.Details) ? $"{player.Name} ({player.Details})" : player.Name);
                WorldUI.SetSelected(button, player == selected);
                button.interactable = !starting;
            }

            subtitle.text = item.Kind == MediaKind.Show
                ? plan?.EpisodeLabel != null ? $"Next up: {plan.EpisodeLabel}" : "TV show"
                : item.DurationMs > 0 ? Duration(item.DurationMs) : "";

            var resumable = plan != null && plan.ResumeMs > 0;
            resumeButton.transform.parent.gameObject.SetActive(resumable);
            startArea.anchorMin = new Vector2(resumable ? 0.5f : 0, 0);
            if (resumable) WorldUI.SetText(resumeButton, $"RESUME AT {Clock(plan.ResumeMs)}");
            WorldUI.SetText(startButton, resumable ? "PLAY FROM START" : "PLAY");
            var canPlay = plan != null && selected != null && !starting && !searching;
            resumeButton.interactable = startButton.interactable = canPlay;
            searchButton.interactable = !starting && !searching;
            WorldUI.SetText(closeButton, playingOn != null ? "DONE" : "CANCEL");

            status.text =
                starting ? $"Starting on {selected?.Name}…" :
                error != null ? error :
                playingOn != null ? $"Now playing on {playingOn.Name}. Enjoy the show!" :
                searching ? "Looking for TVs…" :
                playback == null ? "Opening the till…" :
                players.Count == 0 ? "No TVs found. Open Plex on the TV (with Advertise as player on in its settings), then SEARCH AGAIN." :
                plan == null ? "Checking the tape…" :
                "";
        }

        static string Clock(long ms)
        {
            var t = TimeSpan.FromMilliseconds(ms);
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
        }

        static string Duration(long ms)
        {
            var t = TimeSpan.FromMilliseconds(ms);
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : $"{t.Minutes} min";
        }
    }
}
