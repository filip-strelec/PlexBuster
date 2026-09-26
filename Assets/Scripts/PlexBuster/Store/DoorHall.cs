using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PlexBuster.Store
{
    /// <summary>
    /// A corridor of labelled doors built at runtime. Side doors alternate left and right in the given
    /// order; an optional end door sits in the far wall. Walking up to a door calls its action with the pose
    /// to return the player to later (in front of that door, facing the corridor).
    /// Hall space: the corridor starts at the origin and runs along +Z.
    /// </summary>
    public class DoorHall : MonoBehaviour, IStorePlace
    {
        public readonly struct Door
        {
            public readonly string Label;
            public readonly Action<Pose> Open;

            public Door(string label, Action<Pose> open)
            {
                Label = label;
                Open = open;
            }
        }

        const float WallThickness = StoreShell.WallThickness;
        const float Width = 4f;
        public const float HalfWidth = Width / 2;
        const float DoorWidth = 1.3f;
        const float DoorHeight = 2.3f;
        const float DoorSpacing = 2.4f;
        const float FirstDoorZ = 2.7f;

        public Pose EntryPose => new(transform.TransformPoint(0, 0, 0.9f), transform.rotation);

        /// <summary>The way back out; null for a hall that opens straight onto the lobby.</summary>
        public DoorTrigger Exit { get; private set; }

        /// <param name="withExit">Close the start of the corridor with an EXIT door (for halls reached by teleport).</param>
        /// <param name="entranceSign">Optional sign over the open start, readable from outside (lobby halls).</param>
        /// <param name="title">Optional title on the far wall; only shown without an end door, whose label takes that spot.</param>
        public static DoorHall Build(string name, StoreTheme theme, IReadOnlyList<Door> sideDoors, Door? endDoor,
            Vector3 position, Quaternion rotation, bool withExit, string entranceSign = null, string title = null)
        {
            var hall = new GameObject(name).AddComponent<DoorHall>();
            hall.transform.SetPositionAndRotation(position, rotation);

            var perSide = Mathf.CeilToInt(sideDoors.Count / 2f);
            var length = Mathf.Max(6f, FirstDoorZ + perSide * DoorSpacing + 1.5f);
            hall.BuildShell(theme, length, withExit, entranceSign, endDoor.HasValue ? null : title);

            for (var i = 0; i < sideDoors.Count; i++)
            {
                var side = i % 2 == 0 ? -1 : 1;
                var z = FirstDoorZ + (i / 2) * DoorSpacing;
                var facing = Quaternion.Euler(0, side < 0 ? 90 : -90, 0); // doors face into the corridor
                hall.BuildDoor(theme, sideDoors[i], new Vector3(side * Width / 2, 0, z), facing, bladeSign: true);
            }
            if (endDoor.HasValue)
                hall.BuildDoor(theme, endDoor.Value, new Vector3(0, 0, length), Quaternion.Euler(0, 180, 0), bladeSign: false);

            return hall;
        }

        void BuildShell(StoreTheme theme, float length, bool withExit, string entranceSign, string title)
        {
            var h = theme.wallHeight;
            var shell = new GameObject("Shell").transform;
            shell.SetParent(transform, false);

            StoreShell.Floor(shell, new Vector3(0, -0.05f, length / 2), new Vector3(Width + 2 * WallThickness, 0.1f, length), theme.floorMaterial, theme.floorTileMeters);
            StoreShell.Ceiling(shell, new Vector3(0, h + 0.05f, length / 2), new Vector3(Width + 2 * WallThickness, 0.1f, length), theme);
            Signage.Box(shell, "Wall_End", new Vector3(0, h / 2, length + WallThickness / 2), new Vector3(Width + 2 * WallThickness, h, WallThickness), theme.wallMaterial);
            StoreShell.WallBand(shell, theme, new Vector3(-Width / 2, 0, length), new Vector3(Width / 2, 0, length), Vector3.back, h);
            for (var side = -1; side <= 1; side += 2)
            {
                Signage.Box(shell, "Wall_Side", new Vector3(side * (Width + WallThickness) / 2, h / 2, length / 2), new Vector3(WallThickness, h, length), theme.wallMaterial);
                StoreShell.WallBand(shell, theme, new Vector3(side * Width / 2, 0, 0), new Vector3(side * Width / 2, 0, length), Vector3.left * side, h);
            }
            if (withExit) Exit = StoreShell.FrontWallWithExit(shell, theme, Width + 2 * WallThickness, h);

            for (var z = 2f; z < length; z += theme.lightSpacing)
                StoreShell.CeilingLight(shell, theme, new Vector3(0, h, z));

            if (!string.IsNullOrEmpty(entranceSign))
            {
                var sign = Signage.CreateText(transform, "Sign_Entrance", new Vector3(0, h - 0.4f, -0.25f), Quaternion.identity,
                    3f, theme.signColor, new Vector2(Width, 0.6f), material: theme.signTextMaterial);
                sign.text = entranceSign.ToUpperInvariant();
                sign.fontStyle = FontStyles.Bold;
            }
            if (!string.IsNullOrEmpty(title))
            {
                var sign = Signage.CreateText(transform, "Sign_Title", new Vector3(0, h - 0.3f, length - 0.02f), Quaternion.identity,
                    3f, theme.signColor, new Vector2(Width - 0.3f, 0.5f), material: theme.signTextMaterial);
                sign.text = title.ToUpperInvariant();
                sign.fontStyle = FontStyles.Bold;
            }
        }

        void BuildDoor(StoreTheme theme, Door door, Vector3 position, Quaternion facing, bool bladeSign)
        {
            // Door space: origin on the wall surface at floor level, +Z pointing into the corridor.
            var root = new GameObject($"Door: {door.Label}").transform;
            root.SetParent(transform, false);
            root.SetLocalPositionAndRotation(position, facing);

            Signage.Box(root, "Doorway", new Vector3(0, DoorHeight / 2, 0.005f), new Vector3(DoorWidth, DoorHeight, 0.01f), theme.beadCurtainMaterial != null ? theme.beadCurtainMaterial : theme.doorwayMaterial);
            Signage.Box(root, "Frame_Top", new Vector3(0, DoorHeight + 0.05f, 0.03f), new Vector3(DoorWidth + 0.2f, 0.1f, 0.06f), theme.doorFrameMaterial);
            for (var side = -1; side <= 1; side += 2)
                Signage.Box(root, "Frame_Side", new Vector3(side * (DoorWidth + 0.1f) / 2, DoorHeight / 2, 0.03f), new Vector3(0.1f, DoorHeight, 0.06f), theme.doorFrameMaterial);

            var label = Signage.CreateText(root, "Label", new Vector3(0, DoorHeight + 0.32f, 0.02f), Quaternion.Euler(0, 180, 0),
                1.6f, theme.signColor, new Vector2(DoorSpacing - 0.2f, 0.4f), material: theme.signTextMaterial);
            label.text = door.Label.ToUpperInvariant();
            if (bladeSign)
                Signage.CreateBladeSign(root, door.Label.ToUpperInvariant(), new Vector3(DoorWidth / 2 + 0.25f, 2.25f, 0),
                    Quaternion.identity, theme.doorFrameMaterial, theme.signColor, theme.signTextMaterial);

            var trigger = new GameObject("Trigger").AddComponent<DoorTrigger>();
            trigger.transform.SetParent(root, false);
            trigger.transform.localPosition = new Vector3(0, DoorHeight / 2, 0.3f);
            trigger.Size = new Vector3(DoorWidth, DoorHeight, 0.6f);

            var returnPose = new Pose(root.TransformPoint(new Vector3(0, 0, 1.3f)), root.rotation);
            var open = door.Open;
            trigger.Entered += () => open(returnPose);
        }
    }
}
