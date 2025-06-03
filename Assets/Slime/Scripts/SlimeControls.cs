using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

public class SlimeControls : MonoBehaviour
{
    private List<GameObject> slimeNodes = new List<GameObject>();

    private Transform CenterSlimeNodeObj;

    private bool initialized = false;
    private bool canMove = false;
    public bool useAButtonForExpand = true; // Toggle to enable/disable A button expand/shrink
    public float expandForce = 3f;
    public float moveForce = 3f;

    [Header("XR Input")]
    public InputActionProperty moveAction; // Assign this in the inspector to the left/right controller's primary2DAxis
    public InputActionProperty aButtonAction; // Assign this to the A button action in the inspector
    private Vector2 inputAxis = Vector2.zero;

    private BreathControllerV2 breathController;

    // Start is called before the first frame update
    void Start()
    {
        breathController = gameObject.GetComponent<BreathControllerV2>();
        if (breathController == null)
        {
            Debug.LogError("BreathControllerV2 not found in the scene.");
        }
        if (breathController.currentMode == BreathControllerV2.Mode.unity_control)
        {
            useAButtonForExpand = true;
        }
        else
        {
            useAButtonForExpand = false;
        }
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
            canMove = true;
            initialized = true;
        }
        if (!canMove)
        {
            return;
        }
        // XR input handling (action-based)
        if (moveAction != null && moveAction.action != null)
        {
            inputAxis = moveAction.action.ReadValue<Vector2>();
        }
        else
        {
            inputAxis = Vector2.zero;
        }
        // Move slime if input is detected
        if (inputAxis.sqrMagnitude > 0.01f)
        {
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            Vector3 moveDir = Vector3.zero;
            if (cam != null)
            {
                Vector3 forward = cam.forward;
                Vector3 right = cam.right;
                forward.y = 0;
                right.y = 0;
                forward.Normalize();
                right.Normalize();
                moveDir = forward * inputAxis.y + right * inputAxis.x;
            }
            else
            {
                moveDir = new Vector3(inputAxis.x, 0, inputAxis.y);
            }
            Move(moveDir.normalized);
        }

        // Handle A button for expanding/shrinking
        if (useAButtonForExpand && aButtonAction != null && aButtonAction.action != null)
        {
            if (aButtonAction.action.triggered)
            {
                if (breathController.currentCharacterState == BreathControllerV2.CharacterState.normal)
                {
                    breathController.UpdateCharacterState(BreathControllerV2.CharacterState.enlarged);
                }
                else if (breathController.currentCharacterState == BreathControllerV2.CharacterState.enlarged)
                {
                    breathController.UpdateCharacterState(BreathControllerV2.CharacterState.normal);
                }
                else if (breathController.currentCharacterState == BreathControllerV2.CharacterState.shrunken)
                {
                    breathController.UpdateCharacterState(BreathControllerV2.CharacterState.enlarged);
                }
            }
        }

        switch (breathController.currentCharacterState)
        {
            case BreathControllerV2.CharacterState.enlarged:
                Expand();
                break;
            case BreathControllerV2.CharacterState.shrunken:
                Shrink();
                break;
            case BreathControllerV2.CharacterState.normal:
                Normal();
                break;
        }
        
    }

    private void InitializeNodes()
    {
        CenterSlimeNodeObj = transform.Find("CenterNode");
        slimeNodes = gameObject.GetComponent<CreateSlimeNodes>().instantiatedNodes;
    }

    void Move(Vector3 direction)
    {
        foreach (var node in slimeNodes)
        {
            Rigidbody rb = node.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.AddForce(direction * moveForce, ForceMode.Force);
            }
        }
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

    void Normal()
    {
        
    }

    void Shrink()
    {
        
    }
}
