using UnityEngine;

namespace ColourWays.Level
{
    /// <summary>
    /// Where the player (re)spawns. Port of Unreal's PlayerStart.
    /// The player respawns here when holding Restart or falling below the kill height.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpawnPoint : MonoBehaviour
    {
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            Vector3 p = transform.position;
            Gizmos.DrawWireSphere(p + Vector3.up * 0.5f, 0.4f);
            Gizmos.DrawLine(p + Vector3.up * 0.5f, p + Vector3.up * 0.5f + transform.forward * 0.9f);
            Gizmos.DrawWireCube(p, new Vector3(0.8f, 0.02f, 0.8f));
        }
    }
}
