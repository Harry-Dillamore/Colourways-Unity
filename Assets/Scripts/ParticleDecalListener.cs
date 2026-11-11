using System.Collections.Generic;
using UnityEngine;

public class ParticleDecalListener : MonoBehaviour
{
    private ParticleSystem particleSys;
    private List<ParticleCollisionEvent> collisionEvents = new List<ParticleCollisionEvent>();
    public Color ParticleColor { get; set; } = Color.white;
    public LayerMask collisionMask = ~0; // Default to Everything

    private void Awake()
    {
        particleSys = GetComponent<ParticleSystem>();
    }

    private void OnParticleCollision(GameObject other)
    {
        if (PaintDecalSpawner.Instance == null || particleSys == null) return;

        int numCollisions = particleSys.GetCollisionEvents(other, collisionEvents);

        for (int i = 0; i < numCollisions; i++)
        {
            Vector3 hitPoint = collisionEvents[i].intersection;
            Vector3 hitNormal = collisionEvents[i].normal;
            float radius = Random.Range(0.15f, 0.4f);

            PaintDecalSpawner.Instance.SpawnDecal(hitPoint, hitNormal, radius, ParticleColor);
        }
    }

    private bool IsSurfaceSupported(Vector3 center, Vector3 normal, float radius)
    {
        Vector3 right = Vector3.Cross(normal, Vector3.up);
        if (right.sqrMagnitude < 0.01f) right = Vector3.Cross(normal, Vector3.right);
        right.Normalize();
        Vector3 up = Vector3.Cross(normal, right).normalized;

        // Sample 4 cardinal points around quad boundaries
        Vector3[] checkPoints = new Vector3[]
        {
            center + right * radius,
            center - right * radius,
            center + up * radius,
            center - up * radius
        };

        foreach (var p in checkPoints)
        {
            // Cast ray slightly behind surface normal to verify physical mesh exists
            if (!Physics.Raycast(p + normal * 0.05f, -normal, 0.15f, collisionMask))
            {
                return false; // Corner hangs past mesh edge
            }
        }

        return true;
    }
}