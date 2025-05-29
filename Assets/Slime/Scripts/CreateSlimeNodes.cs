using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class CreateSlimeNodes : MonoBehaviour
{
    public GameObject nodePrefab;

    [Header("Children Springs Settings")]
    public float springStrength = 100f;
    public float springDamper = 5f;

    [Header("Center Springs Settings")]
    public float recoverSpringStrength = 100f;
    public float recoverSpringDamper = 5f;


    private MeshFilter meshFilter;
    public List<GameObject> instantiatedNodes = new List<GameObject>();
    private Transform slimeNodesObj;

    private Rigidbody centerRigidbody;

    private ArticulationBody articulationBody;
    public bool generatedBody = false;
    private List<SpringJoint> instantiatedSprings = new List<SpringJoint>();
    public List<Vector3> Anchors = new List<Vector3>();

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        // Ensure the SlimeNodes parent exists
        slimeNodesObj = transform.Find("CenterNode");
        articulationBody = slimeNodesObj.GetComponent<ArticulationBody>();
        centerRigidbody = slimeNodesObj.GetComponent<Rigidbody>();
    }

    private void Start()
    {

    }

    private void Update()
    {
        if (!generatedBody && meshFilter != null && meshFilter.mesh != null)
        {
            // Check if the mesh has been modified
            if (meshFilter.mesh.vertexCount > 0)
            {
                GenerateNodes();
            }
        }
    }

    public void GenerateNodes()
    {
        if (nodePrefab == null)
        {
            Debug.LogError("Node prefab is not assigned!");
            return;
        }

        Debug.Log("Generating nodes from mesh...");

        ClearNodes();
        ClearSpringJoints();

        // Access mesh vertices and triangles
        Vector3[] vertices = meshFilter.mesh.vertices;

        // Create objects at each vertex position
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 worldPos = transform.TransformPoint(vertices[i]);
            GameObject node = Instantiate(nodePrefab, worldPos, Quaternion.identity);
            node.name = "SlimeNode_" + i;
            node.transform.parent = slimeNodesObj;
            instantiatedNodes.Add(node);
            Anchors.Add(node.transform.localPosition);
            CreateSpringForNode(node);
        }

        Debug.Log($"Generated {instantiatedNodes.Count} nodes from mesh with {vertices.Length} vertices");

        CreateAllToAllSprings();

        generatedBody = true;
    }

    private void CreateAllToAllSprings()
    {
        // Track connections to avoid duplicates
        HashSet<string> connections = new HashSet<string>();

        // Connect all nodes to all other nodes within max distance
        for (int i = 0; i < instantiatedNodes.Count; i++)
        {
            for (int j = i + 1; j < instantiatedNodes.Count; j++)
            {
                GameObject nodeA = instantiatedNodes[i];
                GameObject nodeB = instantiatedNodes[j];
                CreateSpringBetweenNodes(nodeA, nodeB, connections);
            }
            // Create a spring for the node itself
            // CreateSpringForNode(instantiatedNodes[i]);
        }

        Debug.Log($"Created {instantiatedSprings.Count} spring joints between all nodes.");
    }

    private void CreateSpringForNode(GameObject node)
    {
        // Create a spring joint for the node
        SpringJoint spring = node.AddComponent<SpringJoint>();
        spring.spring = recoverSpringStrength;
        spring.damper = recoverSpringDamper;
        spring.connectedBody = centerRigidbody;
        spring.connectedArticulationBody = articulationBody;
        spring.autoConfigureConnectedAnchor = false;
        spring.anchor = Vector3.zero; // Set the anchor to the center of the node
        instantiatedSprings.Add(spring);
    }

    private void CreateSpringBetweenNodes(GameObject nodeA, GameObject nodeB, HashSet<string> connections)
    {
        // Create a unique key for this connection to avoid duplicates
        string connectionKey = GetConnectionKey(nodeA, nodeB);

        // Skip if we've already created this connection
        if (connections.Contains(connectionKey))
        {
            return;
        }

        // Create a spring joint between the two nodes
        SpringJoint spring = nodeA.AddComponent<SpringJoint>();
        spring.connectedBody = nodeB.GetComponent<Rigidbody>();
        spring.spring = springStrength;
        spring.damper = springDamper;


        // Track this connection
        connections.Add(connectionKey);
        instantiatedSprings.Add(spring);
    }

    private string GetConnectionKey(GameObject a, GameObject b)
    {
        // Create a deterministic key regardless of parameter order
        int idA = a.GetInstanceID();
        int idB = b.GetInstanceID();

        if (idA < idB)
        {
            return $"{idA}_{idB}";
        }
        else
        {
            return $"{idB}_{idA}";
        }
    }

    public void ClearNodes()
    {
        // Destroy all previously instantiated nodes
        foreach (GameObject node in instantiatedNodes)
        {
            if (node != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(node);
                }
                else
                {
                    DestroyImmediate(node);
                }
            }
        }

        instantiatedNodes.Clear();
    }

    public void ClearSpringJoints()
    {
        instantiatedSprings.Clear();
    }

    // Editor button to manually generate nodes
    public void EditorGenerateNodes()
    {
        if (meshFilter == null)
        {
            meshFilter = GetComponent<MeshFilter>();
        }

        GenerateNodes();
    }
}