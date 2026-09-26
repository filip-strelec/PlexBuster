using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using PlexBuster.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using Object = UnityEngine.Object;

namespace PlexBuster.Store
{
    /// <summary>
    /// The info station's hologram: everything the library knows about one title (poster, ratings, credits, cast
    /// with photos, dates, file details, seasons) on a translucent floating panel, and a screen that plays its
    /// trailer and other online extras. Built at runtime; readable from the parent's +Z side.
    /// </summary>
    public class InfoHologram : MonoBehaviour
    {
        const float WidthMm = 1800;
        const float HeightMm = 1050;
        const int MaxCast = 10;
        const int MaxVideoButtons = 3;
        const float AppearSeconds = 0.35f;
        const float MusicDuck = 0.15f;

        static readonly Color PanelTint = new(0.02f, 0.12f, 0.2f, 0.55f);
        static readonly Color Edge = new(0.4f, 0.95f, 1f, 0.9f);
        static readonly Color Body = new(0.8f, 0.95f, 1f, 1f);
        static readonly Color ImageTint = new(0.85f, 1f, 1f, 0.95f);
        const string Accent = "#8fe9ff";
        const string Gold = "#ffd76a";

        readonly List<Object> owned = new();          // textures loaded for the current title
        readonly List<Object> built = new();          // materials, meshes and textures made for the hologram itself
        readonly List<(RawImage Photo, TextMeshProUGUI Text)> castCells = new();
        readonly List<Button> videoButtons = new();

        Action previous, next;
        RectTransform canvas;
        CanvasGroup group;
        RawImage backdrop, poster, screen, scanlines;
        TextMeshProUGUI info, ratings, position, screenStatus, castHeading, moreCast;
        Button previousButton, nextButton;
        VideoPlayer player;
        AudioSource audioSource;
        RenderTexture videoTexture;
        Material beam;
        Color beamColor;

        CancellationTokenSource loading;
        CancellationTokenSource videoLoading;
        ItemDetails details;
        ExtraVideo playing;
        Texture art;
        AudioSource duckedJukebox;
        float jukeboxVolume;
        float visibility, targetVisibility;

        public VhsTape Shown { get; private set; }

        /// <param name="beamMaterial">Transparent material for the light beam from the projector; none if null.</param>
        /// <param name="projector">Top of the projector the beam rises from, in the parent's space.</param>
        public static InfoHologram Create(Transform parent, Vector3 localCentre, Vector3 projector, Material beamMaterial,
            Action previous, Action next)
        {
            var hologram = new GameObject("Hologram").AddComponent<InfoHologram>();
            hologram.transform.SetParent(parent, false);
            hologram.transform.localPosition = localCentre;
            hologram.previous = previous;
            hologram.next = next;
            hologram.Build(projector - localCentre, beamMaterial);
            hologram.SetVisibility(0);
            return hologram;
        }

        /// <summary>Shows <paramref name="tape"/>'s title; <paramref name="index"/> of <paramref name="count"/> for the arrows.</summary>
        public void Show(VhsTape tape, int index, int count)
        {
            SetPosition(index, count);
            targetVisibility = 1;
            if (tape == Shown) return;

            Clear();
            Shown = tape;
            var item = tape.Item;
            poster.texture = tape.PosterTexture;
            poster.enabled = poster.texture != null;
            info.text = InfoText(item, null);
            ratings.text = RatingsText(item, null);
            screenStatus.text = "Looking for a trailer...";
            moreCast.text = item.Actors.Count > 0 ? $"<color={Accent}>STARRING</color>  " + string.Join(" · ", item.Actors.Select(E)) : "";

            loading = new CancellationTokenSource();
            LoadDetails(item, loading.Token);
        }

        public void SetPosition(int index, int count)
        {
            var several = count > 1;
            previousButton.gameObject.SetActive(several);
            nextButton.gameObject.SetActive(several);
            position.text = several ? $"{index + 1} / {count}" : "";
        }

        public void Hide()
        {
            targetVisibility = 0;
            Clear();
            Shown = null;
        }

        void OnDestroy()
        {
            Clear();
            foreach (var o in built)
                if (o != null) Destroy(o);
        }

