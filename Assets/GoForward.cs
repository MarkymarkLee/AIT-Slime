using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GoForward : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        Vector3 direction = gameObject.transform.forward;
        gameObject.transform.position += direction * Time.deltaTime * 0.5f; // Move forward at a speed of 5 units per second
    }
}
