using System;
using System.Threading.Tasks;
using PlexBuster.Data;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace PlexBuster.Store
{
    /// <summary>
    /// Moves the player between the lobby and generated rooms: fade out, build the room for a query,
    /// teleport in, fade in. Leaving through the room's door reverses it and discards the room.
    /// Posters stay cached, so going back into a room is quick.
    /// </summary>
    public class StoreNavigator : MonoBehaviour
    {
        [SerializeField] StoreTheme theme;
        [SerializeField, Tooltip("Where generated rooms are built, well away from the lobby.")]
        Vector3 roomOrigin = new(1000, 0, 0);
        [SerializeField] float fadeSeconds = 0.25f;

        GeneratedRoom room;
        Pose returnPose;
        bool busy;
        ScreenFader fader;
        TeleportationProvider teleporter;
        XROrigin origin;

        public static StoreNavigator Instance { get; private set; }
        public StoreTheme Theme => theme;
        public GeneratedRoom CurrentRoom => room;

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

        /// <summary>Builds a room for <paramref name="query"/> and moves the player into it.</summary>
        /// <param name="exitPose">Where to put the player when they leave the room.</param>
        public async void EnterRoom(string title, LibraryQuery query, Pose exitPose)
        {
            if (busy) return;
            busy = true;
            try
            {
                await Fade(1);
                var services = StoreServices.Instance;
                await services.WhenReady();
                var items = await services.Library.QueryAsync(query, services.LifetimeToken);
                Debug.Log($"[Store] Entering {title}: {query} -> {items.Count} items");

                DiscardRoom();
                room = GeneratedRoom.Build(title, items, query.Sort, theme, services.Posters, roomOrigin);
                room.Exit.Entered += ReturnToLobby;
                returnPose = exitPose;
                await TeleportTo(room.EntryPose);
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

        public async void ReturnToLobby()
        {
            if (busy || room == null) return;
            busy = true;
            try
            {
                await Fade(1);
                await TeleportTo(returnPose);
                DiscardRoom();
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

        void DiscardRoom()
        {
            if (room == null) return;
            room.Exit.Entered -= ReturnToLobby;
            Destroy(room.gameObject);
            room = null;
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