        void Update()
        {
            if (!Mathf.Approximately(visibility, targetVisibility))
                SetVisibility(Mathf.MoveTowards(visibility, targetVisibility, Time.deltaTime / AppearSeconds));
            if (visibility <= 0) return;

            // A hologram never quite holds still: faint flicker, drifting scanlines.
            var flicker = 0.93f + 0.07f * Mathf.PerlinNoise(Time.time * 7f, 0.3f);
            group.alpha = visibility * flicker;
            var rect = scanlines.uvRect;
            rect.y = Mathf.Repeat(Time.time * 0.6f, 1f);
            scanlines.uvRect = rect;
            if (beam != null) beam.SetColor(BaseColorId, beamColor * (visibility * flicker));
        }

        void SetVisibility(float value)
        {
            visibility = value;
            // Grows up out of the projector.
            var eased = 1 - (1 - value) * (1 - value);
            canvas.localScale = new Vector3(0.001f, 0.001f * Mathf.Max(0.01f, eased), 0.001f);
            canvas.gameObject.SetActive(value > 0);
            group.alpha = value;
            if (beam != null)
            {
                beam.SetColor(BaseColorId, beamColor * value);
                beamRenderer.enabled = value > 0;
            }
        }

        /// <summary>Stops loads and the trailer, and frees the current title's images.</summary>
        void Clear()
        {
            loading?.Cancel();
            loading = null;
            StopVideo();
            details = null;
            art = null;
            foreach (var o in owned)
                if (o != null) Destroy(o);
            owned.Clear();
            if (poster == null) return; // destroyed with the scene

            poster.texture = null;
            poster.enabled = false;
            backdrop.enabled = false;
            screen.texture = null;
            screen.enabled = false;
            foreach (var (photo, text) in castCells)
            {
                photo.transform.parent.gameObject.SetActive(false);
                photo.texture = null;
                text.text = "";
            }
            foreach (var button in videoButtons) button.gameObject.SetActive(false);
            castHeading.text = "";
            moreCast.text = "";
        }

        async void LoadDetails(LibraryItem item, CancellationToken ct)
        {
            var services = StoreServices.Instance;
            if (services == null) return;
            var library = services.Library;
            LoadImage(item.PosterPath, 480, 720, ct, texture =>
            {
                poster.texture = texture;
                poster.enabled = true;
            });

            ItemDetails loaded;
            try
            {
                loaded = await library.GetDetailsAsync(item, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Info] Details for {item.Title}: {e.Message}");
                if (!ct.IsCancellationRequested && this != null) screenStatus.text = "Details unavailable";
                return;
            }
            if (ct.IsCancellationRequested || this == null) return;

            details = loaded;
            info.text = InfoText(item, loaded);
            ratings.text = RatingsText(item, loaded);
            ShowCast(loaded, ct);
            ShowVideos(loaded);

            LoadImage(loaded.ArtPath, 960, 540, ct, texture =>
            {
                art = texture;
                backdrop.texture = texture;
                backdrop.enabled = true;
                if (playing == null)
                {
                    screen.texture = texture;
                    screen.enabled = true;
                }
            });
        }

        void ShowCast(ItemDetails loaded, CancellationToken ct)
        {
            castHeading.text = loaded.Cast.Count > 0 ? "CAST" : "";
            for (var i = 0; i < castCells.Count; i++)
            {
                var (photo, text) = castCells[i];
                var cell = photo.transform.parent.gameObject;
                if (i >= loaded.Cast.Count)
                {
                    cell.SetActive(false);
                    continue;
                }
                var member = loaded.Cast[i];
                cell.SetActive(true);
                text.text = string.IsNullOrEmpty(member.Role)
                    ? $"<b>{E(member.Name)}</b>"
                    : $"<b>{E(member.Name)}</b>\n<color={Accent}><size=85%>{E(member.Role)}</size></color>";
                photo.texture = null;
                photo.color = new Color(0.3f, 0.6f, 0.7f, 0.35f);
                photo.uvRect = new Rect(0, 0, 1, 1);
                photo.enabled = true;
                var target = photo;
                LoadImage(member.PhotoPath, 128, 128, ct, texture =>
                {
                    target.texture = texture;
                    target.color = ImageTint;
                    // Show the top-centre square of a portrait photo (where the face is).
                    var aspect = texture.width / (float)texture.height;
                    target.uvRect = aspect < 1
                        ? new Rect(0, (1 - aspect) * 0.8f, 1, aspect)
                        : new Rect((1 - 1 / aspect) / 2, 0, 1 / aspect, 1);
                });
            }

            var rest = loaded.Cast.Skip(MaxCast).Take(24).Select(c => E(c.Name)).ToList();
            moreCast.text = rest.Count == 0 ? "" : $"<color={Accent}>ALSO</color>  " + string.Join(" · ", rest);
        }

