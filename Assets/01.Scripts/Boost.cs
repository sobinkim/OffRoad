using System.Collections;
using UnityEngine;

public class Boost : MonoBehaviour
{
    [SerializeField] private GameObject carbody;
    [SerializeField] private float boostPower;
    [SerializeField] private float coolTime;
    [SerializeField] private float boosDuration;
    [SerializeField] float speed;
    private IEnumerator co;

    Rigidbody rigid;
    Vector2 dir;
    private void Start()
    {
        rigid = carbody.GetComponent<Rigidbody>();
        co = null;
    }
    private void Update()
    {
      speed = rigid.linearVelocity.magnitude*3.6f;
    }
    public void GetDir(Vector2 inputdir)
    {
        dir = inputdir;
    }
    public void BoostUse()
    {
        if (co == null)
            StartCoroutine(CoolTime());
        else
            print("ÄðÅ¸ÀÓ");
        
    }

    IEnumerator CoolTime()
    {
        print("ºÎ¿õ");
        co = CoolTime();
        rigid.AddForce(carbody.transform.TransformDirection(Vector3.forward) * boostPower, ForceMode.Force);
        yield return new WaitForSeconds(boosDuration);
        yield return new WaitForSeconds(coolTime);
        co = null;



    }

 
}
