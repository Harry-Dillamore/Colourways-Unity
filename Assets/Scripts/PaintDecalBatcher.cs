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
    public Texture2D customSplatterTexture;
    public Material paintMaterial;

    [Header("Corner Wrapping Settings")]
    [Tooltip("Layer mask for static world geometry (walls, floors) to detect corners against.")]
    public LayerMask environmentMask = ~0;
    [Tooltip("Raycast distance to probe for adjacent perpendicular walls/floors.")]
    public float cornerSearchDistance = 0.4f;

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
    /// Standard splatter entry point (compatibility wrapper).
    /// </summary>
    public void AddSplatter(Vector3 position, Vector3 normal, float radius, Color32 color, int variant = -1)
    {
        AddWrappingSplatter(position, normal, radius, color, variant);
    }

    /// <summary>
    /// Adds a splatter that detects adjacent perpendicular surfaces and folds over 90-degree corners.
    /// </summary>
    public void AddWrappingSplatter(Vector3 position, Vector3 normal, float radius, Color32 color, int variant = -1)
    {
        if (normal.sqrMagnitude < 0.001f) normal = Vector3.up;
        normal.Normalize();

        Vector3 offsetPos = position + normal * 0.004f;

        // Tangent Basis
        Vector3 upDir = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) < 0.95f ? Vector3.up : Vector3.right;
        Vector3 tangent = Vector3.Cross(normal, upDir).normalized;
        Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;

        float angle = Random.Range(0f, Mathf.PI * 2f);
        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);

        Vector3 rotTangent = (tangent * cos + bitangent * sin) * radius;
        Vector3 rotBitangent = (-tangent * sin + bitangent * cos) * radius;

        // Probe 4 cardinal direction bounds around center hit
        Vector3[] checkDirs = new Vector3[] { rotTangent, -rotTangent, rotBitangent, -rotBitangent };
        bool foundCorner = false;
        Vector3 adjacentNormal = Vector3.zero;
        Vector3 cornerEdgePoint = Vector3.zero;
        Vector3 splitDir = Vector3.zero;

        foreach (Vector3 dir in checkDirs)
        {
            Vector3 probeStart = offsetPos + dir;
            // Raycast backward perpendicular to current surface to look for an overhang or adjacent wall/floor
            if (Physics.Raycast(probeStart, dir.normalized, out RaycastHit cornerHit, cornerSearchDistance, environmentMask))
            {
                // Verify we hit a different face (angle difference near 90 degrees)
                if (Vector3.Dot(normal, cornerHit.normal) < 0.3f)
                {
                    foundCorner = true;
                    adjacentNormal = cornerHit.normal;
                    cornerEdgePoint = cornerHit.point;
                    splitDir = dir.normalized;
                    break;
                }
            }
        }

        if (variant < 0 || variant > 3) variant = Random.Range(0, 4);
        float uMin = (variant % 2) * 0.5f;
        float uMax = uMin + 0.5f;
        float vMin = (variant / 2) * 0.5f;
        float vMax = vMin + 0.5f;

        if (foundCorner)
        {
            // --- CONSTRUCT 6-VERTEX BENT RIBBON ---
            int startIndex = vertices.Count;

            Vector3 perpDir = Vector3.Cross(normal, splitDir).normalized * radius;
            Vector3 adjOffset = adjacentNormal * 0.004f;

            // Surface 1 (Primary face)
            Vector3 v0 = offsetPos - splitDir * radius - perpDir;
            Vector3 v1 = offsetPos - splitDir * radius + perpDir;

            // Seam (Corner edge)
            Vector3 v2 = cornerEdgePoint + normal * 0.004f - perpDir;
            Vector3 v3 = cornerEdgePoint + normal * 0.004f + perpDir;

            // Surface 2 (Folded face)
            Vector3 v4 = cornerEdgePoint + adjOffset + splitDir * radius - perpDir;
            Vector3 v5 = cornerEdgePoint + adjOffset + splitDir * radius + perpDir;

            vertices.Add(v0); vertices.Add(v1);
            vertices.Add(v2); vertices.Add(v3);
            vertices.Add(v4); vertices.Add(v5);

            normals.Add(normal); normals.Add(normal);
            normals.Add(normal); normals.Add(normal);
            normals.Add(adjacentNormal); normals.Add(adjacentNormal);

            for (int i = 0; i < 6; i++) colors.Add(color);

            // Remap UVs continuously across both mesh segments
            uvs.Add(new Vector2(uMin, vMin));
            uvs.Add(new Vector2(uMax, vMin));
            uvs.Add(new Vector2(uMin, vMin + 0.25f));
            uvs.Add(new Vector2(uMax, vMin + 0.25f));
            uvs.Add(new Vector2(uMin, vMax));
            uvs.Add(new Vector2(uMax, vMax));

            // Quad 1
            triangles.Add(startIndex + 0); triangles.Add(startIndex + 1); triangles.Add(startIndex + 2);
            triangles.Add(startIndex + 1); triangles.Add(startIndex + 3); triangles.Add(startIndex + 2);

            // Quad 2 (Folded over corner)
            triangles.Add(startIndex + 2); triangles.Add(startIndex + 3); triangles.Add(startIndex + 4);
            triangles.Add(startIndex + 3); triangles.Add(startIndex + 5); triangles.Add(startIndex + 4);

            totalVerticesCount += 6;
        }
        else
        {
            // --- STANDARD 4-VERTEX QUAD ---
            int startIndex = vertices.Count;

            vertices.Add(offsetPos - rotTangent - rotBitangent);
            vertices.Add(offsetPos + rotTangent - rotBitangent);
            vertices.Add(offsetPos + rotTangent + rotBitangent);
            vertices.Add(offsetPos - rotTangent + rotBitangent);

            for (int i = 0; i < 4; i++)
            {
                normals.Add(normal);
                colors.Add(color);
            }

            uvs.Add(new Vector2(uMin, vMin));
            uvs.Add(new Vector2(uMax, vMin));
            uvs.Add(new Vector2(uMax, vMax));
            uvs.Add(new Vector2(uMin, vMax));

            triangles.Add(startIndex + 0); triangles.Add(startIndex + 1); triangles.Add(startIndex + 2);
            triangles.Add(startIndex + 0); triangles.Add(startIndex + 2); triangles.Add(startIndex + 3);

            totalVerticesCount += 4;
        }

        totalSplatsCount++;
        isBatchDirty = true;
    }

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

    public void ClearAll()
    {
        vertices.Clear();
        normals.Clear();
        colors.Clear();
        uvs.Clear();
        triangles.Clear();
        totalSplatsCount = 0;
        totalVerticesCount = 0;

        if (batchedMesh != null) batchedMesh.Clear();
        isBatchDirty = false;
    }

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

                    float wave = Mathf.Sin(theta * 5f + seed) * 0.18f +
                                 Mathf.Cos(theta * 7f - seed * 2f) * 0.12f +
                                 Mathf.Sin(theta * 11f) * 0.08f;
                    float organicRadius = maxR * (1f + wave);

                    float alpha = 0f;
                    if (dist < organicRadius)
                    {
                        alpha = Mathf.Clamp01((organicRadius - dist) * 1.5f);
                    }

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
        tex.Apply(false, true);
        return tex;
    }
}