using System.Collections;
using UnityEngine;

public class Boost : MonoBehaviour
{
    [SerializeField] private GameObject carbody;
    [SerializeField] private float boostPower;
    [SerializeField] private float coolTime;
    [SerializeField] private float boosDuration;
    private IEnumerator co;

    Rigidbody rigid;
    Vector2 dir;
    private void Start()
    {
        rigid = carbody.GetComponent<Rigidbody>();
        co = null;
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
            print("��Ÿ��");
        
    }

    IEnumerator CoolTime()
    {
        print("�ο�");
        co = CoolTime();
        rigid.AddForce(carbody.transform.TransformDirection(Vector3.forward) * boostPower, ForceMode.Force);
        yield return new WaitForSeconds(boosDuration);
        yield return new WaitForSeconds(coolTime);
        co = null;



    }

 
}
