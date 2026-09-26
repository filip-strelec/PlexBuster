using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PlexBuster.Data;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlexBuster.Store
{
    /// <summary>
    /// A room for watching a title (the TV room, the cinema): a screen fed by a <see cref="ScreenPlayer"/>, a tape
    /// slot that starts whatever goes in, controls, subtitles on the screen, room lights that dim while the film
    /// plays, and light from the picture spilling into the room. Subclasses build the room around it.
    /// Built at runtime, door at the origin, the room along +Z.
    /// </summary>
    public abstract class ScreeningRoom : MonoBehaviour, IStorePlace
    {
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int VideoAspectId = Shader.PropertyToID("_VideoAspect");
        static readonly int FillId = Shader.PropertyToID("_Fill");
        static readonly int StaticId = Shader.PropertyToID("_Static");
        static readonly int GlassId = Shader.PropertyToID("_Glass");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        readonly List<(Light Light, float Intensity)> houseLights = new();
        readonly List<(Light Light, float Intensity)> spills = new();
        readonly List<(Material Material, Color Colour, float Off)> glows = new();
        protected readonly List<Object> Owned = new();
        float dim = 1;
        SphericalHarmonicsL2 ambientProbe;
        Color ambientSky, ambientEquator, ambientGround;
        float reflections;
        bool ambientSaved;

        protected StoreTheme Theme;
        protected PosterCache Posters;
        protected ScreenPlayer Player;
        protected Material ScreenMaterial;
        protected TapeSlot Slot;
        protected TextMeshPro Subtitles;
        protected TextMeshPro Notice;
        protected Pose Entry;
        protected Color IdleGlass = Color.black;

        public DoorTrigger Exit { get; protected set; }
        public Pose EntryPose => new(transform.TransformPoint(Entry.position), transform.rotation * Entry.rotation);

        /// <summary>How far the room lights go down while the film plays (1 = not at all).</summary>
        protected abstract float DimmedTo { get; }

        /// <summary>
        /// How much of the store's ambient light reaches this room with its lights on, and while the film plays
        /// (the store's ambient light is bright; a cinema should get properly dark).
        /// </summary>
        protected abstract float AmbientLit { get; }
        protected abstract float AmbientDark { get; }

        /// <summary>What the screen says when nothing is playing.</summary>
        protected abstract string IdleNotice { get; }

        protected void Init(StoreTheme theme, PosterCache posters)
        {
            Theme = theme;
            Posters = posters;
            ambientProbe = RenderSettings.ambientProbe;
            ambientSky = RenderSettings.ambientSkyColor;
            ambientEquator = RenderSettings.ambientEquatorColor;
            ambientGround = RenderSettings.ambientGroundColor;
            reflections = RenderSettings.reflectionIntensity;
            ambientSaved = true;
        }

        /// <summary>
        /// Scales the scene's ambient light. It's global, but only one place is ever occupied: the lobby and halls
        /// get it back when this room is left (destroyed).
        /// </summary>
        void SetAmbient(float factor)
        {
            if (!ambientSaved) return;
            RenderSettings.ambientSkyColor = Scale(ambientSky, factor);
            RenderSettings.ambientEquatorColor = Scale(ambientEquator, factor);
            RenderSettings.ambientGroundColor = Scale(ambientGround, factor);
            RenderSettings.ambientProbe = ambientProbe * factor;
            RenderSettings.reflectionIntensity = reflections * factor;
        }

        protected void CreatePlayer(Vector3 speaker, float spatialBlend, float minDistance, float maxDistance)
        {
            var go = new GameObject("Player");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = speaker;
            var audio = go.AddComponent<AudioSource>();
            audio.spatialBlend = spatialBlend;
            audio.minDistance = minDistance;
            audio.maxDistance = maxDistance;
            audio.rolloffMode = AudioRolloffMode.Linear;
            audio.spread = 90;
            Player = go.AddComponent<ScreenPlayer>();
            Player.Setup(audio);
            Player.Changed += OnPlayerChanged;
        }

        /// <summary>A copy of the theme's screen material showing the player's picture.</summary>
        protected Material CreateScreenMaterial(Material template, float screenAspect)
        {
            ScreenMaterial = Own(new Material(template) { name = template.name + " (Room)" });
            ScreenMaterial.SetTexture(MainTexId, Player.Texture);
            ScreenMaterial.SetFloat("_ScreenAspect", screenAspect);
            return ScreenMaterial;
        }

        /// <summary>
        /// Subtitle text and the screen's notices ("put a tape in..."), a few millimetres in front of the screen
        /// plane so the eyes don't refocus between picture and text.
        /// </summary>
        protected void CreateScreenText(Transform screen, Vector2 screenSize, float subtitleSize, float noticeSize, Color noticeColour)
        {
            Subtitles = Signage.CreateText(screen, "Subtitles", new Vector3(0, -screenSize.y * 0.28f, -0.01f), Quaternion.identity,
                subtitleSize, Color.white, new Vector2(screenSize.x * 0.8f, screenSize.y * 0.36f), TextAlignmentOptions.Bottom);
            Subtitles.enableAutoSizing = false;
            Subtitles.textWrappingMode = TextWrappingModes.Normal;
            Subtitles.overflowMode = TextOverflowModes.Overflow;
            Subtitles.text = "";

            Notice = Signage.CreateText(screen, "Notice", new Vector3(0, 0, -0.012f), Quaternion.identity,
                noticeSize, noticeColour, new Vector2(screenSize.x * 0.85f, screenSize.y * 0.6f));
            Notice.textWrappingMode = TextWrappingModes.Normal;
        }

        protected void CreateSlot(TapeSlot slot)
        {
            Slot = slot;
            Slot.Inserted += tape => Player.Play(tape.Item);
            Slot.Removed += _ => Player.Stop();
        }

        protected void CreateControls(Transform parent, Vector3 localPosition, Quaternion localRotation, float scale)
        {
            var controls = PlaybackControls.Create(parent, localPosition, localRotation, Player, () =>
            {
                if (Slot.Tape != null) Slot.Eject();
                else Player.Stop();
            }, "Put a tape in the slot to watch it");
            controls.transform.localScale *= scale;
        }

        static Color Scale(Color colour, float factor) => new(colour.r * factor, colour.g * factor, colour.b * factor, colour.a);

        /// <summary>A room light that dims while the film plays.</summary>
        protected Light HouseLight(Transform parent, Vector3 position, Color colour, float intensity, float range)
        {
            var light = new GameObject("HouseLight").AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.transform.localPosition = position;
            light.type = LightType.Point;
            light.color = colour;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            houseLights.Add((light, intensity));
            return light;
        }

        /// <summary>An emissive material that dims with the room lights (lamp shades, sconces).</summary>
        /// <param name="off">How much of its glow is left with the room lights fully down.</param>
        protected Material Glow(string name, Color hdr, float off = 0.15f)
        {
            var material = Own(new Material(Theme.lightPanelMaterial) { name = name });
            material.SetColor(BaseColorId, hdr);
            glows.Add((material, hdr, off));
            return material;
        }

        /// <summary>Light cast by the picture: follows its average colour and brightness.</summary>
        protected Light Spill(Light light, float intensity)
        {
            light.shadows = LightShadows.None;
            light.intensity = 0;
            spills.Add((light, intensity));
            return light;
        }

        protected virtual void Update()
        {
            var target = Player.Current switch
            {
                ScreenPlayer.State.Playing => DimmedTo,
                ScreenPlayer.State.Loading => Mathf.Lerp(DimmedTo, 1, 0.5f),
                ScreenPlayer.State.Paused => Mathf.Lerp(DimmedTo, 1, 0.35f),
                _ => 1f,
            };
            dim = Mathf.MoveTowards(dim, target, Time.deltaTime / 2.5f);
            foreach (var (light, intensity) in houseLights)
            {
                light.intensity = intensity * dim;
                // Fully down, a light is switched off, not just black: it costs nothing while the film plays.
                light.enabled = dim > 0.001f;
            }
            foreach (var (material, colour, off) in glows) material.SetColor(BaseColorId, colour * Mathf.Lerp(off, 1f, dim));
            SetAmbient(Mathf.Lerp(AmbientDark, AmbientLit, (dim - DimmedTo) / Mathf.Max(0.01f, 1 - DimmedTo)));

            var picture = Player.AverageColor;
            var peak = Mathf.Max(picture.r, Mathf.Max(picture.g, picture.b));
            var luminance = picture.r * 0.3f + picture.g * 0.59f + picture.b * 0.11f;
            foreach (var (light, intensity) in spills)
            {
                light.color = peak > 0.01f ? picture / peak : Color.black;
                light.intensity = intensity * luminance;
            }

            ScreenMaterial.SetFloat(VideoAspectId, Player.VideoAspect);
            ScreenMaterial.SetFloat(FillId, Player.Fill ? 1 : 0);
        }

        protected virtual void OnPlayerChanged()
        {
            var idle = Player.Current == ScreenPlayer.State.Idle;
            ScreenMaterial.SetColor(GlassId, idle ? IdleGlass : Color.black);
            ScreenMaterial.SetFloat(StaticId, Player.Current == ScreenPlayer.State.Loading ? LoadingStatic : 0);

            Notice.text = Player.Current switch
            {
                ScreenPlayer.State.Idle => IdleNotice,
                ScreenPlayer.State.Loading => $"{Player.Title}\n<size=60%>{Player.Status}</size>",
                ScreenPlayer.State.Failed => $"{Player.Title}\n<size=60%>{Player.Status}</size>",
                _ => "",
            };

            // A translucent box behind the words keeps them readable on bright scenes.
            var text = Player.SubtitleText;
            Subtitles.text = string.IsNullOrEmpty(text) ? "" : $"<mark=#000000B0>{text.Replace("\n", "</mark>\n<mark=#000000B0>")}</mark>";
        }

        /// <summary>How much snow the screen shows while a title loads (a CRT's, not a cinema's).</summary>
        protected virtual float LoadingStatic => 0;

        /// <summary>Recently added titles on a small shelf, to pick from without going back to the store.</summary>
        protected async Task StockShelf(ShelfUnit unit, string label, int count)
        {
            var services = StoreServices.Instance;
            IReadOnlyList<LibraryItem> items;
            try
            {
                items = await services.Library.QueryAsync(new LibraryQuery { Filter = FilterType.RecentlyAdded, Limit = count }, services.LifetimeToken);
            }
            catch (System.Exception e) when (e is not System.OperationCanceledException)
            {
                Debug.LogWarning($"[Screening] Shelf: {e.Message}");
                return;
            }
            if (unit == null) return;
            unit.SetLabel(label, Theme.labelColor);
            foreach (var item in items.Take(unit.Capacity)) unit.AddTape(item, Theme.tapePrefab, Posters);
        }

        /// <summary>
        /// A <see cref="ReshelveButton"/> for <paramref name="unit"/> on a wall, the button facing
        /// <paramref name="outward"/> with its legend below.
        /// </summary>
        protected void ReshelveButtonOnWall(ShelfUnit unit, Vector3 wallPoint, Vector3 outward) =>
            ReshelveButton.Create(transform, wallPoint, Quaternion.LookRotation(Vector3.down, outward),
                () => unit != null ? unit.Reshelve(Theme.tapePrefab, Posters) : 0);

        protected ShelfUnit Shelf(Transform parent, string name, Vector3 position, Quaternion rotation, int columns, int rows)
        {
            var unit = new GameObject(name).AddComponent<ShelfUnit>();
            unit.transform.SetParent(parent, false);
            unit.transform.SetLocalPositionAndRotation(position, rotation);
            unit.Configure(columns, rows, Theme.shelfMaterial, Theme.shelfAccentMaterial);
            unit.EnsureBuilt();
            return unit;
        }

        /// <summary>A box without a collider or shadow: trim, glowing strips, details.</summary>
        protected static GameObject Trim(Transform parent, string name, Vector3 centre, Vector3 size, Material material)
        {
            var box = Signage.Box(parent, name, centre, size, material);
            Destroy(box.GetComponent<Collider>());
            box.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return box;
        }

        protected Material Lit(string name, Color colour, float smoothness, float metallic = 0)
        {
            var material = Own(new Material(Theme.doorFrameMaterial) { name = name });
            material.SetColor(BaseColorId, colour);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            return material;
        }

        protected Material Unlit(string name, Color colour)
        {
            var material = Own(new Material(Theme.lightPanelMaterial) { name = name });
            material.SetColor(BaseColorId, colour);
            return material;
        }

        protected T Own<T>(T obj) where T : Object
        {
            Owned.Add(obj);
            return obj;
        }

        protected virtual void OnDestroy()
        {
            SetAmbient(1);
            if (Player != null) Player.Changed -= OnPlayerChanged;
            foreach (var o in Owned)
                if (o != null) Destroy(o);
        }
    }
}
