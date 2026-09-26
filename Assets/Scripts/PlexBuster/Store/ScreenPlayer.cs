using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PlexBuster.Data;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;

namespace PlexBuster.Store
{
    /// <summary>
    /// Plays library titles onto a render texture for a screen in the store (the TV room's CRT, the cinema):
    /// picks what to play (a show continues where it was left off), streams it from the library, and handles
    /// pause, seeking, episodes, subtitles and progress reports so the title resumes anywhere. Screens and
    /// controls read its state and call its methods; <see cref="Changed"/> fires when anything they show changes.
    /// </summary>
    [DisallowMultipleComponent]
    public class ScreenPlayer : MonoBehaviour
    {
        public enum State { Idle, Loading, Playing, Paused, Failed }

        const float ProgressReportSeconds = 10f;
        const float SeekSettleSeconds = 0.7f;
        const float ColorSampleSeconds = 0.15f;
        const int MaxRecoveries = 3;

        VideoPlayer video;
        AudioSource audioSource;
        RenderTexture texture;
        CancellationTokenSource session;
        VideoStream stream;
        ILibrarySource library;     // kept, so a stream can still be closed while the scene is torn down
        LibraryItem item;
        IReadOnlyList<EpisodeRef> episodes = Array.Empty<EpisodeRef>();
        int episodeIndex = -1;
        Subtitles subtitles;
        long startAtMs;             // for a file played as it is: where to jump once it's ready
        long positionMs;            // last known position in the title
        double pendingSeek = -1;
        float seekDue, nextReport, nextColorSample;
        int recoveries;
        bool readbackPending;

        public event Action Changed;

        public State Current { get; private set; }
        public LibraryItem Item => item;
        public string Title => item?.Title;
        /// <summary>The episode for a show ("S1 · E3 · Title"), else the year.</summary>
        public string Subtitle => episodeIndex >= 0 ? episodes[episodeIndex].Label : item != null && item.Year > 0 ? item.Year.ToString() : null;
        public string Status { get; private set; }
        public string Description => stream?.Description;
        public RenderTexture Texture => texture;
        public float VideoAspect { get; private set; } = 16f / 9f;
        public double PositionSeconds => (Current == State.Playing || Current == State.Paused) && video.isPrepared && pendingSeek < 0
            ? stream.StartMs / 1000.0 + video.time
            : (pendingSeek >= 0 ? pendingSeek : positionMs / 1000.0);
        public double DurationSeconds => stream != null ? stream.DurationMs / 1000.0 : (item?.DurationMs ?? 0) / 1000.0;
        public bool HasPreviousEpisode => episodeIndex > 0;
        public bool HasNextEpisode => episodeIndex >= 0 && episodeIndex < episodes.Count - 1;
        public IReadOnlyList<SubtitleTrack> SubtitleTracks => stream?.Subtitles ?? (IReadOnlyList<SubtitleTrack>)Array.Empty<SubtitleTrack>();
        public SubtitleTrack CurrentSubtitle { get; private set; }
        public bool SubtitlesBurntIn => stream?.SubtitlesBurntIn ?? false;
        /// <summary>The subtitle text showing now, or null.</summary>
        public string SubtitleText { get; private set; }
        /// <summary>Average colour of the picture (dark when nothing plays), for light the screen casts on the room.</summary>
        public Color AverageColor { get; private set; }

        /// <summary>Fill the screen by cropping, instead of fitting the whole picture with black bars.</summary>
        public bool Fill
        {
            get => fill;
            set
            {
                fill = value;
                Changed?.Invoke();
            }
        }

        bool fill;

        public float Volume
        {
            get => audioSource.volume;
            set
            {
                audioSource.volume = Mathf.Clamp01(value);
                Changed?.Invoke();
            }
        }

        /// <summary>Creates the video player; <paramref name="audio"/> is where the sound comes out.</summary>
        public void Setup(AudioSource audio, int width = 1920, int height = 1080)
        {
            audioSource = audio;
            audioSource.playOnAwake = false;
            audioSource.dopplerLevel = 0;

            // Mipmapped: the smallest mip is the picture's average colour, and far-away screens don't shimmer.
            texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "Screen", useMipMap = true, autoGenerateMips = true, anisoLevel = 4, wrapMode = TextureWrapMode.Clamp,
            };
            texture.Create();
            Clear(Color.black);

            video = gameObject.AddComponent<VideoPlayer>();
            video.playOnAwake = false;
            video.source = VideoSource.Url;
            video.renderMode = VideoRenderMode.RenderTexture;
            video.targetTexture = texture;
            video.aspectRatio = VideoAspectRatio.Stretch; // the screen's shader letterboxes by VideoAspect
            video.skipOnDrop = true;
            video.audioOutputMode = VideoAudioOutputMode.AudioSource;
            video.controlledAudioTrackCount = 1;
            video.EnableAudioTrack(0, true);
            video.SetTargetAudioSource(0, audioSource);
            video.prepareCompleted += OnPrepared;
            video.errorReceived += OnError;
            video.loopPointReached += OnFinished;
        }

