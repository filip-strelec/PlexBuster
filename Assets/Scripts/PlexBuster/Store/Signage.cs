using TMPro;
using UnityEngine;

namespace PlexBuster.Store
{
    /// <summary>Small helpers for building graybox geometry and 3D text from code.</summary>
    public static class Signage
    {
        static Material singleSidedText;

        /// <summary>
        /// World-space text. TextMeshPro reads from its -Z side, so pass a rotation whose -Z points at the viewer.
        /// A font size of 1 gives capitals roughly 7 cm tall. Text shrinks (down to a third) to fit <paramref name="size"/>.
        /// </summary>
        /// <param name="material">Optional TextMeshPro material (e.g. a glowing sign preset); by default a single-sided copy of the font's.</param>
        public static TextMeshPro CreateText(Transform parent, string name, Vector3 localPosition, Quaternion localRotation,
            float fontSize, Color color, Vector2 size, TextAlignmentOptions alignment = TextAlignmentOptions.Center,
            Material material = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(localPosition, localRotation);

            var text = go.AddComponent<TextMeshPro>();
            text.rectTransform.sizeDelta = size;
            text.fontSize = fontSize;
            text.enableAutoSizing = true;
            text.fontSizeMax = fontSize;
            text.fontSizeMin = fontSize / 3;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            if (material != null) text.fontSharedMaterial = material;
            // Only at runtime: a material created here can't be saved into a scene.
            else if (Application.isPlaying) text.fontSharedMaterial = SingleSided(text.fontSharedMaterial);
            return text;
        }

        /// <summary>
        /// TextMeshPro's default material is double-sided, so labels on back-to-back shelves show through
        /// mirrored. Signs only ever need their front.
        /// </summary>
        static Material SingleSided(Material fontMaterial)
        {
            if (singleSidedText == null)
            {
                singleSidedText = new Material(fontMaterial) { name = fontMaterial.name + " (Single Sided)" };
                singleSidedText.SetFloat("_CullMode", (float)UnityEngine.Rendering.CullMode.Back);
            }
            return singleSidedText;
        }

        /// <summary>
        /// A double-sided sign board sticking out of a wall, readable from both directions along the wall.
        /// Origin is where it meets the wall; +Z points out of the wall.
        /// </summary>
        public static void CreateBladeSign(Transform parent, string text, Vector3 localPosition, Quaternion localRotation,
            Material boardMaterial, Color textColor, Material textMaterial = null)
        {
            const float length = 0.9f, height = 0.32f, thickness = 0.04f;
            var sign = new GameObject("BladeSign").transform;
            sign.SetParent(parent, false);
            sign.SetLocalPositionAndRotation(localPosition, localRotation);

            // The board lies in the YZ plane, so its faces point along the wall (±X).
            var board = Box(sign, "Board", new Vector3(0, 0, length / 2 + 0.03f), new Vector3(thickness, height, length), boardMaterial);
            Object.Destroy(board.GetComponent<Collider>());
            for (var side = -1; side <= 1; side += 2)
            {
                // Text -Z must face the viewer on this side (±X).
                var rotation = Quaternion.LookRotation(new Vector3(-side, 0, 0));
                CreateText(sign, "Text", new Vector3(side * (thickness / 2 + 0.002f), 0, length / 2 + 0.03f), rotation,
                    1.3f, textColor, new Vector2(length - 0.08f, height - 0.06f), material: textMaterial).text = text;
            }
        }

        public static GameObject Box(Transform parent, string name, Vector3 centre, Vector3 size, Material material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.SetLocalPositionAndRotation(centre, Quaternion.identity);
            box.transform.localScale = size;
            if (material != null) box.GetComponent<MeshRenderer>().sharedMaterial = material;
            return box;
        }
    }
}
