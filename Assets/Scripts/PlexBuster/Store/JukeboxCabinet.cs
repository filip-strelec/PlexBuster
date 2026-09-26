using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PlexBuster.Store
{
    /// <summary>
    /// The jukebox's cabinet, built from code like the rest of the store: a lacquered body under an arch with a
    /// neon ring, a now-playing screen in the arch, PREV / PLAY / NEXT buttons and a speaker grille on the front.
    /// Origin on the floor, front facing +Z.
    /// </summary>
    public class JukeboxCabinet
    {
        public const float Width = 0.9f;
        public const float Depth = 0.55f;
        public const float BodyHeight = 1.05f;
        public const float Height = BodyHeight + Width / 2;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color Lacquer = new(0.32f, 0.02f, 0.05f);
        static readonly Color Chrome = new(0.75f, 0.76f, 0.8f);
        static readonly Color Glass = new(0.02f, 0.01f, 0.05f);
        static readonly Color HeadingColor = new(1f, 0.35f, 0.8f);
        static readonly Color ArtistColor = new(0.45f, 0.85f, 1f);
        static readonly Color InfoColor = new(0.75f, 0.75f, 0.85f);

        readonly List<Object> owned = new();
        Material archNeon, sideNeon;

        public TextMeshProUGUI Heading { get; private set; }
        public TextMeshProUGUI Title { get; private set; }
        public TextMeshProUGUI Artist { get; private set; }
        public TextMeshProUGUI Info { get; private set; }
        public Button PlayPause { get; private set; }

        public static JukeboxCabinet Build(Transform parent, StoreTheme theme, Action previous, Action playPause, Action next)
        {
            var cabinet = new JukeboxCabinet();
            var root = new GameObject("Cabinet").transform;
            root.SetParent(parent, false);

            var body = cabinet.Lit(theme, "Jukebox Lacquer", Lacquer, 0, 0.8f);
            var chrome = cabinet.Lit(theme, "Jukebox Chrome", Chrome, 0.6f, 0.85f);
            var glass = cabinet.Unlit(theme, "Jukebox Glass", Glass);
            cabinet.archNeon = cabinet.Unlit(theme, "Jukebox Neon Arch", Color.white);
            cabinet.sideNeon = cabinet.Unlit(theme, "Jukebox Neon Sides", Color.white);

            const float front = Depth / 2;
            const float radius = Width / 2;

            Trim(root, "Plinth", new Vector3(0, 0.04f, 0), new Vector3(Width + 0.04f, 0.08f, Depth + 0.04f), chrome);
            Signage.Box(root, "Body", new Vector3(0, (0.08f + BodyHeight) / 2, 0), new Vector3(Width, BodyHeight - 0.08f, Depth), body);
            cabinet.Arch(root, "Arch", new Vector3(0, BodyHeight, 0), 0, radius, Depth, body);
            cabinet.Arch(root, "Window", new Vector3(0, BodyHeight, front), 0, radius - 0.075f, 0.012f, glass);
            cabinet.Arch(root, "ArchNeon", new Vector3(0, BodyHeight, front), radius - 0.075f, radius - 0.03f, 0.03f, cabinet.archNeon);
            // Covers where the arch meets the body, including the ends of the neon ring.
            Trim(root, "Crown", new Vector3(0, BodyHeight, front + 0.01f), new Vector3(Width + 0.02f, 0.035f, 0.03f), chrome);

            for (var side = -1; side <= 1; side += 2)
                Trim(root, "SideNeon", new Vector3(side * (Width / 2 - 0.035f), (0.12f + BodyHeight - 0.03f) / 2, front + 0.01f),
                    new Vector3(0.035f, BodyHeight - 0.15f, 0.02f), cabinet.sideNeon);

            Trim(root, "Grille", new Vector3(0, 0.44f, front + 0.003f), new Vector3(Width - 0.2f, 0.52f, 0.006f), theme.doorwayMaterial);
            for (var i = 0; i < 7; i++)
                Trim(root, "Slat", new Vector3(0, 0.23f + i * 0.07f, front + 0.008f), new Vector3(Width - 0.2f, 0.012f, 0.01f), chrome);

            cabinet.BuildScreen(root, front);
            cabinet.PlayPause = BuildControls(root, front, previous, playPause, next);
            cabinet.SetNeon(0, 1);
            return cabinet;
        }

        /// <summary>Colours the neon: the ring at <paramref name="hue"/>, the side tubes opposite it on the colour wheel.</summary>
        public void SetNeon(float hue, float intensity)
        {
            archNeon.SetColor(BaseColorId, Neon(hue, intensity));
            sideNeon.SetColor(BaseColorId, Neon(hue + 0.5f, intensity));
        }

        /// <summary>Destroys the meshes and materials made for the cabinet.</summary>
        public void Release()
        {
            foreach (var o in owned)
                if (o != null) Object.Destroy(o);
            owned.Clear();
        }

        /// <summary>Now-playing text inside the arch window, on a transparent canvas.</summary>
        void BuildScreen(Transform root, float front)
        {
            var screen = WorldUI.CreateCanvas(root, "Screen", new Vector2(600, 300), new Vector3(0, BodyHeight + 0.17f, front + 0.01f),
                Quaternion.Euler(0, 180, 0));
            screen.GetComponent<Image>().enabled = false;

            Heading = WorldUI.Label(WorldUI.Area(screen, "Heading", new Vector2(0, 0.8f), new Vector2(1, 1), 4), "JUKEBOX", 28);
            Heading.color = HeadingColor;
            // The window narrows towards the top of the arch; keep long titles clear of the neon ring.
            var titleArea = WorldUI.Area(screen, "Title", new Vector2(0, 0.45f), new Vector2(1, 0.8f));
            titleArea.offsetMin = new Vector2(40, 0);
            titleArea.offsetMax = new Vector2(-40, 0);
            Title = WorldUI.Label(titleArea, "", 56);
            Artist = WorldUI.Label(WorldUI.Area(screen, "Artist", new Vector2(0, 0.22f), new Vector2(1, 0.45f), 4), "", 38);
            Artist.color = ArtistColor;
            Info = WorldUI.Label(WorldUI.Area(screen, "Info", new Vector2(0, 0), new Vector2(1, 0.22f), 4), "", 26);
            Info.color = InfoColor;
        }

        /// <summary>The button strip under the arch, tilted back a little like a jukebox's selector panel.</summary>
        static Button BuildControls(Transform root, float front, Action previous, Action playPause, Action next)
        {
            var controls = WorldUI.CreateCanvas(root, "Controls", new Vector2(640, 150), new Vector3(0, 0.88f, front + 0.035f),
                Quaternion.Euler(20, 180, 0));
            var row = WorldUI.Area(controls, "Buttons", Vector2.zero, Vector2.one, 18);
            WorldUI.Grid(row, new Vector2(190, 114), new Vector2(17, 0), 3);
            WorldUI.Button(row, "<<  PREV", 34, previous);
            var play = WorldUI.Button(row, "PLAY", 34, playPause);
            WorldUI.Button(row, "NEXT  >>", 34, next);
            return play;
        }

        static Color Neon(float hue, float intensity)
        {
            var colour = Color.HSVToRGB(Mathf.Repeat(hue, 1), 0.85f, 1) * intensity;
            colour.a = 1;
            return colour;
        }

        static GameObject Trim(Transform parent, string name, Vector3 centre, Vector3 size, Material material)
        {
            var box = Signage.Box(parent, name, centre, size, material);
            Object.Destroy(box.GetComponent<Collider>());
            box.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return box;
        }

        void Arch(Transform parent, string name, Vector3 centre, float inner, float outer, float depth, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.GetComponent<MeshFilter>().sharedMesh = Own(ArchMesh(inner, outer, depth));
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>
        /// The upper half of a ring (a half disc when <paramref name="inner"/> is 0), extruded along Z and centred
        /// on the origin. The flat underside is left open; the arch always sits on something.
        /// </summary>
        static Mesh ArchMesh(float inner, float outer, float depth, int segments = 40)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            var z = depth / 2;

            // Two rows of points along the half circle, joined by quads wound clockwise as seen from outside.
            void Strip(float radius0, float z0, float radius1, float z1)
            {
                var start = vertices.Count;
                for (var i = 0; i <= segments; i++)
                {
                    var angle = Mathf.PI * i / segments;
                    var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                    vertices.Add(direction * radius0 + Vector3.forward * z0);
                    vertices.Add(direction * radius1 + Vector3.forward * z1);
                    uvs.Add(new Vector2((float)i / segments, 0));
                    uvs.Add(new Vector2((float)i / segments, 1));
                }
                for (var i = 0; i < segments; i++)
                {
                    int a = start + 2 * i, b = a + 2, c = a + 3, d = a + 1;
                    triangles.AddRange(new[] { a, b, c, a, c, d });
                }
            }

            Strip(outer, z, inner, z);    // front
            Strip(inner, -z, outer, -z);  // back
            Strip(outer, -z, outer, z);   // curved top
            if (inner > 0) Strip(inner, z, inner, -z);

            var mesh = new Mesh { name = "JukeboxArch" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        Material Lit(StoreTheme theme, string name, Color colour, float metallic, float smoothness)
        {
            // A copy of a theme material, so it's the same (URP Lit) shader and makes it into builds.
            var material = Own(new Material(theme.doorFrameMaterial) { name = name });
            material.SetColor(BaseColorId, colour);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        Material Unlit(StoreTheme theme, string name, Color colour)
        {
            var material = Own(new Material(theme.lightPanelMaterial) { name = name });
            material.SetColor(BaseColorId, colour);
            return material;
        }

        T Own<T>(T obj) where T : Object
        {
            owned.Add(obj);
            return obj;
        }
    }
}
