using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class GroundCheck : MonoBehaviour
{
    [SerializeField] private Transform targetTransform;
    [SerializeField] private float dir;
    [SerializeField] private LayerMask targetLayer;
    [SerializeField] private bool isGround;
    [SerializeField] private float durationValue;
    [SerializeField] private float duration;

   
    private float Duration
    {
        get
        {
            return duration;
        }
        set
        {
            if (value <= 0)
            {
                duration = 0;
            }
            else
            {
                duration = value;
            }
        }
    }
    private IEnumerator CO = null;
    void Start()
    {
        duration = durationValue;
    }
    void Update()
    {
        Check();
        Debug.DrawRay(targetTransform.position, targetTransform.TransformDirection(Vector3.down) * dir, Color.red);
    }
    private void Check()
    {
        isGround = Physics.Raycast(targetTransform.position, targetTransform.TransformDirection(Vector3.down) * dir, targetLayer);
        if (!isGround && CO == null)
        {
            bool timerOver = Timer();
            if (timerOver)
            {
                StartCoroutine(RePlace());
                CO = RePlace();
            }
        }
        else
        {
            Duration = durationValue;
        }


    }
    private bool Timer()
    {
        Duration -= Time.deltaTime;
        if (Duration <= 0)
        {
            return true;
        }
        else return false;

    }

    IEnumerator RePlace()
    {
        targetTransform.rotation = Quaternion.identity;
        targetTransform.position += new Vector3(0, 8, 0);
        yield return new WaitUntil(() => isGround == true);
        Duration = durationValue;
        CO = null;

    }

}
