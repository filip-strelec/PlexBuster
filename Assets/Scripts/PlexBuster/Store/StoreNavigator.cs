using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PlexBuster.Data;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace PlexBuster.Store
{
    /// <summary>
    /// Moves the player between the lobby and generated places: fade out, build the place, teleport in,
    /// fade in. Places stack (lobby → department hall → room); walking out of one returns the player to the
    /// door they came through and discards it, while the places below stay built. Posters stay cached,
    /// so revisiting is quick.
    /// </summary>
    public class StoreNavigator : MonoBehaviour
    {
        [SerializeField] StoreTheme theme;
        [SerializeField, Tooltip("Where the first level of generated places is built, well away from the lobby.")]
        Vector3 placeOrigin = new(1000, 0, 0);
        [SerializeField, Tooltip("Offset between levels, so a room never overlaps the hall it was opened from.")]
        Vector3 levelOffset = new(1000, 0, 0);
        [SerializeField] float fadeSeconds = 0.25f;

        readonly List<(IStorePlace Place, Pose ReturnPose)> stack = new();
        bool busy;
        ScreenFader fader;
        TeleportationProvider teleporter;
        XROrigin origin;

        public static StoreNavigator Instance { get; private set; }
        public StoreTheme Theme => theme;
        public IStorePlace CurrentPlace => stack.Count > 0 ? stack[^1].Place : null;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            teleporter = FindAnyObjectByType<TeleportationProvider>();
            origin = FindAnyObjectByType<XROrigin>();
            if (Camera.main != null) fader = ScreenFader.Create(Camera.main, theme.fadeMaterial);
        }

        /// <summary>A department: a hall with a door per genre in the section, and an "all titles" door at the end.</summary>
        public void EnterSection(LibrarySection section, Pose returnPose) => Enter(async (services, position) =>
        {
            var genres = await services.Library.GetFilterValuesAsync(FilterType.Genre, section.Id, services.LifetimeToken);
            var doors = genres.Select(genre => new DoorHall.Door(genre.Title,
                pose => EnterRoom($"{genre.Title} · {section.Title}", LibraryQuery.For(genre, section.Id), pose))).ToList();
            var all = new DoorHall.Door($"All {section.Title}", pose => EnterRoom(section.Title, LibraryQuery.AllOf(section.Id), pose));
            Debug.Log($"[Store] Entering {section}: {doors.Count} genre doors");
            var hall = DoorHall.Build($"Hall: {section.Title}", theme, doors, all, position, Quaternion.identity, withExit: true);

            // "Browse by" board on the right wall, just past the entrance, before the first doors.
            BrowseKiosk.Create(hall.transform, new Vector3(DoorHall.HalfWidth - 0.03f, 1.45f, 1.25f), Vector3.left, section);
            return hall;
        }, returnPose);

        /// <summary>The TV room: a living room with a giant CRT and a VCR to put a tape in.</summary>
        public void EnterTvRoom(Pose returnPose) =>
            Enter((services, position) => TvRoom.Build(theme, services.Posters, position), returnPose);

        /// <summary>The cinema: stadium seating and a 12 m screen.</summary>
        public void EnterCinema(Pose returnPose) =>
            Enter((services, position) => CinemaRoom.Build(theme, services.Posters, position), returnPose);

        /// <summary>A room holding the result of <paramref name="query"/>.</summary>
        public void EnterRoom(string title, LibraryQuery query, Pose returnPose) => Enter(async (services, position) =>
        {
            var items = await services.Library.QueryAsync(query, services.LifetimeToken);
            Debug.Log($"[Store] Entering {title}: {query} -> {items.Count} items");
            return GeneratedRoom.Build(title, items, query.Sort, theme, services.Posters, position);
        }, returnPose);

        async void Enter(Func<StoreServices, Vector3, Task<IStorePlace>> build, Pose returnPose)
        {
            if (busy) return;
            busy = true;
            try
            {
                await Fade(1);
                var services = StoreServices.Instance;
                await services.WhenReady();

                var place = await build(services, placeOrigin + levelOffset * stack.Count);
                place.Exit.Entered += Back;
                stack.Add((place, returnPose));
                await TeleportTo(place.EntryPose);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                await Fade(0);
                busy = false;
            }
        }

        /// <summary>Leaves the current place, returning to the door it was entered from.</summary>
        public async void Back()
        {
            if (busy || stack.Count == 0) return;
            busy = true;
            try
            {
                var (place, returnPose) = stack[^1];
                await Fade(1);
                await TeleportTo(returnPose);
                stack.RemoveAt(stack.Count - 1);
                place.Exit.Entered -= Back;
                Destroy(place.gameObject);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                await Fade(0);
                busy = false;
            }
        }

        async Task TeleportTo(Pose pose)
        {
            if (teleporter != null)
            {
                teleporter.QueueTeleportRequest(new TeleportRequest
                {
                    destinationPosition = pose.position,
                    destinationRotation = pose.rotation,
                    matchOrientation = MatchOrientation.TargetUpAndForward,
                });
            }
            else if (origin != null)
            {
                origin.MoveCameraToWorldLocation(pose.position + Vector3.up * origin.CameraInOriginSpaceHeight);
                origin.MatchOriginUpCameraForward(Vector3.up, pose.forward);
            }

            // The teleport is applied by the locomotion system on a later frame.
            await Awaitable.NextFrameAsync();
            await Awaitable.NextFrameAsync();
        }

        Task Fade(float target) => fader != null ? fader.FadeTo(target, fadeSeconds) : Task.CompletedTask;
    }
}
