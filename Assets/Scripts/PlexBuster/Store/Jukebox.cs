using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace PlexBuster.Store
{
    /// <summary>
    /// The lobby jukebox: plays the songs in <see cref="MusicFolder"/> in shuffled order, with PREV / PLAY / NEXT
    /// buttons on the cabinet, and picks up songs added to the folder at the start of each pass through it.
    /// The sound comes from the cabinet and fades out across the lobby, so the departments and rooms (built far
    /// away) are quiet unless <see cref="spatialBlend"/> is lowered. The cabinet is built at runtime, front facing +Z.
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(AudioSource))]
    public class Jukebox : MonoBehaviour
    {
        const float RescanSeconds = 5f;
        const float HueCycleSeconds = 20f;
        const float NeonIntensity = 2f;

        [SerializeField] StoreTheme theme;
        [SerializeField, Range(0, 1)] float volume = 0.6f;
        [SerializeField, Range(0, 1), Tooltip("1: heard around the jukebox only. 0: heard everywhere, departments and rooms included.")]
        float spatialBlend = 1f;
        [SerializeField, Min(1), Tooltip("Metres from the jukebox at which the music has faded out completely.")]
        float audibleDistance = 18f;

        static readonly System.Random Rng = new();

        readonly List<MusicTrack> tracks = new();
        readonly float[] samples = new float[256];
        AudioSource source;
        JukeboxCabinet cabinet;
        int index = -1;
        int loadVersion;
        int failures;
        int shownSecond = -1;
        bool loading, rescanning, paused;
        float startedAt, nextScan, glow;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.dopplerLevel = 0;
            source.spread = 60;
            source.rolloffMode = AudioRolloffMode.Custom;
            // Full volume at arm's length, about half across the lobby, silent at audibleDistance (the curve's
            // x axis is distance / maxDistance).
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, new AnimationCurve(
                new Keyframe(0, 1), new Keyframe(0.15f, 1), new Keyframe(0.5f, 0.45f), new Keyframe(1, 0)));
            ApplySettings();
        }

        void OnValidate()
        {
            if (source != null) ApplySettings();
        }

        void ApplySettings()
        {
            source.volume = volume;
            source.spatialBlend = spatialBlend;
            source.maxDistance = audibleDistance;
        }

        void Start()
        {
            if (theme == null)
            {
                Debug.LogError("[Jukebox] No StoreTheme assigned.", this);
                enabled = false;
                return;
            }
            Build();
        }

        /// <summary>Builds the cabinet and starts on a freshly shuffled song list.</summary>
        void Build()
        {
            // After a script reload in Play mode the old cabinet is still in the scene, but nothing points at it.
            var stale = transform.Find("Cabinet");
            if (stale != null) Destroy(stale.gameObject);

            tracks.Clear();
            index = -1;
            loading = rescanning = paused = false;
            cabinet = JukeboxCabinet.Build(transform, theme, Previous, TogglePause, Next);
            nextScan = Time.unscaledTime + RescanSeconds;
            Step(+1);
        }

        void OnDestroy()
        {
            cabinet?.Release();
            if (source != null && source.clip != null) Destroy(source.clip);
        }

        void Update()
        {
            if (cabinet == null)
            {
                // A script reload in Play mode keeps the GameObjects but drops plain C# state (the cabinet, the
                // song list, pending loads); rebuild rather than fail every frame.
                source = GetComponent<AudioSource>();
                Build();
                return;
            }

            if (!loading && !rescanning)
            {
                if (tracks.Count == 0)
                {
                    // Waiting for songs: look again every few seconds, and start as soon as some turn up.
                    if (Time.unscaledTime >= nextScan)
                    {
                        nextScan = Time.unscaledTime + RescanSeconds;
                        Step(+1);
                    }
                }
                else if (source.clip != null && !paused && !source.isPlaying && Time.unscaledTime - startedAt > 1f)
                {
                    Step(+1); // song over
                }
            }

            if (source.clip != null && !loading) ShowTime();
            AnimateNeon();
        }

        public void Next() => Step(+1);
        public void Previous() => Step(-1);

        public void TogglePause()
        {
            if (source.clip == null)
            {
                Step(+1);
                return;
            }
            paused = !paused;
            if (paused) source.Pause();
            else source.UnPause();
            WorldUI.SetText(cabinet.PlayPause, paused ? "PLAY" : "PAUSE");
        }

        async void Step(int direction)
        {
            if (rescanning) return;
            var target = index + direction;
            if (tracks.Count == 0 || target >= tracks.Count)
            {
                // A new pass through the folder: pick up songs added since, in a fresh order.
                rescanning = true;
                try
                {
                    await Rescan(index >= 0 && index < tracks.Count ? tracks[index].Path : null);
                }
                finally
                {
                    rescanning = false;
                }
                if (this == null) return;
                if (tracks.Count == 0)
                {
                    StopPlayback();
                    ShowMessage("No music yet", "Add MP3, OGG or WAV files to", MusicFolder.Location);
                    return;
                }
                target = 0;
            }
            Play(target < 0 ? tracks.Count - 1 : target);
        }

        async Task Rescan(string avoidFirst)
        {
            var folder = MusicFolder.Location;
            var found = await Task.Run(() => MusicFolder.Scan(folder));
            for (var i = found.Count - 1; i > 0; i--)
            {
                var j = Rng.Next(i + 1);
                (found[i], found[j]) = (found[j], found[i]);
            }
            // Don't play the song that just ended again straight away.
            if (found.Count > 1 && found[0].Path == avoidFirst) (found[0], found[^1]) = (found[^1], found[0]);

            tracks.Clear();
            tracks.AddRange(found);
            index = -1;
            if (found.Count > 0)
                Debug.Log($"[Jukebox] Shuffled {found.Count} songs from {folder}: " +
                          string.Join(", ", found.ConvertAll(t => t.Title)));
        }

        async void Play(int i)
        {
            index = i;
            var track = tracks[i];
            var version = ++loadVersion;
            loading = true;
            StopPlayback();
            ShowMessage(track.Title, track.Artist, Shuffling, "LOADING…");

            AudioClip clip;
            try
            {
                clip = await LoadAsync(track, destroyCancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (this == null || version != loadVersion)
            {
                // Destroyed, or another song was picked while this one loaded.
                if (clip != null) Destroy(clip);
                return;
            }
            loading = false;

            if (clip == null)
            {
                // Skip files that won't play, but give up after a whole lap of them.
                if (++failures < tracks.Count) Step(+1);
                else
                {
                    failures = 0;
                    ShowMessage("Can't play these songs", "Use MP3, OGG or WAV files in", MusicFolder.Location);
                }
                return;
            }

            failures = 0;
            paused = false;
            source.clip = clip;
            source.Play();
            startedAt = Time.unscaledTime;
            Heading("NOW PLAYING");
            WorldUI.SetText(cabinet.PlayPause, "PAUSE");
        }

        void StopPlayback()
        {
            source.Stop();
            if (source.clip != null) Destroy(source.clip);
            source.clip = null;
            WorldUI.SetText(cabinet.PlayPause, "PLAY");
        }

        static async Task<AudioClip> LoadAsync(MusicTrack track, CancellationToken ct)
        {
            // Kept compressed in memory and decoded as it plays, rather than unpacked up front (a hitch and tens
            // of MB per song); anything that won't load that way gets a second, plain attempt.
            foreach (var compressed in new[] { true, false })
            {
                using var request = UnityWebRequestMultimedia.GetAudioClip(MusicFolder.FileUrl(track.Path), track.Type);
                ((DownloadHandlerAudioClip)request.downloadHandler).compressed = compressed;
                using (ct.Register(request.Abort))
                    await request.SendWebRequest();
                ct.ThrowIfCancellationRequested();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[Jukebox] Can't load {track.Path}: {request.error}");
                    return null;
                }
                var clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip != null && clip.loadState != AudioDataLoadState.Failed)
                {
                    clip.name = track.Title;
                    return clip;
                }
                if (clip != null) Destroy(clip);
            }
            Debug.LogWarning($"[Jukebox] Can't decode {track.Path}");
            return null;
        }

        void ShowMessage(string title, string artist, string info, string heading = "JUKEBOX")
        {
            Heading(heading);
            cabinet.Title.text = title;
            cabinet.Artist.text = artist;
            cabinet.Info.text = info;
            shownSecond = -1;
        }

        void Heading(string text) => cabinet.Heading.text = text;

        void ShowTime()
        {
            var second = Mathf.FloorToInt(source.time);
            if (second == shownSecond) return;
            shownSecond = second;
            cabinet.Info.text = $"{Clock(source.time)} / {Clock(source.clip.length)}   ·   {Shuffling}";
        }

        // Not "3 of 8": a position in the shuffled order reads as if the songs played in folder order.
        string Shuffling => $"SHUFFLE  ·  {tracks.Count} SONGS";

        static string Clock(float seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";

        /// <summary>Neon slowly cycles through the colours and pulses with the music.</summary>
        void AnimateNeon()
        {
            var level = 0f;
            if (source.isPlaying)
            {
                source.GetOutputData(samples, 0);
                var sum = 0f;
                foreach (var s in samples) sum += s * s;
                level = Mathf.Sqrt(sum / samples.Length);
            }
            glow = Mathf.Lerp(glow, level, 1 - Mathf.Exp(-12 * Time.deltaTime));
            cabinet.SetNeon(Time.time / HueCycleSeconds, NeonIntensity * (1 + glow * 4));
        }

        void OnDrawGizmos()
        {
            // The cabinet only exists in play mode; show its footprint for placing it.
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.35f, 0.8f);
            Gizmos.DrawWireCube(new Vector3(0, JukeboxCabinet.Height / 2, 0),
                new Vector3(JukeboxCabinet.Width, JukeboxCabinet.Height, JukeboxCabinet.Depth));
            Gizmos.DrawLine(new Vector3(0, 0.02f, JukeboxCabinet.Depth / 2), new Vector3(0, 0.02f, JukeboxCabinet.Depth / 2 + 0.5f));
        }
    }
}
