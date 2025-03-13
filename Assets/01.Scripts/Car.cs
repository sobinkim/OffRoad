using UnityEngine;

public class Car : MonoBehaviour
{
    [SerializeField] private Drive _drive;
    [SerializeField] private SOInput _InputSO;
    [SerializeField] private Boost  boost;

    private void Awake()
    {
        _InputSO.OnMovementChang += _drive.GetInput;
        _InputSO.OnMovementChang += boost.GetDir;
        _InputSO.OnBrakeAc += _drive.Brake;
        _InputSO.OnBoostUse += boost.BoostUse;

    }
    private void OnDestroy()
    {
        _InputSO.OnMovementChang -= _drive.GetInput;
        _InputSO.OnBrakeAc -= _drive.Brake;
    }


}
