using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PlexBuster.Store
{
    /// <summary>
    /// The control panel for a <see cref="ScreenPlayer"/>: what's playing, a progress bar to drag, skip and pause
    /// buttons, episodes, subtitles, volume and eject. Works with the controller ray or by poking.
    /// </summary>
    public class PlaybackControls : MonoBehaviour
    {
        const float RefreshSeconds = 0.25f;
        static readonly Color Accent = new(0.55f, 0.85f, 1f);

        ScreenPlayer player;
        Action eject;
        string idleMessage;
        TextMeshProUGUI title, detail, elapsed, remaining, idle;
        Slider bar;
        Button previous, next, playPause, subtitles, startOver, zoom;
        GameObject panel;
        bool dragging;
        float nextRefresh;

        /// <param name="localRotation">Readable from the -Z side of this rotation, like the store's other panels.</param>
        public static PlaybackControls Create(Transform parent, Vector3 localPosition, Quaternion localRotation,
            ScreenPlayer player, Action eject, string idleMessage)
        {
            var canvas = WorldUI.CreateCanvas(parent, "PlaybackControls", new Vector2(640, 340), localPosition, localRotation);
            var controls = canvas.gameObject.AddComponent<PlaybackControls>();
            controls.player = player;
            controls.eject = eject;
            controls.idleMessage = idleMessage;
            controls.Build(canvas);
            player.Changed += controls.Refresh;
            controls.Refresh();
            return controls;
        }

        void OnDestroy()
        {
            if (player != null) player.Changed -= Refresh;
        }

        void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            RefreshTime();
        }

        void Build(RectTransform canvas)
        {
            idle = WorldUI.Label(WorldUI.Area(canvas, "Idle", Vector2.zero, Vector2.one, 30), "", 34);
            idle.textWrappingMode = TextWrappingModes.Normal;

            panel = WorldUI.Area(canvas, "Panel", Vector2.zero, Vector2.one).gameObject;
            var root = (RectTransform)panel.transform;

            title = WorldUI.Label(WorldUI.Area(root, "Title", new Vector2(0.03f, 0.8f), new Vector2(0.97f, 0.96f)), "", 34, TextAlignmentOptions.Left);
            title.fontStyle = FontStyles.Bold;
            detail = WorldUI.Label(WorldUI.Area(root, "Detail", new Vector2(0.03f, 0.69f), new Vector2(0.97f, 0.8f)), "", 20, TextAlignmentOptions.Left);
            detail.color = Accent;

            elapsed = WorldUI.Label(WorldUI.Area(root, "Elapsed", new Vector2(0.02f, 0.55f), new Vector2(0.17f, 0.68f)), "", 22, TextAlignmentOptions.Right);
            remaining = WorldUI.Label(WorldUI.Area(root, "Duration", new Vector2(0.83f, 0.55f), new Vector2(0.98f, 0.68f)), "", 22, TextAlignmentOptions.Left);
            bar = ProgressBar(WorldUI.Area(root, "Progress", new Vector2(0.19f, 0.57f), new Vector2(0.81f, 0.66f)));

            var transport = WorldUI.Area(root, "Transport", new Vector2(0.02f, 0.3f), new Vector2(0.98f, 0.52f));
            WorldUI.Grid(transport, new Vector2(80, 66), new Vector2(7, 0), 7);
            previous = WorldUI.Button(transport, "|<", 26, () =>
            {
                if (player.HasPreviousEpisode) player.PreviousEpisode();
                else player.StartOver();
            });
            WorldUI.Button(transport, "<< 30", 22, () => player.SeekBy(-30));
            WorldUI.Button(transport, "<< 10", 22, () => player.SeekBy(-10));
            playPause = WorldUI.Button(transport, "PAUSE", 22, player.TogglePause);
            WorldUI.Button(transport, "10 >>", 22, () => player.SeekBy(10));
            WorldUI.Button(transport, "30 >>", 22, () => player.SeekBy(30));
            next = WorldUI.Button(transport, ">|", 26, player.NextEpisode);

            var options = WorldUI.Area(root, "Options", new Vector2(0.02f, 0.05f), new Vector2(0.98f, 0.27f));
            WorldUI.Grid(options, new Vector2(93, 66), new Vector2(7, 0), 6);
            subtitles = WorldUI.Button(options, "SUBS: OFF", 18, player.CycleSubtitles);
            zoom = WorldUI.Button(options, "FIT", 20, () => player.Fill = !player.Fill);
            WorldUI.Button(options, "VOL -", 20, () => player.Volume -= 0.1f);
            WorldUI.Button(options, "VOL +", 20, () => player.Volume += 0.1f);
            startOver = WorldUI.Button(options, "START OVER", 18, player.StartOver);
            WorldUI.Button(options, "EJECT", 20, () => eject?.Invoke());
        }

        /// <summary>A slider to scrub through the title; it only seeks when let go.</summary>
        Slider ProgressBar(RectTransform area)
        {
            area.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.03f, 0.1f, 1f);

            var fillArea = WorldUI.Area(area, "Fill Area", Vector2.zero, Vector2.one);
            var fill = WorldUI.Area(fillArea, "Fill", Vector2.zero, new Vector2(0, 1)).gameObject.AddComponent<Image>();
            fill.color = WorldUI.ButtonNormal * 1.6f;

            var handleArea = WorldUI.Area(area, "Handle Area", Vector2.zero, Vector2.one);
            handleArea.offsetMin = new Vector2(8, 0);
            handleArea.offsetMax = new Vector2(-8, 0);
            var handle = WorldUI.Area(handleArea, "Handle", Vector2.zero, new Vector2(0, 1)).gameObject.AddComponent<Image>();
            handle.rectTransform.sizeDelta = new Vector2(16, 14);
            handle.color = WorldUI.ButtonSelected;

            var slider = area.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;

            var trigger = area.gameObject.AddComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.PointerDown, () => dragging = true);
            AddTrigger(trigger, EventTriggerType.PointerUp, () =>
            {
                dragging = false;
                if (player.DurationSeconds > 0) player.SeekTo(slider.value * player.DurationSeconds);
            });
            slider.onValueChanged.AddListener(value =>
            {
                if (dragging) elapsed.text = Clock(value * player.DurationSeconds);
            });
            return slider;
        }

        static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => action());
            trigger.triggers.Add(entry);
        }

        void Refresh()
        {
            var state = player.Current;
            var active = state != ScreenPlayer.State.Idle;
            panel.SetActive(active);
            idle.gameObject.SetActive(!active);
            idle.text = idleMessage;
            if (!active) return;

            title.text = player.Title ?? "";
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(player.Subtitle)) parts.Add(player.Subtitle);
            if (!string.IsNullOrEmpty(player.Status)) parts.Add(player.Status);
            else if (!string.IsNullOrEmpty(player.Description)) parts.Add(player.Description);
            detail.text = string.Join("  ·  ", parts);

            WorldUI.SetText(playPause, state == ScreenPlayer.State.Paused ? "PLAY" : "PAUSE");
            WorldUI.SetSelected(playPause, state == ScreenPlayer.State.Paused);
            next.interactable = player.HasNextEpisode;
            previous.interactable = state is ScreenPlayer.State.Playing or ScreenPlayer.State.Paused;
            startOver.interactable = previous.interactable;

            var tracks = player.SubtitleTracks;
            subtitles.interactable = tracks.Count > 0 && !player.SubtitlesBurntIn;
            WorldUI.SetText(subtitles, player.SubtitlesBurntIn ? "SUBS: IN PICTURE"
                : tracks.Count == 0 ? "NO SUBS"
                : $"SUBS: {player.CurrentSubtitle?.Label ?? "OFF"}");
            WorldUI.SetText(zoom, player.Fill ? "FILL" : "FIT");
            RefreshTime();
        }

        void RefreshTime()
        {
            if (!panel.activeSelf || dragging) return;
            var duration = player.DurationSeconds;
            var position = player.PositionSeconds;
            elapsed.text = Clock(position);
            remaining.text = duration > 0 ? Clock(duration) : "";
            bar.SetValueWithoutNotify(duration > 0 ? (float)(position / duration) : 0);
        }

        static string Clock(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
        }
    }
}
