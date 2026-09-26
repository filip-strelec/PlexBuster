using System;
using System.Linq;
using UnityEngine;

namespace PlexBuster.Store
{
    /// <summary>
    /// The corridor behind the lobby with one door per library section ("department"): Movies, TV Shows,
    /// and every other section. Each door leads to that section's own hall of genre rooms.
    /// Sits at the lobby opening; the corridor runs along this object's +Z.
    /// </summary>
    public class LobbyDepartments : MonoBehaviour
    {
        [SerializeField] StoreTheme theme;
        [SerializeField] string signText = "Departments";
        [SerializeField, Tooltip("Section titles to put first, in this order; the rest follow in library order.")]
        string[] firstSections = { "Movies", "TV Shows", "exYu" };

        async void Start()
        {
            var services = StoreServices.Instance;
            await services.WhenReady();
            if (this == null) return;

            try
            {
                var doors = services.Library.Sections
                    .Select((section, index) => (section, index))
                    .OrderBy(s => Priority(s.section.Title))
                    .ThenBy(s => s.index)
                    .Select(s => new DoorHall.Door(s.section.Title, pose => StoreNavigator.Instance.EnterSection(s.section, pose)))
                    .ToList();
                var hall = DoorHall.Build("Departments", theme, doors, null, transform.position, transform.rotation,
                    withExit: false, entranceSign: signText);
                hall.transform.SetParent(transform, true);
            }
            catch (OperationCanceledException) { }
        }

        int Priority(string title)
        {
            var i = Array.FindIndex(firstSections, s => string.Equals(s, title, StringComparison.OrdinalIgnoreCase));
            return i < 0 ? firstSections.Length : i;
        }
    }
}
