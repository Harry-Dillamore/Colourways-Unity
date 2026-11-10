using System.Collections;
using UnityEngine;

[RequireComponent(typeof(PlayerController))]
public class PaintExplosionSystem : MonoBehaviour
{
    [Header("Explosion Tuning")]
    [Tooltip("Number of paint rays cast into the surrounding walls and floor on impact.")]
    [Range(10, 100)]
    public int splatterRayCount = 45;
    [Tooltip("Max radius in meters the paint can spray out to hit walls/floor.")]
    public float maxSprayDistance = 6.0f;
    [Tooltip("Radius of the main central impact splat directly on the wall.")]
    public float mainSplatRadius = 1.3f;
    [Tooltip("Radius of the puddle splat placed directly on the floor.")]
    public float floorPuddleRadius = 1.4f;
    [Tooltip("Minimum droplet size for sprayed splatters.")]
    public float minDropletRadius = 0.2f;
    [Tooltip("Maximum droplet size for sprayed splatters.")]
    public float maxDropletRadius = 0.65f;

    [Header("Colors")]
    [Tooltip("Vibrant paint colors for explosions. The player cycles through these on each explosion.")]
    public Color[] paintColors = new Color[]
    {
        new Color(1.00f, 0.05f, 0.55f), // Neon Magenta
        new Color(0.00f, 0.90f, 1.00f), // Electric Cyan
        new Color(0.20f, 1.00f, 0.15f), // Lime Green
        new Color(1.00f, 0.45f, 0.00f), // Vibrant Orange
        new Color(0.65f, 0.10f, 1.00f), // Neon Violet
        new Color(1.00f, 0.90f, 0.00f), // Vivid Yellow
        new Color(1.00f, 0.15f, 0.25f)  // Crimson Red
    };

    [Header("Respawn & Camera")]
    [Tooltip("Time in seconds before the player respawns after exploding.")]
    public float respawnDelay = 1.2f;
    [Tooltip("Respawn position. Defaults to starting position if left at (0,0,0).")]
    public Vector3 spawnPosition = Vector3.zero;
    [Tooltip("Duration of camera shake on impact.")]
    public float cameraShakeDuration = 0.25f;
    [Tooltip("Intensity of camera shake on impact.")]
    public float cameraShakeMagnitude = 0.18f;

    [Header("Audio (Optional)")]
    public AudioClip explosionSound;

    public bool IsExploding { get; private set; } = false;
    public Color CurrentPaintColor => paintColors[colorIndex % paintColors.Length];

    private int colorIndex = 0;
    private PlayerController playerController;
    private CharacterController characterController;
    private Renderer[] playerRenderers;
    private ParticleSystem proceduralParticles;
    private Material playerMaterialInstance;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        characterController = GetComponent<CharacterController>();
        playerRenderers = GetComponentsInChildren<Renderer>(true);

        if (spawnPosition == Vector3.zero)
        {
            spawnPosition = transform.position;
        }

