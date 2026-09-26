using System.Collections.Generic;
using System.Threading.Tasks;
using PlexBuster.Data;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlexBuster.Store
{
    /// <summary>
    /// A 90s living room with a giant CRT television: put a tape in the VCR beside it and watch from the sofa.
    /// The remote is a panel on the coffee table; a shelf of recently added tapes stands by the wall.
    /// </summary>
    public class TvRoom : ScreeningRoom
    {
        const float Width = 6.4f;
        const float Depth = 7f;
        const float Height = 2.7f;

        // The set: 4:3 tube, 152 x 114 cm picture, on a low stand against the far wall.
        const float ScreenWidth = 1.52f;
        const float ScreenHeight = 1.14f;
        const float StandHeight = 0.42f;
        const float CabinetWidth = 1.92f;
        const float CabinetHeight = 1.5f;
        const float CabinetDepth = 0.62f;

        static readonly Color Warm = new(1f, 0.82f, 0.6f);

        TextMeshPro vcrDisplay;
        TextMeshPro osd;
        float osdUntil;
        ScreenPlayer.State lastState;

        protected override float DimmedTo => 0.22f;
        protected override float AmbientLit => 0.7f;
        protected override float AmbientDark => 0.2f;
        protected override string IdleNotice => "VIDEO 1\n<size=55%>PUT A TAPE IN THE VCR</size>";
        protected override float LoadingStatic => 1f;

        public static async Task<IStorePlace> Build(StoreTheme theme, PosterCache posters, Vector3 origin)
        {
            var room = new GameObject("TV Room").AddComponent<TvRoom>();
            room.transform.position = origin;
            room.Init(theme, posters);
            room.Entry = new Pose(new Vector3(0, 0, 3.2f), Quaternion.identity);
            room.IdleGlass = new Color(0.02f, 0.05f, 0.45f);

            var tvCentre = new Vector3(0, StandHeight + CabinetHeight / 2, Depth - CabinetDepth / 2 - 0.35f);
            room.CreatePlayer(tvCentre + Vector3.back * 0.3f, 0.75f, 2.5f, 14f);
            room.BuildShell();
            room.BuildTelevision(tvCentre);
            room.BuildVcr(new Vector3(1.75f, 0, Depth - 0.6f));
            room.BuildSeating();
            room.BuildDecor();
            room.OnPlayerChanged();

            // The unit's origin is its front: stand its back just clear of the wall's dado rail (4 cm out).
            var shelf = room.Shelf(room.transform, "Shelf", new Vector3(-Width / 2 + 0.045f + ShelfUnit.Depth + ShelfUnit.BackSkin, 0, 4.2f),
                Quaternion.Euler(0, 90, 0), 5, 4);
            // On the wall just past the shelf's end, above the dado rail.
            room.ReshelveButtonOnWall(shelf, new Vector3(-Width / 2, 1.25f, 4.95f), Vector3.right);
            await room.StockShelf(shelf, "JUST IN", 20);
            return room;
        }

        void BuildShell()
        {
            var shell = new GameObject("Shell").transform;
            shell.SetParent(transform, false);
            var carpet = Lit("TV Room Carpet", new Color(0.36f, 0.24f, 0.17f), 0.05f);
            var wallpaper = Lit("TV Room Wallpaper", new Color(0.66f, 0.6f, 0.46f), 0.15f);
            var wood = Lit("TV Room Wood", new Color(0.3f, 0.17f, 0.09f), 0.45f);
            var ceiling = Lit("TV Room Ceiling", new Color(0.86f, 0.84f, 0.78f), 0.1f);
            const float t = StoreShell.WallThickness;

            StoreShell.Floor(shell, new Vector3(0, -0.05f, Depth / 2), new Vector3(Width + 2 * t, 0.1f, Depth + 2 * t), carpet);
            Signage.Box(shell, "Ceiling", new Vector3(0, Height + 0.05f, Depth / 2), new Vector3(Width + 2 * t, 0.1f, Depth + 2 * t), ceiling);
            Signage.Box(shell, "Wall_Back", new Vector3(0, Height / 2, Depth + t / 2), new Vector3(Width, Height, t), wallpaper);
            Signage.Box(shell, "Wall_Left", new Vector3(-(Width + t) / 2, Height / 2, Depth / 2), new Vector3(t, Height, Depth + 2 * t), wallpaper);
            Signage.Box(shell, "Wall_Right", new Vector3((Width + t) / 2, Height / 2, Depth / 2), new Vector3(t, Height, Depth + 2 * t), wallpaper);

            // Wood panelling up to the dado rail, like a proper 90s den.
            const float dado = 0.95f;
            Trim(shell, "Panel_Back", new Vector3(0, dado / 2, Depth - 0.01f), new Vector3(Width, dado, 0.02f), wood);
            Trim(shell, "Panel_Left", new Vector3(-Width / 2 + 0.01f, dado / 2, Depth / 2), new Vector3(0.02f, dado, Depth), wood);
            Trim(shell, "Panel_Right", new Vector3(Width / 2 - 0.01f, dado / 2, Depth / 2), new Vector3(0.02f, dado, Depth), wood);
            Trim(shell, "Rail_Back", new Vector3(0, dado, Depth - 0.02f), new Vector3(Width, 0.04f, 0.04f), wood);
            Trim(shell, "Rail_Left", new Vector3(-Width / 2 + 0.02f, dado, Depth / 2), new Vector3(0.04f, 0.04f, Depth), wood);
            Trim(shell, "Rail_Right", new Vector3(Width / 2 - 0.02f, dado, Depth / 2), new Vector3(0.04f, 0.04f, Depth), wood);

            Exit = StoreShell.FrontWallWithExit(shell, Theme, Width, Height, wallpaper, band: false);
            var segment = (Width - StoreShell.DoorWidth) / 2;
            for (var side = -1; side <= 1; side += 2)
            {
                var x = side * (StoreShell.DoorWidth + segment) / 2;
                Trim(shell, "Panel_Front", new Vector3(x, dado / 2, 0.01f), new Vector3(segment, dado, 0.02f), wood);
                Trim(shell, "Rail_Front", new Vector3(x, dado, 0.02f), new Vector3(segment, 0.04f, 0.04f), wood);
            }

            // Warm lamps rather than store lights: a ceiling fitting and two lamps, all dimming for the film.
            HouseLight(shell, new Vector3(0, Height - 0.35f, Depth / 2 - 0.5f), Warm, 1.1f, 7f);
            var shade = Glow("Ceiling Shade", new Color(1f, 0.85f, 0.6f) * 1.6f);
            var fitting = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fitting.name = "CeilingShade";
            fitting.transform.SetParent(shell, false);
            fitting.transform.SetLocalPositionAndRotation(new Vector3(0, Height - 0.12f, Depth / 2 - 0.5f), Quaternion.identity);
            fitting.transform.localScale = new Vector3(0.45f, 0.18f, 0.45f);
            fitting.GetComponent<MeshRenderer>().sharedMaterial = shade;
            Destroy(fitting.GetComponent<Collider>());
        }

        void BuildTelevision(Vector3 centre)
        {
            var tv = new GameObject("Television").transform;
            tv.SetParent(transform, false);
            tv.localPosition = centre;

            var plastic = Lit("TV Plastic", new Color(0.08f, 0.08f, 0.085f), 0.55f);
            var bezel = Lit("TV Bezel", new Color(0.13f, 0.13f, 0.14f), 0.6f);
            var woodgrain = Lit("TV Woodgrain", new Color(0.35f, 0.2f, 0.1f), 0.5f);
            var grille = Lit("TV Grille", new Color(0.03f, 0.03f, 0.03f), 0.2f);
            var chrome = Lit("TV Chrome", new Color(0.7f, 0.7f, 0.72f), 0.85f, 0.9f);

            // Stand: a low wooden cabinet with a glass door.
            var standZ = 0.02f;
            Signage.Box(tv, "Stand", new Vector3(0, -centre.y + StandHeight / 2, standZ), new Vector3(CabinetWidth + 0.3f, StandHeight, CabinetDepth + 0.1f), woodgrain);
            Trim(tv, "StandGlass", new Vector3(0, -centre.y + StandHeight / 2, standZ - (CabinetDepth + 0.1f) / 2 - 0.003f),
                new Vector3(CabinetWidth, StandHeight - 0.1f, 0.004f), grille);

            // Cabinet: woodgrain sides and top around a dark front, the tube's bulk bulging out behind.
            var h = CabinetHeight;
            var w = CabinetWidth;
            var d = CabinetDepth;
            Signage.Box(tv, "Cabinet", new Vector3(0, 0, 0.05f), new Vector3(w, h, d - 0.1f), woodgrain);
            Signage.Box(tv, "Tube", new Vector3(0, 0.05f, d / 2 + 0.2f), new Vector3(w * 0.72f, h * 0.72f, 0.5f), plastic);
            var front = -d / 2 + 0.02f;
            const float frameDepth = 0.06f;
            var sideWidth = (w - ScreenWidth) / 2;
            var screenY = 0.12f;
            var top = h / 2 - (screenY + ScreenHeight / 2);
            var bottom = h / 2 + screenY - ScreenHeight / 2;
            Signage.Box(tv, "Frame_Top", new Vector3(0, h / 2 - top / 2, front), new Vector3(w, top, frameDepth), bezel);
            Signage.Box(tv, "Frame_Bottom", new Vector3(0, -h / 2 + bottom / 2, front), new Vector3(w, bottom, frameDepth), bezel);
            Signage.Box(tv, "Frame_Left", new Vector3(-w / 2 + sideWidth / 2, screenY, front), new Vector3(sideWidth, ScreenHeight, frameDepth), bezel);
            Signage.Box(tv, "Frame_Right", new Vector3(w / 2 - sideWidth / 2, screenY, front), new Vector3(sideWidth, ScreenHeight, frameDepth), bezel);

            // Speaker grilles, brand, power light and knobs along the chin.
            var chinY = -h / 2 + bottom / 2;
            for (var side = -1; side <= 1; side += 2)
                for (var i = 0; i < 5; i++)
                    Trim(tv, "Grille", new Vector3(side * (w / 2 - 0.3f), chinY - 0.07f + i * 0.035f, front - frameDepth / 2 - 0.002f),
                        new Vector3(0.36f, 0.012f, 0.004f), grille);
            Signage.CreateText(tv, "Brand", new Vector3(0, chinY + 0.03f, front - frameDepth / 2 - 0.004f), Quaternion.identity,
                0.8f, new Color(0.75f, 0.75f, 0.78f), new Vector2(0.5f, 0.08f)).text = "<b>PLEXTRON</b>";
            Trim(tv, "PowerLight", new Vector3(0.4f, chinY - 0.05f, front - frameDepth / 2 - 0.003f), new Vector3(0.02f, 0.02f, 0.004f),
                Unlit("TV Power", new Color(0.2f, 3f, 0.4f)));
            for (var i = 0; i < 2; i++)
            {
                var knob = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                knob.name = "Knob";
                knob.transform.SetParent(tv, false);
                knob.transform.SetLocalPositionAndRotation(new Vector3(-0.35f - i * 0.12f, chinY - 0.04f, front - frameDepth / 2 - 0.015f), Quaternion.Euler(90, 0, 0));
                knob.transform.localScale = new Vector3(0.06f, 0.015f, 0.06f);
                knob.GetComponent<MeshRenderer>().sharedMaterial = chrome;
                Destroy(knob.GetComponent<Collider>());
            }

            // Rabbit ears on top.
            Signage.Box(tv, "AntennaBase", new Vector3(0, h / 2 + 0.03f, 0.1f), new Vector3(0.22f, 0.06f, 0.14f), plastic);
            for (var side = -1; side <= 1; side += 2)
            {
                var rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                rod.name = "Antenna";
                rod.transform.SetParent(tv, false);
                rod.transform.SetLocalPositionAndRotation(new Vector3(side * 0.25f, h / 2 + 0.4f, 0.1f), Quaternion.Euler(0, 0, -side * 32));
                rod.transform.localScale = new Vector3(0.012f, 0.45f, 0.012f);
                rod.GetComponent<MeshRenderer>().sharedMaterial = chrome;
                Destroy(rod.GetComponent<Collider>());
            }

            // The picture tube's face: a gently bulging surface just inside the frame.
            var screen = new GameObject("Screen", typeof(MeshFilter), typeof(MeshRenderer)).transform;
            screen.SetParent(tv, false);
            screen.localPosition = new Vector3(0, screenY, front + 0.01f);
            screen.GetComponent<MeshFilter>().sharedMesh = Own(CurvedScreen(ScreenWidth, ScreenHeight, 0.05f));
            var renderer = screen.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateScreenMaterial(Theme.crtScreenMaterial, ScreenWidth / ScreenHeight);
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            // Text sits just in front of the bulge's peak.
            var textPlane = new GameObject("ScreenText").transform;
            textPlane.SetParent(screen, false);
            textPlane.localPosition = new Vector3(0, 0, -0.055f);
            CreateScreenText(textPlane, new Vector2(ScreenWidth, ScreenHeight), 1.05f, 2.4f, new Color(0.85f, 1f, 0.9f));
            osd = Signage.CreateText(textPlane, "OSD", new Vector3(-ScreenWidth * 0.3f, ScreenHeight * 0.38f, -0.002f), Quaternion.identity,
                1.6f, new Color(0.4f, 1f, 0.5f), new Vector2(0.5f, 0.15f), TextAlignmentOptions.Left);
            osd.text = "";

            // The tube lights the room in the colours of the picture.
            var glow = new GameObject("ScreenGlow").AddComponent<Light>();
            glow.transform.SetParent(tv, false);
            glow.transform.localPosition = new Vector3(0, screenY, front - 0.9f);
            glow.type = LightType.Point;
            glow.range = 6f;
            Spill(glow, 3.5f);
        }

        void BuildVcr(Vector3 position)
        {
            var vcr = new GameObject("VCR").transform;
            vcr.SetParent(transform, false);
            vcr.localPosition = position;

            var wood = Lit("VCR Table", new Color(0.3f, 0.17f, 0.09f), 0.45f);
            var body = Lit("VCR Body", new Color(0.1f, 0.1f, 0.11f), 0.5f, 0.3f);
            var silver = Lit("VCR Face", new Color(0.55f, 0.56f, 0.58f), 0.6f, 0.6f);
            const float tableHeight = 0.72f;
            Signage.Box(vcr, "Table", new Vector3(0, tableHeight / 2, 0), new Vector3(0.9f, tableHeight, 0.5f), wood);

            var deck = tableHeight + 0.05f;
            const float x = -0.2f;
            Signage.Box(vcr, "Body", new Vector3(x, deck, 0), new Vector3(0.43f, 0.1f, 0.32f), body);
            Trim(vcr, "Face", new Vector3(x, deck, -0.161f), new Vector3(0.43f, 0.1f, 0.004f), silver);
            Trim(vcr, "Flap", new Vector3(x - 0.06f, deck + 0.012f, -0.164f), new Vector3(0.2f, 0.035f, 0.004f), body);
            vcrDisplay = Signage.CreateText(vcr, "Display", new Vector3(x + 0.12f, deck + 0.01f, -0.165f), Quaternion.identity,
                0.35f, new Color(0.3f, 1f, 0.45f), new Vector2(0.12f, 0.03f), material: StoreMaterials.GlowText("VCR Display", new Color(0.3f, 2.5f, 0.5f)));
            vcrDisplay.text = "12:00";
            Signage.CreateText(vcr, "Label", new Vector3(x - 0.06f, deck - 0.03f, -0.165f), Quaternion.identity,
                0.2f, new Color(0.85f, 0.85f, 0.85f), new Vector2(0.2f, 0.02f)).text = "VHS  HQ";

            // The tape goes in through the flap: it parks half inside, sticking out towards the room.
            var slot = TapeSlot.Create(vcr, "TapeSlot", new Vector3(x - 0.06f, deck + 0.012f, -0.16f), Quaternion.identity, 0.35f,
                new Pose(new Vector3(0, 0, 0.02f), Quaternion.Euler(90, 0, 0)),
                new Pose(new Vector3(0.52f, -0.03f, 0.08f), Quaternion.Euler(90, 0, 0)));
            CreateSlot(slot);
        }

        void BuildSeating()
        {
            var fabric = Lit("Sofa Fabric", new Color(0.16f, 0.3f, 0.27f), 0.12f);
            var cushion = Lit("Sofa Cushion", new Color(0.19f, 0.35f, 0.31f), 0.12f);
            var wood = Lit("Coffee Table", new Color(0.33f, 0.19f, 0.1f), 0.55f);

            var sofa = new GameObject("Sofa").transform;
            sofa.SetParent(transform, false);
            sofa.localPosition = new Vector3(0, 0, 2.3f);
            Signage.Box(sofa, "Base", new Vector3(0, 0.21f, 0), new Vector3(2.2f, 0.42f, 0.9f), fabric);
            Signage.Box(sofa, "Back", new Vector3(0, 0.62f, -0.36f), new Vector3(2.2f, 0.5f, 0.2f), fabric);
            for (var side = -1; side <= 1; side += 2)
                Signage.Box(sofa, "Arm", new Vector3(side * 1.0f, 0.5f, 0), new Vector3(0.2f, 0.35f, 0.9f), fabric);
            for (var i = -1; i <= 1; i++)
            {
                Trim(sofa, "Seat", new Vector3(i * 0.6f, 0.48f, 0.06f), new Vector3(0.58f, 0.12f, 0.68f), cushion);
                Trim(sofa, "BackCushion", new Vector3(i * 0.6f, 0.72f, -0.22f), new Vector3(0.56f, 0.4f, 0.14f), cushion);
            }

            var table = new GameObject("CoffeeTable").transform;
            table.SetParent(transform, false);
            table.localPosition = new Vector3(0, 0, 3.95f);
            Signage.Box(table, "Top", new Vector3(0, 0.44f, 0), new Vector3(1.2f, 0.05f, 0.6f), wood);
            for (var x = -1; x <= 1; x += 2)
            for (var z = -1; z <= 1; z += 2)
                Trim(table, "Leg", new Vector3(x * 0.54f, 0.21f, z * 0.24f), new Vector3(0.05f, 0.42f, 0.05f), wood);

            // The "remote": the control panel propped up on the table, tilted towards the sofa.
            CreateControls(table, new Vector3(0, 0.6f, -0.05f), Quaternion.LookRotation(new Vector3(0, -0.55f, 0.84f)), 0.75f);

            // A rug under it all.
            Trim(transform, "Rug", new Vector3(0, 0.004f, 3.7f), new Vector3(3.2f, 0.008f, 2.4f), Lit("Rug", new Color(0.45f, 0.12f, 0.1f), 0.05f));
        }

        void BuildDecor()
        {
            var decor = new GameObject("Decor").transform;
            decor.SetParent(transform, false);

            // Floor lamp in the corner behind the sofa, and a table lamp by the VCR.
            var metal = Lit("Lamp Metal", new Color(0.2f, 0.18f, 0.15f), 0.7f, 0.8f);
            var shade = Glow("Lamp Shade", new Color(1f, 0.8f, 0.55f) * 2.2f);
            FloorLamp(decor, new Vector3(Width / 2 - 0.45f, 0, 1.4f), metal, shade);
            HouseLight(decor, new Vector3(Width / 2 - 0.45f, 1.55f, 1.4f), Warm, 0.9f, 5f);
            FloorLamp(decor, new Vector3(-Width / 2 + 0.45f, 0, Depth - 0.5f), metal, shade);
            HouseLight(decor, new Vector3(-Width / 2 + 0.45f, 1.55f, Depth - 0.5f), Warm, 0.7f, 5f);

            // A potted plant beside the television.
            var pot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pot.name = "Pot";
            pot.transform.SetParent(decor, false);
            pot.transform.SetLocalPositionAndRotation(new Vector3(-1.55f, 0.2f, Depth - 0.55f), Quaternion.identity);
            pot.transform.localScale = new Vector3(0.36f, 0.2f, 0.36f);
            pot.GetComponent<MeshRenderer>().sharedMaterial = Lit("Pot", new Color(0.55f, 0.28f, 0.16f), 0.3f);
            var leaves = Lit("Leaves", new Color(0.13f, 0.32f, 0.12f), 0.3f);
            for (var i = 0; i < 7; i++)
            {
                var leaf = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                leaf.name = "Leaves";
                leaf.transform.SetParent(decor, false);
                var angle = i * Mathf.PI * 2 / 7;
                leaf.transform.SetLocalPositionAndRotation(new Vector3(-1.55f + Mathf.Cos(angle) * 0.14f, 0.7f + (i % 3) * 0.22f, Depth - 0.55f + Mathf.Sin(angle) * 0.14f),
                    Quaternion.Euler(i * 23, i * 51, 0));
                leaf.transform.localScale = new Vector3(0.34f, 0.5f, 0.3f);
                leaf.GetComponent<MeshRenderer>().sharedMaterial = leaves;
                Destroy(leaf.GetComponent<Collider>());
            }

        }

        static void FloorLamp(Transform parent, Vector3 position, Material metal, Material shade)
        {
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "LampPole";
            pole.transform.SetParent(parent, false);
            pole.transform.SetLocalPositionAndRotation(position + Vector3.up * 0.75f, Quaternion.identity);
            pole.transform.localScale = new Vector3(0.03f, 0.75f, 0.03f);
            pole.GetComponent<MeshRenderer>().sharedMaterial = metal;
            Destroy(pole.GetComponent<Collider>());

            var shadeGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shadeGo.name = "LampShade";
            shadeGo.transform.SetParent(parent, false);
            shadeGo.transform.SetLocalPositionAndRotation(position + Vector3.up * 1.6f, Quaternion.identity);
            shadeGo.transform.localScale = new Vector3(0.38f, 0.14f, 0.38f);
            shadeGo.GetComponent<MeshRenderer>().sharedMaterial = shade;
            shadeGo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Destroy(shadeGo.GetComponent<Collider>());

            var foot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            foot.name = "LampFoot";
            foot.transform.SetParent(parent, false);
            foot.transform.SetLocalPositionAndRotation(position + Vector3.up * 0.015f, Quaternion.identity);
            foot.transform.localScale = new Vector3(0.3f, 0.015f, 0.3f);
            foot.GetComponent<MeshRenderer>().sharedMaterial = metal;
        }

        protected override void Update()
        {
            base.Update();
            if (osd.text.Length > 0 && Time.time > osdUntil) osd.text = "";
            if (Player.Current is ScreenPlayer.State.Playing or ScreenPlayer.State.Paused)
            {
                var t = System.TimeSpan.FromSeconds(Player.PositionSeconds);
                vcrDisplay.text = $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
            }
            else vcrDisplay.text = Player.Current == ScreenPlayer.State.Idle && Mathf.Repeat(Time.time, 1f) > 0.5f ? "" : "12:00";
        }

        protected override void OnPlayerChanged()
        {
            base.OnPlayerChanged();
            var state = Player.Current;
            if (state == lastState) return;
            // The VCR's on-screen display, for a few seconds after each change.
            var label = state switch
            {
                ScreenPlayer.State.Playing => "PLAY",
                ScreenPlayer.State.Paused => "PAUSE",
                ScreenPlayer.State.Loading when lastState is ScreenPlayer.State.Playing or ScreenPlayer.State.Paused => "SEARCH",
                _ => null,
            };
            lastState = state;
            if (label == null) return;
            osd.text = label;
            osdUntil = Time.time + (state == ScreenPlayer.State.Paused ? 1e6f : 3f);
        }

        /// <summary>A grid of vertices bulging towards -Z in the middle, like the face of a picture tube.</summary>
        static Mesh CurvedScreen(float width, float height, float bulge, int columns = 32, int rows = 24)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (var y = 0; y <= rows; y++)
            for (var x = 0; x <= columns; x++)
            {
                var u = x / (float)columns;
                var v = y / (float)rows;
                var nx = u * 2 - 1;
                var ny = v * 2 - 1;
                vertices.Add(new Vector3(nx * width / 2, ny * height / 2, -bulge * (1 - nx * nx) * (1 - ny * ny)));
                uvs.Add(new Vector2(u, v));
            }
            for (var y = 0; y < rows; y++)
            for (var x = 0; x < columns; x++)
            {
                var i = y * (columns + 1) + x;
                // Clockwise as seen from -Z, the side facing the room.
                triangles.AddRange(new[] { i, i + columns + 1, i + 1, i + 1, i + columns + 1, i + columns + 2 });
            }
            var mesh = new Mesh { name = "CrtScreen" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
