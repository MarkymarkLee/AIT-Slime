using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VR_Head_Position : MonoBehaviour
{
    Transform centerNode;
    public Vector3 Offset = new Vector3(0, 1, 0); // Offset to apply to the CenterNode's position

    void Start()
    {
        // OVRManager.display.RecenterPose();
        // Find the Slime GameObject and its child CenterNode
        GameObject slime = GameObject.Find("Slime");
        if (slime != null)
        {
            Transform found = slime.transform.Find("CenterNode");
            if (found != null)
            {
                centerNode = found;
            }
            else
            {
                Debug.LogError("CenterNode child not found under Slime GameObject.");
            }
        }
        else
        {
            Debug.LogError("Slime GameObject not found in the scene.");
        }
    }

    void Update()
    {
        if (centerNode != null)
        {
            // Set this object's position to CenterNode's position + 1 on the y-axis
            transform.position = centerNode.position + Offset;
        }
    }
}
