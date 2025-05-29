using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Analytics;

public class UpdatePosition : MonoBehaviour
{
    private List<Transform> slimeNodes = new List<Transform>();
    private MeshFilter meshFilter;
    private Mesh mesh;
    private CreateSlimeNodes createSlimeNodes;
    private bool initialized = false;

    // Start is called before the first frame update
    void Start()
    {
        meshFilter = GetComponent<MeshFilter>();
        if (meshFilter != null)
        {
            mesh = meshFilter.mesh;
        }
        createSlimeNodes = GetComponent<CreateSlimeNodes>();
    }

    // Update is called once per frame
    void Update()
    {
        if (createSlimeNodes != null && createSlimeNodes.generatedBody && mesh != null)
        {
            // Only initialize the nodes list once or when needed
            if (!initialized)
            {
                InitializeNodes();
            }

            // Update the mesh vertices
            UpdateMeshVertices();
            // UpdateSlimePosition();
        }
    }

    void InitializeNodes()
    {
        slimeNodes = gameObject.GetComponent<CreateSlimeNodes>().instantiatedNodes.ConvertAll(node => node.transform);
    }

    void UpdateMeshVertices()
    {
        // Convert transform positions to Vector3 array
        Vector3[] vertices = new Vector3[slimeNodes.Count];
        for (int i = 0; i < slimeNodes.Count; i++)
        {
            // Convert from world position to local position relative to this object
            vertices[i] = transform.InverseTransformPoint(slimeNodes[i].position);
        }

        // Update the mesh
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
    }
}