        void ShowVideos(ItemDetails loaded)
        {
            var videos = loaded.Videos.Take(MaxVideoButtons).ToList();
            screenStatus.text = videos.Count == 0 ? "No trailer available" : "";
            for (var i = 0; i < videoButtons.Count; i++)
            {
                var button = videoButtons[i];
                button.gameObject.SetActive(i < videos.Count);
                if (i < videos.Count) WorldUI.SetText(button, VideoLabel(videos[i], false));
            }
        }

        void OnVideoButton(int index)
        {
            if (details == null || index >= details.Videos.Count) return;
            var video = details.Videos[index];
            if (playing == video) StopVideo();
            else PlayVideo(video);
        }

        async void PlayVideo(ExtraVideo video)
        {
            StopVideo();
            playing = video;
            UpdateVideoButtons();
            screenStatus.text = "Loading...";
            videoLoading = new CancellationTokenSource();
            var ct = videoLoading.Token;

            string url;
            try
            {
                url = await StoreServices.Instance.Library.ResolveVideoUrlAsync(video, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Info] Trailer for {Shown?.Item?.Title}: {e.Message}");
                url = null;
            }
            if (ct.IsCancellationRequested || this == null || playing != video) return;
            if (url == null)
            {
                playing = null;
                UpdateVideoButtons();
                screenStatus.text = "This video can't be played";
                return;
            }

            EnsurePlayer();
            player.url = url;
            player.Prepare();
            DuckJukebox(true);
        }

        void StopVideo()
        {
            videoLoading?.Cancel();
            videoLoading = null;
            if (playing == null) return;
            playing = null;
            if (player != null)
            {
                player.Stop();
            }
            DuckJukebox(false);
            if (screen == null) return;
            screen.texture = art;
            screen.enabled = art != null;
            screenStatus.text = "";
            UpdateVideoButtons();
        }

        void UpdateVideoButtons()
        {
            if (details == null) return;
            for (var i = 0; i < videoButtons.Count && i < details.Videos.Count; i++)
                WorldUI.SetText(videoButtons[i], VideoLabel(details.Videos[i], details.Videos[i] == playing));
        }

        static string VideoLabel(ExtraVideo video, bool isPlaying)
        {
            if (isPlaying) return "STOP";
            var kind = video.Kind switch
            {
                "trailer" => "TRAILER",
                "behindTheScenes" => "BEHIND THE SCENES",
                "deletedScene" => "DELETED SCENE",
                "interview" => "INTERVIEW",
                "featurette" => "FEATURETTE",
                "sceneOrSample" => "SCENE",
                "short" => "SHORT",
                _ => "EXTRA",
            };
            return video.DurationMs > 0 ? $"PLAY {kind}  {Runtime(video.DurationMs, true)}" : $"PLAY {kind}";
        }

        void EnsurePlayer()
        {
            if (player != null) return;
            var go = new GameObject("Video");
            go.transform.SetParent(transform, false);
            audioSource = go.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0.6f;
            audioSource.dopplerLevel = 0;
            audioSource.minDistance = 2f;
            audioSource.maxDistance = 20f;

            videoTexture = new RenderTexture(1280, 720, 0) { name = "Trailer" };
            built.Add(videoTexture);

            player = go.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.source = VideoSource.Url;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = videoTexture;
            player.aspectRatio = VideoAspectRatio.FitInside;
            player.skipOnDrop = true;
            player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            player.controlledAudioTrackCount = 1;
            player.EnableAudioTrack(0, true);
            player.SetTargetAudioSource(0, audioSource);
            player.prepareCompleted += OnPrepared;
            player.loopPointReached += _ => StopVideo();
            player.errorReceived += (_, message) =>
            {
                Debug.LogWarning($"[Info] Video error: {message}");
                StopVideo();
                if (screenStatus != null) screenStatus.text = "This video can't be played";
            };
        }

