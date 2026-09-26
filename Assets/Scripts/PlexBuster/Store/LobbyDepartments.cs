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

        async void Start()
        {
            var services = StoreServices.Instance;
            await services.WhenReady();
            if (this == null) return;

            try
            {
                var doors = services.Library.Sections
                    .Select(section => new DoorHall.Door(section.Title, pose => StoreNavigator.Instance.EnterSection(section, pose)))
                    .ToList();
                var hall = DoorHall.Build("Departments", theme, doors, null, transform.position, transform.rotation,
                    withExit: false, entranceSign: signText);
                hall.transform.SetParent(transform, true);
            }
            catch (OperationCanceledException) { }
        }
    }
}
