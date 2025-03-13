using System;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

[CreateAssetMenu(fileName = "SOInput", menuName = "Scriptable Objects/SOInput")]
public class SOInput : ScriptableObject,Controller.ICarMovementActions
{
    private Controller _controller;
    public event Action<Vector2> OnMovementChang;
    public event Action<bool> OnBrakeAc;
    public event Action OnBoostUse;
    private Vector2 InputDir;
    public void OnBrake(InputAction.CallbackContext context)
    {
        if (context.performed) 
        {
            OnBrakeAc(true);
        }
        else
        {
            OnBrakeAc(false);
        }
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        InputDir = context.ReadValue<Vector2>();
        OnMovementChang.Invoke(InputDir);
    }
   

    private void OnEnable()
    {
        if(_controller == null)
        {
            _controller = new Controller();
            _controller.CarMovement.SetCallbacks(this);
        }
        _controller.Enable();
    }

    private void OnDisable()
    {
        _controller.Disable();
    }

    public void OnBoost(InputAction.CallbackContext context)
    {
        if(context.performed)
        {
            OnBoostUse.Invoke();
        }
    }
}