        void OnPrepared(VideoPlayer source)
        {
            if (playing == null) return;
            screenStatus.text = "";
            screen.texture = videoTexture;
            screen.enabled = true;
            source.Play();
        }

        /// <summary>Turns the lobby jukebox down while a trailer plays.</summary>
        void DuckJukebox(bool duck)
        {
            if (duck && duckedJukebox == null)
            {
                var jukebox = FindAnyObjectByType<Jukebox>();
                if (jukebox == null || !jukebox.TryGetComponent(out duckedJukebox)) return;
                jukeboxVolume = duckedJukebox.volume;
                duckedJukebox.volume = jukeboxVolume * MusicDuck;
            }
            else if (!duck && duckedJukebox != null)
            {
                duckedJukebox.volume = jukeboxVolume;
                duckedJukebox = null;
            }
        }

        async void LoadImage(string path, int width, int height, CancellationToken ct, Action<Texture2D> onLoaded)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var texture = await StoreServices.Instance.Library.LoadImageAsync(path, width, height, ct);
                if (texture == null) return;
                if (ct.IsCancellationRequested || this == null)
                {
                    Destroy(texture);
                    return;
                }
                owned.Add(texture);
                onLoaded(texture);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Debug.LogWarning($"[Info] Image: {e.Message}");
            }
        }

        static string InfoText(LibraryItem item, ItemDetails d)
        {
            var text = new StringBuilder();
            text.Append($"<size=230%><b>{E(item.Title)}</b></size>\n");
            if (!string.IsNullOrEmpty(d?.OriginalTitle)) text.Append($"<size=125%><color={Accent}>{E(d.OriginalTitle)}</color></size>\n");

            var facts = new List<string>();
            if (item.Year > 0) facts.Add(item.Year.ToString());
            if (item.DurationMs > 0) facts.Add(Runtime(item.DurationMs, false));
            if (item.Kind == MediaKind.Show && item.SeasonCount > 0)
                facts.Add($"{Plural(item.SeasonCount, "season")} · {Plural(item.EpisodeCount, "episode")}");
            if (!string.IsNullOrEmpty(item.ContentRating)) facts.Add(item.ContentRating);
            if (!string.IsNullOrEmpty(item.Studio)) facts.Add(item.Studio);
            text.Append($"<size=115%>{E(string.Join("  ·  ", facts))}</size>\n");

            var genres = d?.Genres.Count > 0 ? d.Genres : item.Genres;
            if (genres.Count > 0) text.Append($"<color={Gold}>{E(string.Join(" · ", genres))}</color>\n");
            if (!string.IsNullOrEmpty(item.Tagline)) text.Append($"<i>\"{E(item.Tagline)}\"</i>\n");
            if (!string.IsNullOrEmpty(item.Summary)) text.Append($"<line-height=115%>\n</line-height>{E(item.Summary)}\n");
            text.Append('\n');

            var directors = d?.Directors.Count > 0 ? d.Directors : item.Directors;
            Credit(text, directors.Count > 1 ? "DIRECTORS" : "DIRECTOR", directors);
            if (d != null)
            {
                Credit(text, "WRITTEN BY", d.Writers);
                Credit(text, "PRODUCED BY", d.Producers.Take(5).ToList());
                Credit(text, d.Countries.Count > 1 ? "COUNTRIES" : "COUNTRY", d.Countries);
                Credit(text, "COLLECTION", d.Collections);

                var dates = new List<string>();
                if (d.Released.HasValue) dates.Add($"<color={Accent}>RELEASED</color>  {Date(d.Released.Value)}");
                if (d.Added.HasValue) dates.Add($"<color={Accent}>IN THE LIBRARY SINCE</color>  {Date(d.Added.Value)}");
                if (dates.Count > 0) text.Append(string.Join("     ", dates)).Append('\n');

                var watched = d.ViewCount switch
                {
                    0 when d.LastViewed == null => "not yet",
                    0 => $"last {Date(d.LastViewed.Value)}",
                    1 => d.LastViewed.HasValue ? $"once, {Date(d.LastViewed.Value)}" : "once",
                    var n => d.LastViewed.HasValue ? $"{n} times, last {Date(d.LastViewed.Value)}" : $"{n} times",
                };
                text.Append($"<color={Accent}>WATCHED</color>  {watched}\n");
                if (d.Seasons.Count > 0) Credit(text, "SEASONS", d.Seasons, ",  ");
                if (!string.IsNullOrEmpty(d.MediaSummary)) text.Append($"<color={Accent}>FILE</color>  {E(d.MediaSummary)}\n");
                if (!string.IsNullOrEmpty(d.SectionTitle)) text.Append($"<color={Accent}>SHELVED IN</color>  {E(d.SectionTitle)}\n");
            }
            return text.ToString();
        }

        static void Credit(StringBuilder text, string heading, IReadOnlyCollection<string> names, string separator = ", ")
        {
            if (names == null || names.Count == 0) return;
            text.Append($"<color={Accent}>{heading}</color>  {E(string.Join(separator, names))}\n");
        }

        static string RatingsText(LibraryItem item, ItemDetails d)
        {
            var lines = new List<string>();
            if (d?.Ratings.Count > 0)
            {
                foreach (var r in d.Ratings)
                {
                    var label = r.Source switch
                    {
                        "Rotten Tomatoes" => r.Kind == "critic" ? "Tomatometer" : "RT Audience",
                        var source => r.Kind == "critic" && source != "IMDb" ? $"{source} critics" : source,
                    };
                    lines.Add($"<size=150%><b>{E(r.Display)}</b></size>  <color={Accent}>{E(label)}</color>");
                }
            }
            else
            {
                if (item.Rating > 0) lines.Add($"<size=150%><b>{Score(item.Rating)}</b></size>  <color={Accent}>critics</color>");
                if (item.AudienceRating > 0) lines.Add($"<size=150%><b>{Score(item.AudienceRating)}</b></size>  <color={Accent}>audience</color>");
            }
            return lines.Count == 0 ? $"<color={Accent}>No ratings</color>" : string.Join("\n", lines);
        }

        static string Runtime(long ms, bool clock)
        {
            var span = TimeSpan.FromMilliseconds(ms);
            if (clock) return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
            return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes:00}m" : $"{span.Minutes} min";
        }

        static string Score(float score) => score.ToString("0.0", CultureInfo.InvariantCulture);
        static string Date(DateTime date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        static string Plural(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

        /// <summary>Library text, shown as-is rather than parsed for rich-text tags.</summary>
        static string E(string text) => string.IsNullOrEmpty(text) ? "" : $"<noparse>{text}</noparse>";

        // ---------------------------------------------------------------------------------------------------------
        // Building

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        MeshRenderer beamRenderer;

        void Build(Vector3 projector, Material beamMaterial)
        {
            canvas = WorldUI.CreateCanvas(transform, "Canvas", new Vector2(WidthMm, HeightMm), Vector3.zero, Quaternion.Euler(0, 180, 0));
            canvas.GetComponent<Image>().color = PanelTint;
            // Grows from its bottom edge, where the beam meets it.
            canvas.pivot = new Vector2(0.5f, 0);
            canvas.localPosition = new Vector3(0, -HeightMm / 2000f, 0);
            group = canvas.gameObject.AddComponent<CanvasGroup>();

            backdrop = Picture(canvas, "Backdrop", Vector2.zero, Vector2.one, 0);
            backdrop.color = new Color(0.5f, 0.9f, 1f, 0.12f);
            Frame(canvas);

            // Left: poster, ratings, arrows.
            poster = Picture(canvas, "Poster", new Vector2(0.017f, 0.37f), new Vector2(0.25f, 0.971f), 0);
            ratings = Text(WorldUI.Area(canvas, "Ratings", new Vector2(0.017f, 0.12f), new Vector2(0.25f, 0.36f)), 30, TextAlignmentOptions.TopLeft);
            var nav = WorldUI.Area(canvas, "Arrows", new Vector2(0.017f, 0.025f), new Vector2(0.25f, 0.105f));
            previousButton = WorldUI.Button(nav, "<", 44, () => previous?.Invoke());
            Place(previousButton.transform, new Vector2(0, 0), new Vector2(0.3f, 1));
            position = Text(WorldUI.Area(nav, "Position", new Vector2(0.3f, 0), new Vector2(0.7f, 1)), 34, TextAlignmentOptions.Center);
            nextButton = WorldUI.Button(nav, ">", 44, () => next?.Invoke());
            Place(nextButton.transform, new Vector2(0.7f, 0), new Vector2(1, 1));

            // Middle: everything written about the title.
            info = Text(WorldUI.Area(canvas, "Info", new Vector2(0.265f, 0.025f), new Vector2(0.63f, 0.971f)), 31, TextAlignmentOptions.TopLeft);
            info.fontSizeMin = 14;

            // Right: trailer screen and its buttons, then the cast.
            var screenArea = WorldUI.Area(canvas, "Screen", new Vector2(0.645f, 0.645f), new Vector2(0.983f, 0.971f));
            screenArea.gameObject.AddComponent<Image>().color = new Color(0, 0.02f, 0.04f, 0.8f);
            screen = Picture(screenArea, "Picture", Vector2.zero, Vector2.one, 0);
            screenStatus = Text(WorldUI.Area(screenArea, "Status", Vector2.zero, Vector2.one, 20), 30, TextAlignmentOptions.Center);

            var buttons = WorldUI.Area(canvas, "VideoButtons", new Vector2(0.645f, 0.575f), new Vector2(0.983f, 0.635f));
            var layout = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childForceExpandWidth = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            for (var i = 0; i < MaxVideoButtons; i++)
            {
                var index = i;
                var button = WorldUI.Button(buttons, "PLAY TRAILER", 22, () => OnVideoButton(index));
                button.gameObject.SetActive(false);
                videoButtons.Add(button);
            }

            castHeading = Text(WorldUI.Area(canvas, "CastHeading", new Vector2(0.645f, 0.525f), new Vector2(0.983f, 0.565f)), 28, TextAlignmentOptions.BottomLeft);
            castHeading.color = Edge;
            var cast = WorldUI.Area(canvas, "Cast", new Vector2(0.645f, 0.165f), new Vector2(0.983f, 0.52f));
            var grid = WorldUI.Grid(cast, new Vector2(299, 70), new Vector2(10, 4), 2);
            grid.childAlignment = TextAnchor.UpperLeft;
            for (var i = 0; i < MaxCast; i++) castCells.Add(CastCell(cast));
            moreCast = Text(WorldUI.Area(canvas, "MoreCast", new Vector2(0.645f, 0.025f), new Vector2(0.983f, 0.16f)), 23, TextAlignmentOptions.TopLeft);
            moreCast.fontSizeMin = 12;

            scanlines = Picture(canvas, "Scanlines", Vector2.zero, Vector2.one, 0);
            scanlines.texture = ScanlineTexture();
            scanlines.color = new Color(0.6f, 0.95f, 1f, 0.06f);
            scanlines.uvRect = new Rect(0, 0, 1, HeightMm / 9f);
            scanlines.enabled = true;

            if (beamMaterial != null) BuildBeam(projector, beamMaterial);
            Clear();
        }

        (RawImage, TextMeshProUGUI) CastCell(Transform grid)
        {
            var cell = new GameObject("CastMember", typeof(RectTransform));
            cell.transform.SetParent(grid, false);
            var photo = Picture((RectTransform)cell.transform, "Photo", Vector2.zero, new Vector2(0, 1), 0);
            photo.rectTransform.sizeDelta = new Vector2(70, 0);
            photo.rectTransform.anchoredPosition = new Vector2(35, 0);
            var text = Text(WorldUI.Area(cell.transform, "Name", Vector2.zero, Vector2.one), 25, TextAlignmentOptions.Left);
            text.rectTransform.offsetMin = new Vector2(80, 0);
            text.fontSizeMin = 12;
            text.textWrappingMode = TextWrappingModes.Normal;
            cell.SetActive(false);
            return (photo, text);
        }

        static RawImage Picture(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, float padding)
        {
            var image = WorldUI.Area(parent, name, anchorMin, anchorMax, padding).gameObject.AddComponent<RawImage>();
            image.color = ImageTint;
            image.raycastTarget = false;
            image.enabled = false;
            return image;
        }

        static TextMeshProUGUI Text(Transform area, float size, TextAlignmentOptions alignment)
        {
            var text = WorldUI.Label(area, "", size, alignment);
            text.color = Body;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = true;
            return text;
        }

        static void Place(Transform child, Vector2 anchorMin, Vector2 anchorMax)
        {
            var rect = (RectTransform)child;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(6, 6);
            rect.offsetMax = new Vector2(-6, -6);
        }

        /// <summary>Glowing edges, with brighter corner brackets.</summary>
        static void Frame(RectTransform canvas)
        {
            void Line(string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax, float alpha)
            {
                var rect = WorldUI.Area(canvas, name, min, max);
                rect.offsetMin = offsetMin;
                rect.offsetMax = offsetMax;
                var image = rect.gameObject.AddComponent<Image>();
                image.color = new Color(Edge.r, Edge.g, Edge.b, alpha);
                image.raycastTarget = false;
            }

            Line("Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -3), Vector2.zero, 0.5f);
            Line("Bottom", Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 3), 0.5f);
            Line("Left", Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(3, 0), 0.5f);
            Line("Right", new Vector2(1, 0), Vector2.one, new Vector2(-3, 0), Vector2.zero, 0.5f);
            for (var x = 0; x <= 1; x++)
            for (var y = 0; y <= 1; y++)
            {
                var corner = new Vector2(x, y);
                var inward = new Vector2(x == 0 ? 1 : -1, y == 0 ? 1 : -1);
                Line("CornerH", corner, corner, Vector2.Min(Vector2.zero, inward * new Vector2(90, 8)), Vector2.Max(Vector2.zero, inward * new Vector2(90, 8)), 1);
                Line("CornerV", corner, corner, Vector2.Min(Vector2.zero, inward * new Vector2(8, 90)), Vector2.Max(Vector2.zero, inward * new Vector2(8, 90)), 1);
            }
        }

        Texture2D ScanlineTexture()
        {
            var texture = new Texture2D(1, 3, TextureFormat.RGBA32, false)
            {
                name = "Scanlines", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat,
            };
            texture.SetPixels32(new Color32[] { new(255, 255, 255, 255), new(255, 255, 255, 0), new(255, 255, 255, 0) });
            texture.Apply(false, true);
            built.Add(texture);
            return texture;
        }

        /// <summary>A fan of light from the projector up to the panel's bottom edge.</summary>
        void BuildBeam(Vector3 projector, Material template)
        {
            const float footprint = 0.1f;
            var bottom = -HeightMm / 2000f;
            var halfWidth = WidthMm / 2000f;
            var vertices = new[]
            {
                projector + new Vector3(-footprint, 0, -footprint), projector + new Vector3(footprint, 0, -footprint),
                projector + new Vector3(footprint, 0, footprint), projector + new Vector3(-footprint, 0, footprint),
                new Vector3(-halfWidth, bottom, -0.01f), new Vector3(halfWidth, bottom, -0.01f),
                new Vector3(halfWidth, bottom, 0.01f), new Vector3(-halfWidth, bottom, 0.01f),
            };
            // Sides only (no caps); the material is double-sided.
            var triangles = new[] { 0, 4, 5, 0, 5, 1, 1, 5, 6, 1, 6, 2, 2, 6, 7, 2, 7, 3, 3, 7, 4, 3, 4, 0 };
            var mesh = new Mesh { name = "HologramBeam", vertices = vertices, triangles = triangles };
            mesh.uv = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(1, 1),
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            built.Add(mesh);

            var go = new GameObject("Beam", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            beamRenderer = go.GetComponent<MeshRenderer>();
            beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beamRenderer.receiveShadows = false;
            beam = new Material(template) { name = "HologramBeam (Instance)" };
            beamColor = template.GetColor(BaseColorId);
            beamRenderer.sharedMaterial = beam;
            built.Add(beam);
        }
    }
}
