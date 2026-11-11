using UnityEngine;
using UnityEngine.Rendering.Universal;

public class PaintDecalSpawner : MonoBehaviour
{
    private static PaintDecalSpawner instance;
    public static PaintDecalSpawner Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<PaintDecalSpawner>();
                if (instance == null)
                {
                    GameObject go = new GameObject("[PaintDecalSpawner]");
                    instance = go.AddComponent<PaintDecalSpawner>();
                }
            }
            return instance;
        }
    }

    [Header("Decal Setup")]
    public Material decalMaterial;
    [Tooltip("Depth of the projection volume box. Higher values wrap deeper over corners.")]
    public float projectionDepth = 0.8f;
    [Tooltip("Layer mask for geometry that accepts paint decals.")]
    public LayerMask targetLayers = ~0;

    /// <summary>
    /// Spawns a 3D decal projector box aligned to the surface impact normal.
    /// </summary>
    public void SpawnDecal(Vector3 hitPoint, Vector3 hitNormal, float size, Color color)
    {
        if (decalMaterial == null)
        {
            Debug.LogWarning("PaintDecalSpawner: No Decal Material assigned!");
            return;
        }

        GameObject decalObj = new GameObject("PaintDecal_Volume");
        decalObj.transform.position = hitPoint;
        
        // Orient local Z-axis (forward) pointing INTO the surface
        decalObj.transform.rotation = Quaternion.LookRotation(-hitNormal, Vector3.up);
        
        // Add random roll around projection axis for visual variety
        decalObj.transform.Rotate(Vector3.forward, Random.Range(0f, 360f), Space.Self);

        DecalProjector projector = decalObj.AddComponent<DecalProjector>();
        projector.material = decalMaterial;
        
        // Size = (Width, Height, Projection Depth)
        // A deeper projection depth allows the volume to clip and wrap over adjacent edges/corners
        projector.size = new Vector3(size, size, projectionDepth);

        // Instance material to apply dynamic color
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color);
        projector.SetMaterialPropertyBlock(block);
    }
}