using System.Collections;
using UnityEngine;

public class GravityReversal : MonoBehaviour
{
    [SerializeField] private Rigidbody rigid;
    [SerializeField] private float gravityScale;
    [SerializeField] private float gravityCon;
    [SerializeField] private float RayLength;
    [SerializeField] private LayerMask GroundLayer;

    private Coroutine coroutine;
    private GameObject targetOj;
    private bool revers;
    private float ratio;

    private float smoothRatio = 0f;   // �ε巯�� ��ȭ ������ ����
    private float ratioVelocity = 0f; // SmoothDamp�� �ӵ� ���� ����

    private void Start()
    {
        print(this.gameObject.name);
        targetOj = rigid.gameObject;
        revers = false;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            revers = !revers;
            if (revers && coroutine == null)
            {
                coroutine = StartCoroutine(SetGravityRevers(Vector3.up, 180));
            }
            else
            {
                StartCoroutine(SetGravityRevers(Vector3.down, 0));
            }
        }
    }

    private IEnumerator SetGravityRevers(Vector3 dir, float targetAngle)
    {
        if (coroutine != null)
        {
            StopCoroutine(coroutine);
        }

        coroutine = StartCoroutine(RotateOverTime(dir, 0.5f, targetAngle));

        while (revers)
        {
            float power = Mathf.Lerp(0, (gravityScale * rigid.mass) / gravityCon, 0.5f);
            rigid.AddForce(dir * power, ForceMode.Acceleration);
            yield return null;
        }

        coroutine = null;
    }

    private IEnumerator RotateOverTime(Vector3 dir, float duration, float targetAngle)
    {
        float currentTime = 0;
        Quaternion startRotation = targetOj.transform.rotation;
        Quaternion endRotation = Quaternion.Euler(0, 0, targetAngle);

        smoothRatio = ratio;  // ù ��° ���� �� smoothRatio�� �ٷ� ratio�� ����

        while (currentTime < duration)
        {
            CheckDistans(dir);

            // SmoothDamp�� `ratio`�� �ε巴�� ��ȭ��Ŵ
            smoothRatio = Mathf.SmoothDamp(smoothRatio, ratio, ref ratioVelocity, 0.4f);

            // �ε巯�� ������ ȸ�� ����
            targetOj.transform.rotation = Quaternion.Lerp(startRotation, endRotation, smoothRatio);

            currentTime += Time.deltaTime;
            yield return null;
        }

        print("ȸ�� �Ϸ�");
        coroutine = null;
    }

    private void CheckDistans(Vector3 dir)
    {
        RaycastHit hit;
        if (Physics.Raycast(targetOj.transform.position, dir, out hit, RayLength, GroundLayer))
        {
            float distanceToHit = hit.distance;
            float distanceFromMe = Vector3.Distance(transform.position, hit.point);

            ratio = distanceFromMe / distanceToHit;

            print("����: " + (ratio * 100) + "%");
        }
    }
}