        colorIndex = Random.Range(0, paintColors.Length);
        SetupPlayerColorVisual();
        SetupProceduralParticleSystem();
    }

    private void SetupPlayerColorVisual()
    {
        // Give player a tinted material so they match the paint color
        Renderer mainRenderer = GetComponent<Renderer>();
        if (mainRenderer != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            if (shader != null)
            {
                playerMaterialInstance = new Material(shader)
                {
                    name = "PlayerPaintMat"
                };
                playerMaterialInstance.color = CurrentPaintColor;
                mainRenderer.material = playerMaterialInstance;
            }
        }
    }

    private void SetupProceduralParticleSystem()
    {
        GameObject pObj = new GameObject("PaintBurstParticles");
        pObj.transform.SetParent(transform);
        pObj.transform.localPosition = Vector3.zero;

        proceduralParticles = pObj.AddComponent<ParticleSystem>();
        var main = proceduralParticles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 16f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
        main.gravityModifier = 1.8f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = proceduralParticles.emission;
        emission.enabled = false;

        var shape = proceduralParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.3f;

        var pRenderer = pObj.GetComponent<ParticleSystemRenderer>();
        Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (particleShader == null) particleShader = Shader.Find("Particles/Standard Unlit");
        if (particleShader != null)
        {
            pRenderer.material = new Material(particleShader);
        }
    }

    /// <summary>
    /// Triggers the full paint explosion: generates permanent splatters, bursts particles, hides player, and respawns.
    /// </summary>
    public void Explode(Vector3 impactPoint, Vector3 impactNormal, Vector3 moveDirection)
    {
        if (IsExploding) return;
        StartCoroutine(ExplosionRoutine(impactPoint, impactNormal, moveDirection));
    }

    private IEnumerator ExplosionRoutine(Vector3 impactPoint, Vector3 impactNormal, Vector3 moveDirection)
    {
        IsExploding = true;
        Color explosionColor = CurrentPaintColor;

        // 1. Permanently add splatters to PaintDecalBatcher
        GeneratePaintSplodges(impactPoint, impactNormal, explosionColor);

        // 2. Play particle burst
        if (proceduralParticles != null)
        {
            var main = proceduralParticles.main;
            main.startColor = explosionColor;
            proceduralParticles.transform.position = impactPoint + impactNormal * 0.1f;
            proceduralParticles.Emit(Random.Range(50, 80));
        }

        // 3. Play optional audio
        if (explosionSound != null)
        {
            AudioSource.PlayClipAtPoint(explosionSound, impactPoint, 1.0f);
        }

        // 4. Trigger camera shake if camera exists
        if (playerController != null && playerController.playerCamera != null)
        {
            StartCoroutine(CameraShakeRoutine(playerController.playerCamera));
        }

        // 5. Hide player visual and disable character controller movement
        SetPlayerVisualVisible(false);
        characterController.enabled = false;

        // 6. Wait for respawn delay
        yield return new WaitForSeconds(respawnDelay);

        // 7. Respawn player at spawn position
        transform.position = spawnPosition;
        characterController.enabled = true;

        // 8. Cycle to next vibrant color
        colorIndex = (colorIndex + 1) % paintColors.Length;
        if (playerMaterialInstance != null)
        {
            playerMaterialInstance.color = CurrentPaintColor;
        }

        SetPlayerVisualVisible(true);
        IsExploding = false;
    }

    private void GeneratePaintSplodges(Vector3 impactPoint, Vector3 impactNormal, Color color)
    {
        PaintDecalBatcher batcher = PaintDecalBatcher.Instance;
        if (batcher == null) return;

        Color32 col32 = color;

        // A. Primary Impact Splodge on the wall
        batcher.AddSplatter(impactPoint, impactNormal, mainSplatRadius * Random.Range(0.9f, 1.2f), col32, 0);

        // B. Floor Puddle directly below player
        Vector3 floorOrigin = transform.position + Vector3.up * 0.2f;
        if (Physics.Raycast(floorOrigin, Vector3.down, out RaycastHit floorHit, 4.0f))
        {
            batcher.AddSplatter(floorHit.point, floorHit.normal, floorPuddleRadius * Random.Range(0.85f, 1.25f), col32, 3);
        }

        // C. Radial Spray onto surrounding walls, ceiling and floor
        Vector3 blastOrigin = impactPoint + impactNormal * 0.15f;
        int raysCast = splatterRayCount;

        for (int i = 0; i < raysCast; i++)
        {
            // Hemispherical / spherical spray directions favoring bounce normal and downward spread
            Vector3 randomDir = Random.onUnitSphere;

            // Bias towards bounce normal (reflection of impact)
            if (Vector3.Dot(randomDir, impactNormal) < -0.2f)
            {
                randomDir = Vector3.Reflect(randomDir, impactNormal);
            }

            // Also add downward bias so floor gets abundant splashes
            if (Random.value < 0.45f)
            {
                randomDir = Vector3.Lerp(randomDir, Vector3.down, Random.Range(0.3f, 0.8f)).normalized;
            }

            if (Physics.Raycast(blastOrigin, randomDir, out RaycastHit hit, maxSprayDistance))
            {
                // Don't splat on player itself
                if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                // Size decreases with distance from impact
                float distanceRatio = 1.0f - (hit.distance / maxSprayDistance);
                float radius = Mathf.Lerp(minDropletRadius, maxDropletRadius, distanceRatio) * Random.Range(0.7f, 1.3f);

                // Slight color tint variance (+/- 8%) for visual realism
                Color32 dropletColor = col32;
                float tintVar = Random.Range(-0.06f, 0.06f);
                dropletColor.r = (byte)Mathf.Clamp(col32.r + (tintVar * 255f), 0, 255);
                dropletColor.g = (byte)Mathf.Clamp(col32.g + (tintVar * 255f), 0, 255);
                dropletColor.b = (byte)Mathf.Clamp(col32.b + (tintVar * 255f), 0, 255);

                batcher.AddSplatter(hit.point, hit.normal, radius, dropletColor);
            }
        }

        // Commit all splodges to the batched mesh in one efficient call (1 draw call total!)
        batcher.Commit();
    }

    private void SetPlayerVisualVisible(bool visible)
    {
        for (int i = 0; i < playerRenderers.Length; i++)
        {
            if (playerRenderers[i] != null)
            {
                playerRenderers[i].enabled = visible;
            }
        }
    }

    private IEnumerator CameraShakeRoutine(Transform cam)
    {
        Vector3 originalLocalPos = cam.localPosition;
        float elapsed = 0f;

        while (elapsed < cameraShakeDuration)
        {
            float damper = 1.0f - (elapsed / cameraShakeDuration);
            Vector3 offset = Random.insideUnitSphere * (cameraShakeMagnitude * damper);
            cam.localPosition = originalLocalPos + offset;
            elapsed += Time.deltaTime;
            yield return null;
        }

        cam.localPosition = originalLocalPos;
    }
}
