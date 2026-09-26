using System.Collections.Generic;
using System.Threading.Tasks;
using PlexBuster.Data;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlexBuster.Store
{
    /// <summary>
    /// A cinema auditorium: in through the back, stadium rows stepping down towards a 12 m scope screen between
    /// red curtains, a projector beam from the booth over the door. Your seat is in the middle of the fourth row,
    /// with a console beside it for the tape and the controls. The house lights go down when the film starts.
    /// </summary>
    public class CinemaRoom : ScreeningRoom
    {
        const float Width = 14f;
        const float AisleDepth = 2.4f;
        const int Rows = 7;
        const float RowDepth = 1.1f;
        const float RowDrop = 0.5f;
        const float Ceiling = 4f;
        const float ScreenZ = 17f;
        const float Depth = ScreenZ + 0.3f;
        const float ScreenWidth = 12f;
        const float ScreenHeight = ScreenWidth / 2.39f;
        const float ScreenCentreY = -0.3f;
        const int SeatsPerRow = 11;
        const float SeatPitch = 0.6f;
        const int MyRow = 3;

        static readonly float FrontFloor = -RowDrop * (Rows + 1);
        static readonly Color Warm = new(1f, 0.78f, 0.55f);

        Material beam;
        Color beamColour;

        protected override float DimmedTo => 0.03f;
        protected override float AmbientLit => 0.35f;
        protected override float AmbientDark => 0.03f;
        protected override string IdleNotice => "<b>PLEXBUSTER CINEMA</b>\n<size=45%>Put a tape in the console by your seat</size>";

        static float RowFloor(int row) => -RowDrop * (row + 1);
        static float RowCentreZ(int row) => AisleDepth + RowDepth * (row + 0.5f);

        public static async Task<IStorePlace> Build(StoreTheme theme, PosterCache posters, Vector3 origin)
        {
            var room = new GameObject("Cinema").AddComponent<CinemaRoom>();
            room.transform.position = origin;
            room.Init(theme, posters);
            room.Entry = new Pose(new Vector3(0, RowFloor(MyRow), RowCentreZ(MyRow) + 0.1f), Quaternion.identity);
            room.IdleGlass = new Color(0.035f, 0.035f, 0.04f);

            room.CreatePlayer(new Vector3(0, ScreenCentreY, ScreenZ - 0.5f), 0.3f, 12f, 60f);
            var reverb = room.gameObject.AddComponent<AudioReverbZone>();
            reverb.reverbPreset = AudioReverbPreset.Auditorium;
            reverb.minDistance = 12f;
            reverb.maxDistance = 30f;

            room.BuildShell();
            room.BuildScreen();
            room.BuildSeats();
            room.BuildConsole();
            room.BuildBooth();
            room.OnPlayerChanged();

            var shelf = room.Shelf(room.transform, "NowShowing", new Vector3(-4.2f, 0, 0.24f), Quaternion.identity, 6, 4);
            await room.StockShelf(shelf, "NOW SHOWING", 24);
            return room;
        }

        void BuildShell()
        {
            var shell = new GameObject("Shell").transform;
            shell.SetParent(transform, false);
            var carpet = Lit("Cinema Carpet", new Color(0.22f, 0.03f, 0.05f), 0.05f);
            var fabric = Lit("Cinema Walls", new Color(0.07f, 0.04f, 0.1f), 0.08f);
            var pilaster = Lit("Cinema Pilasters", new Color(0.12f, 0.07f, 0.15f), 0.2f);
            var dark = Lit("Cinema Ceiling", new Color(0.02f, 0.02f, 0.03f), 0.05f);
            var nosing = Glow("Step Lights", new Color(1f, 0.55f, 0.15f) * 0.9f);
            const float t = StoreShell.WallThickness;
            var height = Ceiling - FrontFloor;
            var midY = (Ceiling + FrontFloor) / 2;

            // Floors: the aisle behind the seats at the door's level, then each row a step down, then the flat front.
            StoreShell.Floor(shell, new Vector3(0, -RowDrop / 2, AisleDepth / 2), new Vector3(Width, RowDrop, AisleDepth), carpet).name = "Aisle";
            for (var row = 0; row < Rows; row++)
            {
                StoreShell.Floor(shell, new Vector3(0, RowFloor(row) - RowDrop / 2, RowCentreZ(row)), new Vector3(Width, RowDrop, RowDepth), carpet).name = $"Row {row + 1}";
                // Little lights along each step's edge, as in every cinema.
                Trim(shell, "StepLight", new Vector3(0, RowFloor(row - 1) - 0.02f, AisleDepth + RowDepth * row + 0.012f), new Vector3(Width, 0.015f, 0.02f), nosing);
            }
            var frontLength = Depth - (AisleDepth + RowDepth * Rows);
            StoreShell.Floor(shell, new Vector3(0, FrontFloor - 0.05f, Depth - frontLength / 2), new Vector3(Width, 0.1f, frontLength), carpet).name = "Front";

            Signage.Box(shell, "Ceiling", new Vector3(0, Ceiling + 0.05f, Depth / 2), new Vector3(Width + 2 * t, 0.1f, Depth + 2 * t), dark);
            Signage.Box(shell, "Wall_Screen", new Vector3(0, midY, Depth + t / 2), new Vector3(Width, height, t), dark);
            for (var side = -1; side <= 1; side += 2)
            {
                Signage.Box(shell, "Wall_Side", new Vector3(side * (Width + t) / 2, midY, Depth / 2), new Vector3(t, height, Depth + 2 * t), fabric);
                for (var z = 2f; z < Depth - 1; z += 3.5f)
                    Trim(shell, "Pilaster", new Vector3(side * (Width / 2 - 0.06f), midY, z), new Vector3(0.12f, height, 0.3f), pilaster);
            }
            Exit = StoreShell.FrontWallWithExit(shell, Theme, Width, Ceiling);

            // Sconces between the pilasters; they fade out when the film starts.
            var sconce = Glow("Sconce", new Color(1f, 0.75f, 0.45f) * 2.5f);
            for (var side = -1; side <= 1; side += 2)
            for (var z = 3.75f; z < Depth - 1; z += 3.5f)
            {
                var y = z < AisleDepth + RowDepth * Rows ? 1.6f - RowDrop * (z - AisleDepth) / RowDepth : FrontFloor + 2.4f;
                Trim(shell, "Sconce", new Vector3(side * (Width / 2 - 0.05f), y, z), new Vector3(0.1f, 0.35f, 0.22f), sconce);
                HouseLight(shell, new Vector3(side * (Width / 2 - 0.5f), y, z), Warm, 0.9f, 5.5f);
            }
            HouseLight(shell, new Vector3(0, Ceiling - 0.4f, AisleDepth / 2 + 0.5f), Warm, 1f, 7f);

        }

        void BuildScreen()
        {
            var parent = new GameObject("Screen").transform;
            parent.SetParent(transform, false);
            parent.localPosition = new Vector3(0, ScreenCentreY, ScreenZ);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Picture";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            quad.transform.localScale = new Vector3(ScreenWidth, ScreenHeight, 1);
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateScreenMaterial(Theme.cinemaScreenMaterial, ScreenWidth / ScreenHeight);
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            // Black masking around the picture.
            var masking = Unlit("Masking", new Color(0.005f, 0.005f, 0.005f));
            const float border = 0.35f;
            Trim(parent, "Mask_Top", new Vector3(0, ScreenHeight / 2 + border / 2, 0.02f), new Vector3(ScreenWidth + 2 * border, border, 0.02f), masking);
            Trim(parent, "Mask_Bottom", new Vector3(0, -ScreenHeight / 2 - border / 2, 0.02f), new Vector3(ScreenWidth + 2 * border, border, 0.02f), masking);
            for (var side = -1; side <= 1; side += 2)
                Trim(parent, "Mask_Side", new Vector3(side * (ScreenWidth / 2 + border / 2), 0, 0.02f), new Vector3(border, ScreenHeight, 0.02f), masking);

            var text = new GameObject("ScreenText").transform;
            text.SetParent(parent, false);
            text.localPosition = new Vector3(0, 0, -0.02f);
            CreateScreenText(text, new Vector2(ScreenWidth, ScreenHeight), 6.5f, 11f, new Color(1f, 0.85f, 0.5f));

            // Red velvet: gathered curtains either side and a pelmet across the top.
            var velvet = Lit("Velvet", new Color(0.42f, 0.02f, 0.05f), 0.35f);
            var velvetDark = Lit("Velvet Fold", new Color(0.26f, 0.01f, 0.03f), 0.3f);
            var drop = Ceiling - FrontFloor;
            for (var side = -1; side <= 1; side += 2)
            for (var i = 0; i < 6; i++)
            {
                var fold = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                fold.name = "Curtain";
                Destroy(fold.GetComponent<Collider>());
                fold.transform.SetParent(transform, false);
                fold.transform.SetLocalPositionAndRotation(new Vector3(side * (ScreenWidth / 2 + 0.35f + i * 0.12f), (Ceiling + FrontFloor) / 2, ScreenZ - 0.2f - (i % 2) * 0.09f), Quaternion.identity);
                fold.transform.localScale = new Vector3(0.22f, drop / 2, 0.22f);
                fold.GetComponent<MeshRenderer>().sharedMaterial = i % 2 == 0 ? velvet : velvetDark;
            }
            Signage.Box(transform, "Pelmet", new Vector3(0, Ceiling - 0.35f, ScreenZ - 0.3f), new Vector3(Width, 0.7f, 0.2f), velvet);
            for (var i = 0; i < 28; i++)
                Trim(transform, "PelmetFold", new Vector3(-Width / 2 + 0.25f + i * 0.5f, Ceiling - 0.35f, ScreenZ - 0.41f), new Vector3(0.12f, 0.68f, 0.02f), velvetDark);

            // The picture lights the room: mostly the front, a little all the way back.
            var spill = new GameObject("ScreenSpill").AddComponent<Light>();
            spill.transform.SetParent(transform, false);
            spill.transform.SetLocalPositionAndRotation(new Vector3(0, ScreenCentreY, ScreenZ - 1f), Quaternion.LookRotation(Vector3.back));
            spill.type = LightType.Spot;
            spill.spotAngle = 140f;
            spill.innerSpotAngle = 60f;
            spill.range = 28f;
            Spill(spill, 7f);
        }

        /// <summary>Rows of seats, one combined mesh per row and material (hundreds of boxes otherwise).</summary>
        void BuildSeats()
        {
            var fabric = Lit("Seat Fabric", new Color(0.5f, 0.03f, 0.05f), 0.2f);
            var plastic = Lit("Seat Frame", new Color(0.05f, 0.05f, 0.06f), 0.4f);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var cubeMesh = cube.GetComponent<MeshFilter>().sharedMesh;
            Destroy(cube);

            var seats = new GameObject("Seats").transform;
            seats.SetParent(transform, false);
            for (var row = 0; row < Rows; row++)
            {
                var soft = new List<CombineInstance>();
                var hard = new List<CombineInstance>();
                var floor = RowFloor(row);
                var z = RowCentreZ(row) + 0.1f;
                for (var seat = 0; seat < SeatsPerRow; seat++)
                {
                    var x = (seat - (SeatsPerRow - 1) / 2f) * SeatPitch;
                    Add(soft, cubeMesh, new Vector3(x, floor + 0.44f, z + 0.03f), Quaternion.identity, new Vector3(0.5f, 0.1f, 0.48f));
                    Add(soft, cubeMesh, new Vector3(x, floor + 0.72f, z - 0.25f), Quaternion.Euler(-12, 0, 0), new Vector3(0.5f, 0.56f, 0.09f));
                    Add(hard, cubeMesh, new Vector3(x, floor + 0.18f, z - 0.05f), Quaternion.identity, new Vector3(0.3f, 0.36f, 0.28f));
                    Add(hard, cubeMesh, new Vector3(x - SeatPitch / 2, floor + 0.62f, z - 0.02f), Quaternion.identity, new Vector3(0.06f, 0.06f, 0.44f));
                }
                Add(hard, cubeMesh, new Vector3((SeatsPerRow / 2f) * SeatPitch, floor + 0.62f, z - 0.02f), Quaternion.identity, new Vector3(0.06f, 0.06f, 0.44f));
                Combined(seats, $"Row {row + 1} Seats", soft, fabric);
                Combined(seats, $"Row {row + 1} Frames", hard, plastic);
            }
        }

        static void Add(List<CombineInstance> list, Mesh mesh, Vector3 position, Quaternion rotation, Vector3 size) =>
            list.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(position, rotation, size) });

        void Combined(Transform parent, string name, List<CombineInstance> parts, Material material)
        {
            var mesh = Own(new Mesh { name = name });
            mesh.CombineMeshes(parts.ToArray(), true, true);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>A slim console on the armrest to the right of your seat: the tape slot on top, the controls above it.</summary>
        void BuildConsole()
        {
            var console = new GameObject("SeatConsole").transform;
            console.SetParent(transform, false);
            console.localPosition = new Vector3(SeatPitch / 2, RowFloor(MyRow), RowCentreZ(MyRow) + 0.12f);
            var body = Lit("Console", new Color(0.08f, 0.08f, 0.09f), 0.6f, 0.4f);
            const float height = 0.78f;
            Signage.Box(console, "Body", new Vector3(0, height / 2, 0), new Vector3(0.1f, height, 0.3f), body);
            Trim(console, "Glow", new Vector3(0, height + 0.001f, 0), new Vector3(0.07f, 0.002f, 0.16f), Unlit("Console Glow", new Color(1f, 0.55f, 0.15f) * 2f));

            // The tape stands in the top like a slice of toast, spine up, facing the seat.
            var slot = TapeSlot.Create(console, "TapeSlot", new Vector3(0, height, 0), Quaternion.identity, 0.3f,
                new Pose(new Vector3(0, 0.04f, 0), Quaternion.Euler(0, -90, 0)),
                new Pose(new Vector3(-0.3f, 0.1f, 0.05f), Quaternion.Euler(90, -90, 0)));
            CreateSlot(slot);

            // Controls on a small arm, turned towards a seated head.
            var head = new Vector3(-SeatPitch / 2, 1.15f, -0.12f);
            var panel = new Vector3(0.2f, 0.9f, 0.24f);
            CreateControls(console, panel, Quaternion.LookRotation(panel - head), 0.45f);
            Trim(console, "Arm", new Vector3(0.1f, height - 0.05f, 0.2f), new Vector3(0.22f, 0.02f, 0.02f), body);
        }

        /// <summary>The projection booth window over the door and its beam to the screen.</summary>
        void BuildBooth()
        {
            var booth = new GameObject("Booth").transform;
            booth.SetParent(transform, false);
            var window = new Vector3(0, Ceiling - 0.8f, 0.02f);
            Trim(booth, "Window", window, new Vector3(1.1f, 0.5f, 0.02f), Unlit("Booth Window", new Color(0.35f, 0.33f, 0.3f)));

            if (Theme.beamMaterial == null) return;
            var from = window + Vector3.forward * 0.05f;
            var to = new Vector3(0, ScreenCentreY, ScreenZ - 0.05f);
            const float lens = 0.08f;
            var vertices = new[]
            {
                from + new Vector3(-lens, -lens, 0), from + new Vector3(lens, -lens, 0), from + new Vector3(lens, lens, 0), from + new Vector3(-lens, lens, 0),
                to + new Vector3(-ScreenWidth / 2, -ScreenHeight / 2, 0), to + new Vector3(ScreenWidth / 2, -ScreenHeight / 2, 0),
                to + new Vector3(ScreenWidth / 2, ScreenHeight / 2, 0), to + new Vector3(-ScreenWidth / 2, ScreenHeight / 2, 0),
            };
            var mesh = Own(new Mesh { name = "ProjectorBeam", vertices = vertices, triangles = new[] { 0, 4, 5, 0, 5, 1, 1, 5, 6, 1, 6, 2, 2, 6, 7, 2, 7, 3, 3, 7, 4, 3, 4, 0 } });
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.RecalculateBounds();
            var go = new GameObject("Beam", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(booth, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            beam = Own(new Material(Theme.beamMaterial) { name = "ProjectorBeam" });
            beamColour = Theme.beamMaterial.GetColor("_BaseColor");
            renderer.sharedMaterial = beam;
        }

        protected override void Update()
        {
            base.Update();
            if (beam == null) return;
            // The beam is only visible while something plays, and takes the picture's colour.
            var picture = Player.AverageColor;
            var on = Player.Current is ScreenPlayer.State.Playing or ScreenPlayer.State.Paused;
            var colour = on ? new Color(picture.r, picture.g, picture.b, beamColour.a * 0.12f) : Color.clear;
            beam.SetColor("_BaseColor", Color.Lerp(beam.GetColor("_BaseColor"), colour, Time.deltaTime * 4f));
        }
    }
}
