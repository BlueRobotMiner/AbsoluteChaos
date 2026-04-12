using UnityEngine;

public class BalanceController : MonoBehaviour
{
    public float targetRotation;
    public float force;

    Rigidbody2D rb;

    void Start()
    {
        rb = gameObject.GetComponent<Rigidbody2D>();
        rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
    }

    void FixedUpdate()
    {
        rb.MoveRotation(Mathf.LerpAngle(rb.rotation, targetRotation, force * Time.fixedDeltaTime));
    }
}
