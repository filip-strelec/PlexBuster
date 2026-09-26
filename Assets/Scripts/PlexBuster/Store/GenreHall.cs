using System;
using System.Collections.Generic;
using PlexBuster.Data;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace PlexBuster.Store
{
    /// <summary>
    /// A corridor with one door per value of a filter (every genre, by default), alternating left and
    /// right in alphabetical order. Walking into a door opens that value's room.
    /// Origin is the middle of the corridor's open end on the floor; the corridor runs along +Z.
    /// </summary>
    public class GenreHall : MonoBehaviour
    {
        const float WallThickness = 0.2f;
        const float DoorWidth = 1.3f;
        const float DoorHeight = 2.3f;

        [SerializeField] StoreTheme theme;
        [SerializeField] FilterType filter = FilterType.Genre;
        [SerializeField] SortOrder roomSort = SortOrder.Title;
        [SerializeField] string entranceSign = "Genres";
        [SerializeField] float width = 4f;
        [SerializeField] float doorSpacing = 2.4f;

        async void Start()
        {
            var services = StoreServices.Instance;
            await services.WhenReady();
            try
            {
                var values = await services.Library.GetFilterValuesAsync(filter, services.LifetimeToken);
                if (this != null) Build(values);
            }
            catch (OperationCanceledException) { }
        }

        void Build(IReadOnlyList<FilterValue> values)
        {
            var perSide = Mathf.CeilToInt(values.Count / 2f);
            var length = Mathf.Max(4f, perSide * doorSpacing + 2f);
            var h = theme.wallHeight;

            var shell = new GameObject("Shell").transform;
            shell.SetParent(transform, false);
            var floor = Signage.Box(shell, "Floor", new Vector3(0, -0.05f, length / 2), new Vector3(width + 2 * WallThickness, 0.1f, length), theme.floorMaterial);
            floor.AddComponent<TeleportationArea>().interactionLayers = InteractionLayerMask.GetMask("Teleport");
            Signage.Box(shell, "Ceiling", new Vector3(0, h + 0.05f, length / 2), new Vector3(width + 2 * WallThickness, 0.1f, length), theme.ceilingMaterial);
            Signage.Box(shell, "Wall_End", new Vector3(0, h / 2, length + WallThickness / 2), new Vector3(width + 2 * WallThickness, h, WallThickness), theme.wallMaterial);
            for (var side = -1; side <= 1; side += 2)
                Signage.Box(shell, "Wall_Side", new Vector3(side * (width + WallThickness) / 2, h / 2, length / 2), new Vector3(WallThickness, h, length), theme.wallMaterial);

            for (var z = 2f; z < length; z += theme.lightSpacing)
            {
                var light = new GameObject("Light").AddComponent<Light>();
                light.transform.SetParent(shell, false);
                light.transform.localPosition = new Vector3(0, h - theme.lightDrop, z);
                light.type = LightType.Point;
                light.color = theme.lightColor;
                light.intensity = theme.lightIntensity;
                light.range = theme.lightRange;
                light.shadows = LightShadows.None;
            }

            // Sign over the corridor entrance, readable from the lobby.
            var sign = Signage.CreateText(transform, "Sign", new Vector3(0, h - 0.4f, -0.25f), Quaternion.identity,
                3f, theme.signColor, new Vector2(width, 0.6f));
            sign.text = entranceSign.ToUpperInvariant();
            sign.fontStyle = TMPro.FontStyles.Bold;

            for (var i = 0; i < values.Count; i++)
            {
                var side = i % 2 == 0 ? -1 : 1;              // alphabetical, alternating left / right
                var z = 1.5f + (i / 2) * doorSpacing + doorSpacing / 2;
                var facing = Quaternion.Euler(0, side < 0 ? 90 : -90, 0); // door faces into the corridor
                BuildDoor(values[i], new Vector3(side * width / 2, 0, z), facing);
            }
        }

        void BuildDoor(FilterValue value, Vector3 position, Quaternion facing)
        {
            // Door space: origin on the wall surface at floor level, +Z pointing into the corridor.
            var door = new GameObject($"Door: {value.Title}").transform;
            door.SetParent(transform, false);
            door.SetLocalPositionAndRotation(position, facing);

            Signage.Box(door, "Doorway", new Vector3(0, DoorHeight / 2, 0.005f), new Vector3(DoorWidth, DoorHeight, 0.01f), theme.doorwayMaterial);
            Signage.Box(door, "Frame_Top", new Vector3(0, DoorHeight + 0.05f, 0.03f), new Vector3(DoorWidth + 0.2f, 0.1f, 0.06f), theme.doorFrameMaterial);
            for (var side = -1; side <= 1; side += 2)
                Signage.Box(door, "Frame_Side", new Vector3(side * (DoorWidth + 0.1f) / 2, DoorHeight / 2, 0.03f), new Vector3(0.1f, DoorHeight, 0.06f), theme.doorFrameMaterial);

            var label = Signage.CreateText(door, "Label", new Vector3(0, DoorHeight + 0.32f, 0.02f), Quaternion.Euler(0, 180, 0),
                1.6f, theme.signColor, new Vector2(doorSpacing - 0.2f, 0.4f));
            label.text = value.Title.ToUpperInvariant();

            // A blade sign beside the door, readable while walking down the corridor.
            Signage.CreateBladeSign(door, value.Title.ToUpperInvariant(), new Vector3(DoorWidth / 2 + 0.25f, 2.25f, 0),
                Quaternion.identity, theme.doorFrameMaterial, theme.signColor);

            // Walking up to the doorway opens the room; leaving it puts you back here, facing the corridor.
            var trigger = new GameObject("Trigger").AddComponent<DoorTrigger>();
            trigger.transform.SetParent(door, false);
            trigger.transform.localPosition = new Vector3(0, DoorHeight / 2, 0.3f);
            trigger.Size = new Vector3(DoorWidth, DoorHeight, 0.6f);

            var exitPose = new Pose(door.TransformPoint(new Vector3(0, 0, 1.3f)), door.rotation);
            trigger.Entered += () => StoreNavigator.Instance.EnterRoom(value.Title, LibraryQuery.For(value, roomSort), exitPose);
        }
    }
}