        void OnDestroy()
        {
            Stop();
            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
            }
        }

        /// <summary>Plays a title: a movie where it was left off, a show from its next episode.</summary>
        public async void Play(LibraryItem title)
        {
            Stop();
            item = title;
            var ct = NewSession();
            SetState(State.Loading, "Loading...");
            try
            {
                var services = StoreServices.Instance;
                await services.WhenReady();
                var plan = await services.Playback.PlanAsync(title, ct);
                if (title.Kind == MediaKind.Show)
                {
                    episodes = await services.Library.GetEpisodesAsync(title, ct);
                    episodeIndex = episodes.ToList().FindIndex(e => e.Key == plan.Key);
                }
                if (ct.IsCancellationRequested) return;
                await Open(plan.Key, plan.ResumeMs, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        public void TogglePause()
        {
            if (Current == State.Playing) Pause();
            else if (Current == State.Paused) Resume();
        }

        public void Pause()
        {
            if (Current != State.Playing) return;
            video.Pause();
            SetState(State.Paused);
            Report("paused");
        }

        public void Resume()
        {
            if (Current != State.Paused) return;
            video.Play();
            SetState(State.Playing);
            Report("playing");
        }

        public void SeekBy(double seconds) => SeekTo((pendingSeek >= 0 ? pendingSeek : PositionSeconds) + seconds);

        public void SeekTo(double seconds)
        {
            if (stream == null || Current is State.Idle or State.Failed) return;
            seconds = Math.Max(0, Math.Min(seconds, DurationSeconds - 1));
            if (stream.Seekable)
            {
                video.time = seconds;
                positionMs = (long)(seconds * 1000);
                Changed?.Invoke();
                return;
            }
            // A converted stream restarts at the new place; wait for the buttons to settle first.
            pendingSeek = seconds;
            seekDue = Time.unscaledTime + SeekSettleSeconds;
            Changed?.Invoke();
        }

        public void StartOver() => SeekTo(0);

        public void NextEpisode() => PlayEpisode(episodeIndex + 1);
        public void PreviousEpisode() => PlayEpisode(episodeIndex - 1);

        async void PlayEpisode(int index)
        {
            if (index < 0 || index >= episodes.Count) return;
            CloseStream("stopped");
            episodeIndex = index;
            var ct = NewSession();
            SetState(State.Loading, "Loading...");
            try
            {
                await Open(episodes[index].Key, 0, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        /// <summary>Steps through the subtitle files, then off.</summary>
        public async void CycleSubtitles()
        {
            var tracks = SubtitleTracks;
            if (tracks.Count == 0 || SubtitlesBurntIn) return;
            var next = CurrentSubtitle == null ? 0 : tracks.ToList().IndexOf(CurrentSubtitle) + 1;
            await ShowSubtitles(next < tracks.Count ? tracks[next] : null, session?.Token ?? CancellationToken.None);
        }

        /// <summary>Stops and forgets the title.</summary>
        public void Stop()
        {
            session?.Cancel();
            session = null;
            CloseStream("stopped");
            item = null;
            episodes = Array.Empty<EpisodeRef>();
            episodeIndex = -1;
            if (texture != null) Clear(Color.black);
            SetState(State.Idle);
        }

        void Update()
        {
            if (pendingSeek >= 0 && Time.unscaledTime >= seekDue && stream != null)
            {
                var target = pendingSeek;
                pendingSeek = -1;
                Reopen((long)(target * 1000));
            }

            if (Current is State.Playing or State.Paused && video.isPrepared)
            {
                positionMs = (long)(PositionSeconds * 1000);
                if (Time.unscaledTime >= nextReport) Report(Current == State.Playing ? "playing" : "paused");
            }

            var text = subtitles?.TextAt(PositionSeconds);
            if (text != SubtitleText)
            {
                SubtitleText = text;
                Changed?.Invoke();
            }

            SampleColor();
        }

        async Task Open(string key, long offsetMs, CancellationToken ct)
        {
            library = StoreServices.Instance.Library;
            var previousSubtitle = CurrentSubtitle;
            var opened = await library.OpenStreamAsync(key, offsetMs, ct);
            if (ct.IsCancellationRequested)
            {
                library.CloseStream(opened);
                return;
            }
            if (opened == null)
            {
                SetState(State.Failed, "No films in offline mode");
                return;
            }

            stream = opened;
            positionMs = offsetMs;
            startAtMs = stream.Seekable ? offsetMs : 0;
            video.url = stream.Url;
            video.Prepare();

            // Keep the viewer's subtitle choice across seeks; otherwise start with the library's.
            var wanted = previousSubtitle != null
                ? stream.Subtitles.FirstOrDefault(t => t.Id == previousSubtitle.Id)
                : stream.DefaultSubtitle;
            if (wanted?.Id != CurrentSubtitle?.Id || subtitles == null) await ShowSubtitles(wanted, ct);
        }

        /// <summary>Opens the same title again at <paramref name="atMs"/> (seeking a converted stream, or recovering).</summary>
        async void Reopen(long atMs)
        {
            var key = stream.Key;
            CloseStream(null);
            positionMs = atMs;
            var ct = NewSession();
            SetState(State.Loading, "Loading...");
            try
            {
                await Open(key, atMs, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        async Task ShowSubtitles(SubtitleTrack track, CancellationToken ct)
        {
            CurrentSubtitle = track;
            subtitles = null;
            SubtitleText = null;
            Changed?.Invoke();
            if (track == null) return;
            try
            {
                var loaded = await StoreServices.Instance.Library.LoadSubtitlesAsync(track, ct);
                if (CurrentSubtitle == track) subtitles = loaded;
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Debug.LogWarning($"[Screen] Subtitles {track.Label}: {e.Message}");
            }
        }

        void OnPrepared(VideoPlayer source)
        {
            if (stream == null) return;
            var pixelAspect = source.pixelAspectRatioDenominator > 0
                ? source.pixelAspectRatioNumerator / (float)source.pixelAspectRatioDenominator : 1f;
            if (source.height > 0) VideoAspect = source.width * pixelAspect / source.height;
            if (startAtMs > 0) source.time = startAtMs / 1000.0;
            startAtMs = 0;
            source.Play();
            recoveries = 0;
            SetState(State.Playing, null);
            Report("playing");
        }

        void OnError(VideoPlayer source, string message)
        {
            // The URL is a local relay address; nothing secret in the message.
            Debug.LogWarning($"[Screen] {Title}: {message}");
            if (stream != null && recoveries++ < MaxRecoveries && positionMs > 0)
            {
                // A converted stream can drop after a long pause; carry on from where it was.
                Reopen(positionMs);
                return;
            }
            SetState(State.Failed, "Can't play this one");
        }

        void OnFinished(VideoPlayer source)
        {
            positionMs = stream?.DurationMs ?? positionMs;
            if (HasNextEpisode)
            {
                NextEpisode();
                return;
            }
            Stop();
        }

        void Fail(Exception e)
        {
            Debug.LogWarning($"[Screen] {Title}: {e.Message}");
            CloseStream(null);
            SetState(State.Failed, "Can't play this one");
        }

        CancellationToken NewSession()
        {
            session?.Cancel();
            session = new CancellationTokenSource();
            return session.Token;
        }

        void CloseStream(string finalState)
        {
            if (stream == null) return;
            if (finalState != null) Report(finalState);
            if (video != null)
            {
                video.Stop();
                video.url = "";
            }
            library?.CloseStream(stream);
            stream = null;
            pendingSeek = -1;
        }

        void Report(string state)
        {
            nextReport = Time.unscaledTime + ProgressReportSeconds;
            if (stream == null) return;
            library?.ReportProgressAsync(stream, state, positionMs, CancellationToken.None)
                .ContinueWith(t => Debug.LogWarning($"[Screen] Progress report: {t.Exception?.GetBaseException().Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);
        }

        void SetState(State state, string status = null)
        {
            Current = state;
            Status = status;
            Changed?.Invoke();
        }

        void Clear(Color color)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(false, true, color);
            RenderTexture.active = previous;
        }

        /// <summary>Reads the 1x1 mip of the picture back from the GPU a few times a second.</summary>
        void SampleColor()
        {
            if (Current != State.Playing && Current != State.Paused)
            {
                AverageColor = Color.Lerp(AverageColor, Color.black, Time.deltaTime * 3f);
                return;
            }
            if (readbackPending || Time.unscaledTime < nextColorSample || !SystemInfo.supportsAsyncGPUReadback) return;
            nextColorSample = Time.unscaledTime + ColorSampleSeconds;
            readbackPending = true;
            AsyncGPUReadback.Request(texture, texture.mipmapCount - 1, TextureFormat.RGBA32, request =>
            {
                readbackPending = false;
                if (request.hasError || this == null) return;
                NativeArray<Color32> pixels = request.GetData<Color32>();
                if (pixels.Length > 0) AverageColor = Color.Lerp(AverageColor, pixels[0], 0.6f);
            });
        }
    }
}
