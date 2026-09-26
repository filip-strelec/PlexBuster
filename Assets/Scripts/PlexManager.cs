using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Xml.Linq;
using System.Linq;

#if UNITY_XR_INTERACTION_TOOLKIT
using UnityEngine.XR.Interaction.Toolkit;
#endif

public class PlexManager : MonoBehaviour
{
    [SerializeField] private GameObject dvdPrefab; // Cube with texture renderer
    [SerializeField] private Transform shelfTransform; // Assign a shelf GameObject in Inspector

    // Server and token come from PLEX_URL / PLEX_TOKEN (environment or .env), never from the scene.
    private string plexServerUrl;
    private string plexToken;

    void Start()
    {
        var config = PlexBuster.Data.PlexConfig.Load();
        if (!config.IsUsable)
        {
            Debug.LogWarning("PlexManager: PLEX_URL / PLEX_TOKEN are not set.");
            return;
        }
        plexServerUrl = config.ServerUrl.TrimEnd('/');
        plexToken = config.Token;
        StartCoroutine(FetchMovies());
    }

    IEnumerator FetchMovies()
    {
        string url = $"{plexServerUrl}/library/sections/1/all";
        Debug.Log("Requesting section 1 from Plex");

        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.SetRequestHeader("Accept", "application/xml");
            www.SetRequestHeader("X-Plex-Token", plexToken);
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Plex API Error: {www.error} - Response Code: {www.responseCode}");
                yield break;
            }

            try
            {
                XDocument doc = XDocument.Parse(www.downloadHandler.text);
                var videos = doc.Descendants("Video").Take(300); // Limit to first 400

                if (!videos.Any())
                {
                    Debug.LogWarning("No videos found in Plex response. Check section ID or library.");
                    yield break;
                }

                Debug.Log($"Processing {videos.Count()} movies.");

                int index = 0;
                foreach (var video in videos)
                {
                    string title = video.Attribute("title")?.Value ?? "Untitled";
                    // Sanitize title
                    title = System.Text.RegularExpressions.Regex.Replace(title, @"[^\u0020-\u007E]", "?");

                    // Construct poster URL using thumb
                    string posterUrl = null;
                    var thumb = video.Attribute("thumb");
                    if (thumb != null)
                    {
                        posterUrl = $"{plexServerUrl}{thumb.Value}";
                        StartCoroutine(LoadPoster(posterUrl, index, title));
                    }
                    else
                    {
                        Debug.LogWarning($"No thumb for movie: {title}");
                    }

                    index++;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"XML Parsing Error: {ex.Message}");
            }
        }
    }

    IEnumerator LoadPoster(string posterUrl, int index, string title)
    {
        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(posterUrl))
        {
            www.SetRequestHeader("X-Plex-Token", plexToken);
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
            {
                // Spawn DVD on shelf
                Vector3 position = shelfTransform.position + new Vector3(index * 0.3f, 0.3f, 0);
                GameObject dvd = Instantiate(dvdPrefab, position, Quaternion.identity);
                dvd.name = $"DVD_{title}";

                Texture2D texture = ((DownloadHandlerTexture)www.downloadHandler).texture;
                Renderer renderer = dvd.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.mainTexture = texture;
                }
                else
                {
                    Debug.LogWarning($"No Renderer on DVD prefab for {title}");
                }

#if UNITY_XR_INTERACTION_TOOLKIT
                // Make grabbable for VR
                var grab = dvd.AddComponent<XRGrabInteractable>();
                grab.movementType = XRGrabInteractable.MovementType.Instantaneous;
                grab.trackRotation = true;
#else
                Debug.LogWarning($"XRGrabInteractable not available for {title}. Install XR Interaction Toolkit or use SteamVR.");
                // Fallback: Add a basic collider for minimal interaction
                if (dvd.GetComponent<Collider>() == null)
                {
                    dvd.AddComponent<BoxCollider>();
                }
#endif
            }
            else
            {
                Debug.LogError($"Failed to load poster for {title}: {www.error}");
            }
        }
    }
}