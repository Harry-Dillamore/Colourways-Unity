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
    private ParticleDecalListener particleDecalListener;
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

    private void OnDestroy()
    {
        if (playerMaterialInstance != null)
        {
            Destroy(playerMaterialInstance);
        }
    }

    private void SetupPlayerColorVisual()
    {
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
        particleDecalListener = pObj.AddComponent<ParticleDecalListener>();

        proceduralParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

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

        var collision = proceduralParticles.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.sendCollisionMessages = true;
        collision.bounce = 0.0f;
        collision.lifetimeLoss = 1.0f;

        var pRenderer = pObj.GetComponent<ParticleSystemRenderer>();
        Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (particleShader == null) particleShader = Shader.Find("Particles/Standard Unlit");
        if (particleShader != null)
        {
            pRenderer.material = new Material(particleShader);
        }
    }

    public void Explode(Vector3 impactPoint, Vector3 impactNormal, Vector3 moveDirection)
    {
        if (IsExploding) return;
        StartCoroutine(ExplosionRoutine(impactPoint, impactNormal, moveDirection));
    }

    private IEnumerator ExplosionRoutine(Vector3 impactPoint, Vector3 impactNormal, Vector3 moveDirection)
    {
        IsExploding = true;
        Color explosionColor = CurrentPaintColor;

        // 1. Spawn corner-wrapping volume decals via PaintDecalSpawner
        GeneratePaintSplodges(impactPoint, impactNormal, explosionColor);

        // 2. Play particle burst & pass color to collision listener
        if (proceduralParticles != null)
        {
            if (particleDecalListener != null)
            {
                particleDecalListener.ParticleColor = explosionColor;
            }

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

        // 4. Trigger camera shake
        if (playerController != null && playerController.playerCamera != null)
        {
            StartCoroutine(CameraShakeRoutine(playerController.playerCamera));
        }

        // 5. Hide player visual and disable movement
        SetPlayerVisualVisible(false);
        if (characterController != null)
        {
            characterController.enabled = false;
        }

        // 6. Wait for respawn delay
        yield return new WaitForSeconds(respawnDelay);

        // 7. Teleport & sync physics before enabling controller
        transform.position = spawnPosition;
        Physics.SyncTransforms();

        if (playerController != null)
        {
            playerController.ResetMovementState();
        }

        if (characterController != null)
        {
            characterController.enabled = true;
        }

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
        PaintDecalSpawner spawner = PaintDecalSpawner.Instance;
        if (spawner == null) return;

        // Primary Impact Splodge on wall
        spawner.SpawnDecal(impactPoint, impactNormal, mainSplatRadius * Random.Range(0.9f, 1.2f), color);

        // Floor Puddle directly below player
        Vector3 floorOrigin = transform.position + Vector3.up * 0.2f;
        if (Physics.Raycast(floorOrigin, Vector3.down, out RaycastHit floorHit, 4.0f))
        {
            spawner.SpawnDecal(floorHit.point, floorHit.normal, floorPuddleRadius * Random.Range(0.85f, 1.25f), color);
        }

        // Radial Raycast Spray onto surrounding surfaces
        Vector3 blastOrigin = impactPoint + impactNormal * 0.15f;
        int raysCast = splatterRayCount;

        for (int i = 0; i < raysCast; i++)
        {
            Vector3 randomDir = Random.onUnitSphere;

            if (Vector3.Dot(randomDir, impactNormal) < -0.2f)
            {
                randomDir = Vector3.Reflect(randomDir, impactNormal);
            }

            if (Random.value < 0.45f)
            {
                randomDir = Vector3.Lerp(randomDir, Vector3.down, Random.Range(0.3f, 0.8f)).normalized;
            }

            if (Physics.Raycast(blastOrigin, randomDir, out RaycastHit hit, maxSprayDistance))
            {
                if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                float distanceRatio = 1.0f - (hit.distance / maxSprayDistance);
                float radius = Mathf.Lerp(minDropletRadius, maxDropletRadius, distanceRatio) * Random.Range(0.7f, 1.3f);

                Color dropletColor = color;
                float tintVar = Random.Range(-0.06f, 0.06f);
                dropletColor.r = Mathf.Clamp01(color.r + tintVar);
                dropletColor.g = Mathf.Clamp01(color.g + tintVar);
                dropletColor.b = Mathf.Clamp01(color.b + tintVar);

                spawner.SpawnDecal(hit.point, hit.normal, radius, dropletColor);
            }
        }
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
        Vector3 basePos = cam.localPosition;
        float elapsed = 0f;

        while (elapsed < cameraShakeDuration)
        {
            float damper = 1.0f - (elapsed / cameraShakeDuration);
            cam.localPosition = basePos + Random.insideUnitSphere * (cameraShakeMagnitude * damper);
            elapsed += Time.deltaTime;
            yield return null;
        }

        cam.localPosition = basePos;
    }
}