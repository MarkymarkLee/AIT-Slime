using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MirrorMovement : MonoBehaviour
{

    public Transform playerTarget; // The target to mirror
    public Transform mirror; // The plane to mirror across

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 localPlayerPosition = mirror.InverseTransformPoint(playerTarget.position);
        transform.position = mirror.TransformPoint(new Vector3(localPlayerPosition.x, localPlayerPosition.y, -localPlayerPosition.z));
        Vector3 lookatDirection = mirror.TransformPoint(new Vector3(-localPlayerPosition.x, localPlayerPosition.y, localPlayerPosition.z));
        transform.LookAt(lookatDirection);
    }
}
