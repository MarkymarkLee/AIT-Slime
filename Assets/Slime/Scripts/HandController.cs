using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HandController : MonoBehaviour
{
    private Transform leftHand;
    private Transform rightHand;

    // Assign these externally or find the source of the hand positions in your VR system
    public Transform leftHandTarget;
    public Transform rightHandTarget;

    // Start is called before the first frame update
    void Start()
    {
        // Find children named "Left Hand" and "Right Hand"
        leftHand = transform.Find("LeftHand");
        rightHand = transform.Find("RightHand");
    }

    // Update is called once per frame
    void Update()
    {
        // Update the hand positions if targets are assigned
        if (leftHand != null && leftHandTarget != null)
        {
            leftHand.position = leftHandTarget.position;
            leftHand.rotation = leftHandTarget.rotation;
        }
        if (rightHand != null && rightHandTarget != null)
        {
            rightHand.position = rightHandTarget.position;
            rightHand.rotation = rightHandTarget.rotation;
        }
    }
}
