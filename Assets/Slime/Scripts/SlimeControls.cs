using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

public class SlimeControls : MonoBehaviour
{
    private List<GameObject> slimeNodes = new List<GameObject>();

    private List<Vector3> Anchors = new List<Vector3>();

    private List<SpringJoint> AnchorSprings = new List<SpringJoint>();

    private Transform CenterSlimeNodeObj;

    private bool initialized = false;
    private float moveTimer = 0f;
    private bool canMove = false;
    public float expandForce = 10f;

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if (!gameObject.GetComponent<CreateSlimeNodes>().generatedBody)
        {
            return;
        }

        if (!initialized)
        {
            InitializeNodes();
            moveTimer = 0f;
            canMove = true;
            initialized = true;
        }

        if (!canMove)
        {
            return;
        }

        moveTimer += Time.deltaTime;
        float cycleTime = 10f; // 5s expand + 5s shrink
        float timeInCycle = moveTimer % cycleTime;

        if (timeInCycle > 5f)
        {
            Debug.Log("Expanding");
            Expand();
        }
        else
        {
            Debug.Log("Shrinking");
            Shrink();
        }
    }

    private void InitializeNodes()
    {
        CenterSlimeNodeObj = transform.Find("CenterNode");
        slimeNodes = gameObject.GetComponent<CreateSlimeNodes>().instantiatedNodes;
        Anchors = gameObject.GetComponent<CreateSlimeNodes>().Anchors;
    }

    void Move(Vector3 direction)
    {
        // Implement movement logic here
        // direction: the direction to move in
        // faceDirection: the direction the slime is facing
        Vector3 newPosition = transform.position + direction * Time.deltaTime;
        transform.position = newPosition;
    }

    void Expand()
    {
        foreach (var node in slimeNodes)
        {
            Rigidbody rb = node.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Vector3 dir = (node.transform.position - CenterSlimeNodeObj.transform.position).normalized; // Get the direction from the center to the node
                rb.AddForce(dir * expandForce, ForceMode.Force);
            }
        }

    }

    void Shrink()
    {
        
    }
}
