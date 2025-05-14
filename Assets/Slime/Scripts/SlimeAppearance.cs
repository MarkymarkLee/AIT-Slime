using System.Collections.Generic;
using UnityEngine;


[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class SlimeAppearance : MonoBehaviour
{
    // List of nodes (GameObjects) that define the slime's structure.
    public List<Transform> nodes;

    // Number of subdivisions around each node.  Higher values create a smoother surface.
    [Range(16, 64)] // Increased range for more smoothness
    public int radialSegments = 24; // Increased default value for smoothness

    // Radius of the influence of each node.  Adjust to control how "blobby" the slime looks.
    [Range(0.1f, 1f)] // Added a reasonable range
    public float nodeRadius = 0.25f; // Slightly reduced default

    // Controls how spherical the connections are.  0 = pointy, 1 = round
    [Range(0f, 1f)]
    public float sphereFactor = 0.75f; // Default value

    // Strength of the tension.  Higher values make the slime more taut.
    [Range(0f, 1f)] // Added range
    public float tension = 0.6f;

    // Offset the mesh from the nodes
    public Vector3 meshOffset = Vector3.zero;

    private MeshFilter meshFilter;
    private Mesh mesh;
    private Vector3[] vertices;
    private int[] triangles;
    private Vector3[] normals;

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        mesh = new Mesh();
        mesh.name = "SlimeMesh";
        meshFilter.sharedMesh = mesh;
    }

    private void Update()
    {
        if (nodes == null || nodes.Count < 2)
        {
            if (meshFilter.sharedMesh != null)
                meshFilter.sharedMesh.Clear();
        }

        Transform slimeParent = gameObject.transform.parent;
        if (slimeParent == null)
        {
            Debug.LogError("Slime parent object not found!");
            return;
        }

        Transform SlimeNodesObj = slimeParent.Find("SlimeNodes");
        if (SlimeNodesObj == null)
        {
            Debug.LogError("SlimeNodes object not found!");
            return;
        }

        if (gameObject.transform.parent.GetComponent<CreateSlimeNodes>().generatedBody)
        {
            nodes = new List<Transform>();
            foreach (Transform child in SlimeNodesObj)
            {
                if (child != null && child.gameObject.activeSelf)
                {
                    nodes.Add(child);
                }
            }
            GenerateMesh();
        }

    }

    private void GenerateMesh()
    {
        int numNodes = nodes.Count;
        int totalVertices = numNodes * radialSegments;
        int totalTriangles = numNodes * radialSegments * 6;

        if (vertices == null || vertices.Length != totalVertices)
        {
            vertices = new Vector3[totalVertices];
            normals = new Vector3[totalVertices];
        }
        if (triangles == null || triangles.Length != totalTriangles)
        {
            triangles = new int[totalTriangles];
        }

        // 1. Calculate Vertex Positions
        for (int i = 0; i < numNodes; i++)
        {
            Transform node = nodes[i];
            for (int j = 0; j < radialSegments; j++)
            {
                float angle = (float)j / radialSegments * Mathf.PI * 2f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                vertices[i * radialSegments + j] = node.position + direction * nodeRadius;
            }
        }

        // 2. Apply Tension and Sphere Factor
        for (int i = 0; i < numNodes; i++)
        {
            Transform node1 = nodes[i];
            Transform node2 = nodes[(i + 1) % numNodes];

            Vector3 center = (node1.position + node2.position) * 0.5f;
            float distance = Vector3.Distance(node1.position, node2.position);
            float sphereInfluence = Mathf.Clamp01(1f - distance / (nodeRadius * 2f)); // 0 at 2*radius, 1 at 0

            for (int j = 0; j < radialSegments; j++)
            {
                int index1 = i * radialSegments + j;
                int index2 = ((i + 1) % numNodes) * radialSegments + j;

                // Apply tension
                vertices[index1] = Vector3.Lerp(vertices[index1], center, tension);
                vertices[index2] = Vector3.Lerp(vertices[index2], center, tension);

                // Apply sphere factor to make it more circular
                Vector3 toCenter = center - vertices[index1];
                vertices[index1] += toCenter * sphereFactor * sphereInfluence;

                Vector3 toCenter2 = center - vertices[index2];
                vertices[index2] += toCenter2 * sphereFactor * sphereInfluence;
            }
        }

        // Apply mesh offset
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] += meshOffset;
        }

        // 3. Generate Triangles
        int triIndex = 0;
        for (int i = 0; i < numNodes; i++)
        {
            for (int j = 0; j < radialSegments; j++)
            {
                int index1 = i * radialSegments + j;
                int index2 = i * radialSegments + (j + 1) % radialSegments;
                int index3 = ((i + 1) % numNodes) * radialSegments + j;
                int index4 = ((i + 1) % numNodes) * radialSegments + (j + 1) % radialSegments;

                triangles[triIndex++] = index1;
                triangles[triIndex++] = index3;
                triangles[triIndex++] = index2;

                triangles[triIndex++] = index2;
                triangles[triIndex++] = index3;
                triangles[triIndex++] = index4;
            }
        }

        // 4. Calculate Normals
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        normals = mesh.normals;

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.normals = normals;
        meshFilter.sharedMesh = mesh;
    }

    private void OnDrawGizmos()
    {
        if (nodes == null || nodes.Count < 2) return;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] == null) continue;
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(nodes[i].position, 0.1f);

            if (i < nodes.Count - 1 && nodes[i + 1] != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(nodes[i].position, nodes[i + 1].position);
            }
            else if (i == nodes.Count - 1 && nodes[0] != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(nodes[i].position, nodes[0].position);
            }
        }
    }
}
