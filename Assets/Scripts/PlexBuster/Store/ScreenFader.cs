using System.Threading.Tasks;
using UnityEngine;

namespace PlexBuster.Store
{
    /// <summary>Fades the view to black and back with a quad just in front of the camera, drawn last.</summary>
    public class ScreenFader : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        MeshRenderer quad;
        Material material;
        float alpha;

        public static ScreenFader Create(Camera camera, Material template)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "ScreenFader";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(camera.transform, false);
            // Close enough to cover everything, far enough to survive the near clip plane.
            go.transform.SetLocalPositionAndRotation(new Vector3(0, 0, camera.nearClipPlane + 0.03f), Quaternion.identity);
            go.transform.localScale = new Vector3(2f, 2f, 1f);

            var fader = go.AddComponent<ScreenFader>();
            fader.quad = go.GetComponent<MeshRenderer>();
            fader.quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fader.quad.receiveShadows = false;
            fader.material = new Material(template);
            fader.quad.sharedMaterial = fader.material;
            fader.Apply();
            return fader;
        }

        void OnDestroy() => Destroy(material);

        public async Task FadeTo(float target, float duration)
        {
            while (!Mathf.Approximately(alpha, target))
            {
                alpha = duration > 0 ? Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime / duration) : target;
                Apply();
                await Awaitable.NextFrameAsync();
                if (this == null) return;
            }
        }

        void Apply()
        {
            material.SetColor(BaseColorId, new Color(0, 0, 0, alpha));
            quad.enabled = alpha > 0.001f;
        }
    }
}
