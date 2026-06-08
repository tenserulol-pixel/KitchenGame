using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
public class LookAtCamera : MonoBehaviour
{
private enum Mode
    {
        LookAt,
        LookAtInverted,
        Forward,
        ForwardInverted,
    }
[SerializeField] private Mode mode; 
private void LateUpdate()
    {
        switch (mode)
        {
            case Mode.LookAt:
                transform.LookAt(Camera.main.transform);
                break;
            case Mode.LookAtInverted:
                Vector3 dirFromCamera=transform.position-Camera.main.transform.position;
                transform.LookAt(transform.position+dirFromCamera);
                break;
            case Mode.Forward:
                transform.forward=Camera.main.transform.forward;
                break;
            case Mode.ForwardInverted:
                transform.forward=-Camera.main.transform.forward;
                break;
        }
}
}