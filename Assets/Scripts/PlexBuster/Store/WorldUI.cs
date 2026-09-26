using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace PlexBuster.Store
{
    /// <summary>
    /// Builds world-space UI from code. Canvases use 1 unit = 1 mm, so sizes and font sizes read as millimetres.
    /// XR controllers interact through <see cref="TrackedDeviceGraphicRaycaster"/> (ray and poke).
    /// </summary>
    public static class WorldUI
    {
        public static readonly Color Panel = new(0.05f, 0.07f, 0.2f, 0.95f);
        public static readonly Color ButtonNormal = new(0.1f, 0.18f, 0.55f, 1f);
        public static readonly Color ButtonSelected = new(1f, 0.8f, 0.1f, 1f);
        public static readonly Color Text = new(1f, 0.92f, 0.7f, 1f);
        public static readonly Color TextOnSelected = new(0.05f, 0.07f, 0.2f, 1f);

        /// <summary>A canvas whose readable side faces the -Z of <paramref name="localRotation"/>, like TextMeshPro.</summary>
        public static RectTransform CreateCanvas(Transform parent, string name, Vector2 sizeMm, Vector3 localPosition, Quaternion localRotation)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.SetLocalPositionAndRotation(localPosition, localRotation);
            rect.sizeDelta = sizeMm;
            rect.localScale = Vector3.one * 0.001f;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            go.AddComponent<TrackedDeviceGraphicRaycaster>();
            go.AddComponent<Image>().color = Panel;
            return rect;
        }

        /// <summary>A child rect anchored by fractions of the parent, inset by <paramref name="padding"/> mm.</summary>
        public static RectTransform Area(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, float padding = 0)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
            return rect;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, float size, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var label = new GameObject("Label", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent, false);
            var rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            label.text = text;
            label.fontSize = size;
            label.enableAutoSizing = true;
            label.fontSizeMax = size;
            label.fontSizeMin = size / 3;
            label.color = Text;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        public static Button Button(Transform parent, string text, float fontSize, Action onClick)
        {
            var go = new GameObject($"Button: {text}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = Color.white;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            SetColors(button, false);
            button.onClick.AddListener(() => onClick());

            var label = Label(go.transform, text, fontSize);
            label.rectTransform.offsetMin = new Vector2(8, 4);
            label.rectTransform.offsetMax = new Vector2(-8, -4);
            return button;
        }

        public static void SetText(Button button, string text) => button.GetComponentInChildren<TextMeshProUGUI>().text = text;

        /// <summary>Shows a button as the current choice (yellow) or a normal one (blue).</summary>
        public static void SetSelected(Button button, bool selected)
        {
            SetColors(button, selected);
            // A Selectable only re-tints on its next state change; apply the new normal colour now.
            button.targetGraphic.CrossFadeColor(button.colors.normalColor * button.colors.colorMultiplier, 0f, true, true);
            button.GetComponentInChildren<TextMeshProUGUI>().color = selected ? TextOnSelected : Text;
        }

        static void SetColors(Button button, bool selected)
        {
            var baseColor = selected ? ButtonSelected : ButtonNormal;
            var colors = button.colors;
            colors.normalColor = baseColor;
            colors.selectedColor = baseColor;
            colors.highlightedColor = Color.Lerp(baseColor, Color.white, 0.3f);
            colors.pressedColor = Color.Lerp(baseColor, Color.black, 0.3f);
            colors.disabledColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0.3f);
            button.colors = colors;
        }

        public static GridLayoutGroup Grid(RectTransform area, Vector2 cellMm, Vector2 spacingMm, int columns)
        {
            var grid = area.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = cellMm;
            grid.spacing = spacingMm;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.childAlignment = TextAnchor.UpperCenter;
            return grid;
        }
    }
}
