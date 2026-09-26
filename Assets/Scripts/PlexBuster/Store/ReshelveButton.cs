using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PlexBuster.Store
{
    /// <summary>
    /// A big red button that puts a room's tapes back on its shelves: ones dropped on the floor, pushed into a
    /// shelf or fallen out of reach. Tapes in a hand, the basket or a player stay where they are. Poke it with a
    /// controller tip or point at it and click; its legend says how many tapes came back.
    /// Button space: a plate in XZ with the button on its +Y face; its user stands towards +Z.
    /// </summary>
    public class ReshelveButton : MonoBehaviour
    {
        const float PlateWidth = 0.14f;
        const float PlateDepth = 0.2f;
        const float PlateThickness = 0.02f;
        const float CapDiameter = 0.07f;
        const float CapHeight = 0.024f;
        const float Travel = 0.012f;
        const string Prompt = "<size=65%>LOST A TAPE?</size>\nRE-SHELVE ALL";

        Func<int> reshelve;
        Transform cap;
        Vector3 capRest;
        TextMeshPro legend;
        float pressedAt = float.NegativeInfinity;

        /// <param name="reshelve">Puts the tapes back and says how many moved.</param>
        public static ReshelveButton Create(Transform parent, Vector3 localPosition, Quaternion localRotation, Func<int> reshelve)
        {
            var root = new GameObject("ReshelveButton");
            root.transform.SetParent(parent, false);
            root.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            var button = root.AddComponent<ReshelveButton>();
            button.reshelve = reshelve;
            button.Build();
            return button;
        }

        void Build()
        {
            var plate = StoreMaterials.Lit("ReshelvePlate", new Color(0.1f, 0.1f, 0.11f), 0.4f);
            var red = StoreMaterials.Lit("ReshelveCap", new Color(0.8f, 0.06f, 0.04f), 0.7f, new Color(0.35f, 0.02f, 0.01f));
            Signage.Box(transform, "Plate", new Vector3(0, PlateThickness / 2, 0), new Vector3(PlateWidth, PlateThickness, PlateDepth), plate);

            // The button on the far half of the plate, the legend on the near half.
            var button = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            button.name = "Cap";
            Destroy(button.GetComponent<Collider>());
            cap = button.transform;
            cap.SetParent(transform, false);
            capRest = new Vector3(0, PlateThickness + CapHeight / 2, -0.045f);
            cap.localPosition = capRest;
            cap.localScale = new Vector3(CapDiameter, CapHeight / 2, CapDiameter); // a unit cylinder is 2 tall
            button.GetComponent<MeshRenderer>().sharedMaterial = red;

            legend = Signage.CreateText(transform, "Legend", new Vector3(0, PlateThickness + 0.0006f, 0.055f),
                Quaternion.LookRotation(Vector3.down, Vector3.back), 0.16f, new Color(1f, 0.85f, 0.3f),
                new Vector2(PlateWidth - 0.014f, 0.075f));
            legend.fontStyle = FontStyles.Bold;
            legend.text = Prompt;

            // An invisible UI panel over the whole plate: the XR UI pipeline handles poke and ray clicks.
            var hit = WorldUI.CreateCanvas(transform, "Hit", new Vector2(PlateWidth * 1000, PlateDepth * 1000),
                new Vector3(0, PlateThickness + CapHeight + 0.002f, 0), Quaternion.LookRotation(Vector3.down, Vector3.back));
            hit.GetComponent<Image>().color = Color.clear;
            var click = hit.gameObject.AddComponent<Button>();
            click.transition = Selectable.Transition.None;
            click.onClick.AddListener(Press);
        }

        void Press()
        {
            // A poke can register twice.
            if (Time.time - pressedAt < 0.6f) return;
            pressedAt = Time.time;
            StopAllCoroutines();
            StartCoroutine(Show(reshelve()));
        }

        IEnumerator Show(int count)
        {
            cap.localPosition = capRest + Vector3.down * Travel;
            legend.text = count switch
            {
                0 => "<size=65%>ALL TAPES ARE</size>\nIN PLACE",
                1 => "<size=65%>1 TAPE</size>\nBACK ON THE SHELF",
                _ => $"<size=65%>{count} TAPES</size>\nBACK ON THE SHELVES",
            };
            yield return new WaitForSeconds(0.15f);
            cap.localPosition = capRest;
            yield return new WaitForSeconds(2.5f);
            legend.text = Prompt;
        }
    }
}
