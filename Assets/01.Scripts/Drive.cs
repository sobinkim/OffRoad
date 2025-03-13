using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;

public class Drive : MonoBehaviour
{
    [SerializeField] private WheelCollider[] wc;
    [SerializeField] private float torque = 200;
    [SerializeField] private float MaxSteerAngle = 30;
    [SerializeField] private float _brakeVal = 300;
    [SerializeField] private GameObject[] Wheel;
    [SerializeField] private float repositionSpeed;
    [SerializeField] private GroundCheck ground;

    private Vector2 dir;
 
    private void Start()
    {

    }

   
    public void GetInput(Vector2 inputVal)
    {
        dir = inputVal;
    }
    private void Update()
    {
        Go(dir.y,dir.x);
        if(ground.GetIsGround())
        Reposition();
    }
    private void  Reposition()
    {
        float currentRotZ = transform.eulerAngles.z;
        //float currentRotZ = transform.rotation.z 쿼턴이온 값을 직접 가져와서 x;
        float reRotZ= Mathf.Lerp(currentRotZ, 0, repositionSpeed);
        transform.rotation = Quaternion.Euler(transform.eulerAngles.x, transform.eulerAngles.y, reRotZ);

    }

    public void Go(float accel, float steer)
    {
        Quaternion quater;
        Vector3 position;
        float CurrentSteer = 0; ;

        
        CurrentSteer = Mathf.Lerp(CurrentSteer, steer,0.7f);
        for (int i = 0; i < wc.Length; i++)
        {
            wc[i].GetWorldPose(out position, out quater);
            Wheel[i].transform.position = position;
            Wheel[i].transform.rotation = quater;

            if (i < 2)
            {
                float thrustTourque = accel * torque;
                wc[i].motorTorque = thrustTourque;
              
                wc[i].steerAngle = CurrentSteer*MaxSteerAngle;
            }
          

        }
    }
    public void Brake(bool SendValue)
    {
        for(int i = 3;i>1; i--)
        {
            if (SendValue)
            {
                print($"{Wheel[i]}");
                wc[i].brakeTorque = _brakeVal;
            }
             
            else
                wc[i].brakeTorque = 0;

        }
    }
  



}
