using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace PlexBuster.Store
{
    /// <summary>Building blocks shared by generated rooms and halls.</summary>
    public static class StoreShell
    {
        public const float WallThickness = 0.2f;
        public const float DoorWidth = 1.6f;
        public const float DoorHeight = 2.4f;
        const float VestibuleDepth = 1.2f;

        public const float BandBottom = 2.45f;

        public static GameObject Floor(Transform parent, Vector3 centre, Vector3 size, Material material, float metersPerTile = 0)
        {
            var floor = Signage.Box(parent, "Floor", centre, size, material);
            floor.AddComponent<TeleportationArea>().interactionLayers = InteractionLayerMask.GetMask("Teleport");
            if (metersPerTile > 0) TiledSurface.Apply(floor, metersPerTile);
            return floor;
        }

        public static GameObject Ceiling(Transform parent, Vector3 centre, Vector3 size, StoreTheme theme)
        {
            var ceiling = Signage.Box(parent, "Ceiling", centre, size, theme.ceilingMaterial);
            TiledSurface.Apply(ceiling, theme.ceilingTileMeters);
            return ceiling;
        }

        /// <summary>
        /// A coloured band along the top of a wall, from just above the doors to the ceiling, where the
        /// room's sign sits. <paramref name="start"/>/<paramref name="end"/> run along the wall face,
        /// <paramref name="outward"/> points from the wall into the room.
        /// </summary>
        public static void WallBand(Transform parent, StoreTheme theme, Vector3 start, Vector3 end, Vector3 outward, float height)
        {
            if (theme.wallBandMaterial == null) return;
            var along = end - start;
            var centre = (start + end) / 2 + outward * 0.005f + Vector3.up * (BandBottom + height) / 2;
            var size = Mathf.Abs(Vector3.Dot(outward, Vector3.right)) > 0.5f
                ? new Vector3(0.01f, height - BandBottom, along.magnitude)
                : new Vector3(along.magnitude, height - BandBottom, 0.01f);
            var band = Signage.Box(parent, "WallBand", centre, size, theme.wallBandMaterial);
            Object.Destroy(band.GetComponent<Collider>());
        }

        public static void CeilingLight(Transform parent, StoreTheme theme, Vector3 ceilingPoint, bool withPanel = true)
        {
            if (withPanel)
            {
                var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
                panel.name = "LightPanel";
                Object.Destroy(panel.GetComponent<Collider>());
                panel.transform.SetParent(parent, false);
                panel.transform.SetLocalPositionAndRotation(ceilingPoint + Vector3.down * 0.005f, Quaternion.Euler(-90, 0, 0));
                panel.transform.localScale = new Vector3(1.2f, 0.3f, 1);
                var renderer = panel.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = theme.lightPanelMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.transform.localPosition = ceilingPoint + Vector3.down * theme.lightDrop;
            light.type = LightType.Point;
            light.color = theme.lightColor;
            light.intensity = theme.lightIntensity;
            light.range = theme.lightRange;
            light.shadows = LightShadows.None;
        }

        /// <summary>
        /// The front wall of a space that extends along +Z: a wall across X at z = 0 with a door at the origin,
        /// a dark vestibule behind it, and EXIT above the door. Returns the trigger that fires when the player
        /// steps into the vestibule.
        /// </summary>
        /// <param name="wall">The wall's material; the theme's by default.</param>
        /// <param name="band">Whether the wall gets the store's coloured band along the top.</param>
        public static DoorTrigger FrontWallWithExit(Transform parent, StoreTheme theme, float width, float height,
            Material wall = null, bool band = true)
        {
            wall ??= theme.wallMaterial;
            var segment = (width - DoorWidth) / 2;
            for (var side = -1; side <= 1; side += 2)
                Signage.Box(parent, "Wall_Front", new Vector3(side * (DoorWidth + segment) / 2, height / 2, -WallThickness / 2),
                    new Vector3(segment, height, WallThickness), wall);
            Signage.Box(parent, "Lintel", new Vector3(0, (DoorHeight + height) / 2, -WallThickness / 2),
                new Vector3(DoorWidth, height - DoorHeight, WallThickness), wall);

            if (band) WallBand(parent, theme, new Vector3(-width / 2, 0, 0), new Vector3(width / 2, 0, 0), Vector3.forward, height);

            var z = -WallThickness - VestibuleDepth / 2;
            var outer = DoorWidth + 2 * WallThickness;
            var vestibuleFloor = Floor(parent, new Vector3(0, -0.05f, z), new Vector3(outer, 0.1f, VestibuleDepth), theme.doorwayMaterial);
            vestibuleFloor.name = "Vestibule_Floor";
            Signage.Box(parent, "Vestibule_Ceiling", new Vector3(0, DoorHeight + 0.05f, z), new Vector3(outer, 0.1f, VestibuleDepth), theme.doorwayMaterial);
            Signage.Box(parent, "Vestibule_End", new Vector3(0, DoorHeight / 2, z - VestibuleDepth / 2), new Vector3(outer, DoorHeight, 0.1f), theme.beadCurtainMaterial != null ? theme.beadCurtainMaterial : theme.doorwayMaterial);
            for (var side = -1; side <= 1; side += 2)
                Signage.Box(parent, "Vestibule_Side", new Vector3(side * (DoorWidth + WallThickness) / 2, DoorHeight / 2, z),
                    new Vector3(WallThickness, DoorHeight, VestibuleDepth), theme.doorwayMaterial);

            // In the space between the door and the ceiling (low rooms have less of it).
            var above = height - DoorHeight;
            var signHeight = Mathf.Min(0.35f, above * 0.85f);
            Signage.CreateText(parent, "Exit", new Vector3(0, DoorHeight + Mathf.Min(0.26f, above / 2), 0.02f), Quaternion.Euler(0, 180, 0),
                2.2f, theme.exitColor, new Vector2(DoorWidth, signHeight), material: theme.exitTextMaterial).text = "EXIT";

            var exit = new GameObject("Exit").AddComponent<DoorTrigger>();
            exit.transform.SetParent(parent, false);
            exit.transform.localPosition = new Vector3(0, DoorHeight / 2, z);
            exit.Size = new Vector3(DoorWidth, DoorHeight, VestibuleDepth);
            return exit;
        }
    }
}
