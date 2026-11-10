using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-50)]
public class PaintDecalBatcher : MonoBehaviour
{
    private static PaintDecalBatcher instance;
    public static PaintDecalBatcher Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<PaintDecalBatcher>();
                if (instance == null)
                {
                    GameObject go = new GameObject("[PaintDecalBatcher]");
                    instance = go.AddComponent<PaintDecalBatcher>();
                }
            }
            return instance;
        }
    }

    [Header("Material & Texture")]
    [Tooltip("Optional custom splatter texture. If null, a high-quality procedural texture atlas will be generated automatically.")]
    public Texture2D customSplatterTexture;
    [Tooltip("Custom material using Custom/PaintSplatter shader. If null, automatically created.")]
    public Material paintMaterial;

    [Header("Batch Stats (Read-Only)")]
    [SerializeField] private int totalSplatsCount = 0;
    [SerializeField] private int totalVerticesCount = 0;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh batchedMesh;

    // CPU mesh buffers
    private readonly List<Vector3> vertices = new List<Vector3>(4096);
    private readonly List<Vector3> normals = new List<Vector3>(4096);
    private readonly List<Color32> colors = new List<Color32>(4096);
    private readonly List<Vector2> uvs = new List<Vector2>(4096);
    private readonly List<int> triangles = new List<int>(6144);

    private bool isBatchDirty = false;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;

        InitializeMeshAndMaterial();
    }

    private void InitializeMeshAndMaterial()
    {
        meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null) meshFilter = gameObject.AddComponent<MeshFilter>();

        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();

        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        batchedMesh = new Mesh
        {
            name = "BatchedPaintDecals",
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };
        batchedMesh.MarkDynamic();
        meshFilter.sharedMesh = batchedMesh;

        if (paintMaterial == null)
        {
            Shader shader = Shader.Find("Custom/PaintSplatter");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            paintMaterial = new Material(shader)
            {
                name = "BatchedPaintMaterial"
            };

            Texture2D splatTex = customSplatterTexture != null ? customSplatterTexture : GenerateProceduralAtlas();
            paintMaterial.mainTexture = splatTex;
        }

        meshRenderer.sharedMaterial = paintMaterial;
    }

    /// <summary>
    /// Adds a paint splodge to the batch. Call Commit() when finished adding a burst of splatters.
    /// </summary>
    public void AddSplatter(Vector3 position, Vector3 normal, float radius, Color32 color, int variant = -1)
    {
        if (normal.sqrMagnitude < 0.001f) normal = Vector3.up;
        normal.Normalize();

        // Small normal offset to ensure no precision clipping on flat geometry
        Vector3 offsetPos = position + normal * 0.004f;

        // Calculate surface tangent frame
        Vector3 up = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) < 0.95f ? Vector3.up : Vector3.right;
        Vector3 tangent = Vector3.Cross(normal, up).normalized;
        Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;

        // Random rotation around surface normal
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);

        Vector3 rotTangent = (tangent * cos + bitangent * sin) * radius;
        Vector3 rotBitangent = (-tangent * sin + bitangent * cos) * radius;

        int startIndex = vertices.Count;

        // 4 Quad vertices
        vertices.Add(offsetPos - rotTangent - rotBitangent);
        vertices.Add(offsetPos + rotTangent - rotBitangent);
        vertices.Add(offsetPos + rotTangent + rotBitangent);
        vertices.Add(offsetPos - rotTangent + rotBitangent);

        for (int i = 0; i < 4; i++)
        {
            normals.Add(normal);
            colors.Add(color);
        }

        // UV Atlas (2x2 grid of 4 variants)
        if (variant < 0 || variant > 3)
        {
            variant = Random.Range(0, 4);
        }

        float uMin = (variant % 2) * 0.5f;
        float uMax = uMin + 0.5f;
        float vMin = (variant / 2) * 0.5f;
        float vMax = vMin + 0.5f;

        uvs.Add(new Vector2(uMin, vMin));
        uvs.Add(new Vector2(uMax, vMin));
        uvs.Add(new Vector2(uMax, vMax));
        uvs.Add(new Vector2(uMin, vMax));

        // Two triangles
        triangles.Add(startIndex + 0);
        triangles.Add(startIndex + 1);
        triangles.Add(startIndex + 2);

        triangles.Add(startIndex + 0);
        triangles.Add(startIndex + 2);
        triangles.Add(startIndex + 3);

        totalSplatsCount++;
        totalVerticesCount += 4;
        isBatchDirty = true;
    }

    /// <summary>
    /// Uploads all buffered splatters to the GPU in a single efficient call.
    /// </summary>
    public void Commit()
    {
        if (!isBatchDirty || batchedMesh == null) return;

        batchedMesh.Clear();
        batchedMesh.SetVertices(vertices);
        batchedMesh.SetNormals(normals);
        batchedMesh.SetColors(colors);
        batchedMesh.SetUVs(0, uvs);
        batchedMesh.SetTriangles(triangles, 0);
        batchedMesh.RecalculateBounds();

        isBatchDirty = false;
    }

    /// <summary>
    /// Clears all permanent splatters from the scene if needed.
    /// </summary>
    public void ClearAll()
    {
        vertices.Clear();
        normals.Clear();
        colors.Clear();
        uvs.Clear();
        triangles.Clear();
        totalSplatsCount = 0;
        totalVerticesCount = 0;

        if (batchedMesh != null)
        {
            batchedMesh.Clear();
        }
        isBatchDirty = false;
    }

    /// <summary>
    /// Generates a 512x512 texture containing 4 organic paint splatter variations in a 2x2 grid.
    /// </summary>
    private Texture2D GenerateProceduralAtlas()
    {
        int size = 512;
        int half = size / 2;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "ProceduralSplatterAtlas",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Color[] pixels = new Color[size * size];

        for (int q = 0; q < 4; q++)
        {
            int qx = (q % 2) * half;
            int qy = (q / 2) * half;
            float cx = qx + half * 0.5f;
            float cy = qy + half * 0.5f;
            float maxR = half * 0.38f;

            int seed = q * 100 + 42;
            Random.InitState(seed);

            // Droplet positions for this quadrant
            int dropletCount = Random.Range(7, 14);
            Vector2[] dropletCenters = new Vector2[dropletCount];
            float[] dropletRadii = new float[dropletCount];

            for (int d = 0; d < dropletCount; d++)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float dist = Random.Range(maxR * 0.7f, half * 0.46f);
                dropletCenters[d] = new Vector2(cx + Mathf.Cos(ang) * dist, cy + Mathf.Sin(ang) * dist);
                dropletRadii[d] = Random.Range(2.5f, 7.5f);
            }

            for (int y = qy; y < qy + half; y++)
            {
                for (int x = qx; x < qx + half; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float theta = Mathf.Atan2(dy, dx);

                    // Organic wavy shape
                    float wave = Mathf.Sin(theta * 5f + seed) * 0.18f +
                                 Mathf.Cos(theta * 7f - seed * 2f) * 0.12f +
                                 Mathf.Sin(theta * 11f) * 0.08f;
                    float organicRadius = maxR * (1f + wave);

                    float alpha = 0f;
                    if (dist < organicRadius)
                    {
                        alpha = Mathf.Clamp01((organicRadius - dist) * 1.5f);
                    }

                    // Satellite droplets
                    for (int d = 0; d < dropletCount; d++)
                    {
                        float dDist = Vector2.Distance(new Vector2(x, y), dropletCenters[d]);
                        if (dDist < dropletRadii[d])
                        {
                            float dAlpha = Mathf.Clamp01((dropletRadii[d] - dDist) * 1.2f);
                            if (dAlpha > alpha) alpha = dAlpha;
                        }
                    }

                    int idx = y * size + x;
                    pixels[idx] = new Color(1f, 1f, 1f, alpha);
                }
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, true); // make non-readable to free CPU RAM
        return tex;
    }
}
