using UnityEngine;
using Mirror;

public class PushBall : NetworkBehaviour
{
    public float pushForce = 10f; 
    public float pushCooldown = 0.1f; 
    private float lastPushTime;

    private void OnCollisionEnter(Collision collision)
    {
        if (isServer)
        {
            if (collision.gameObject.CompareTag("Ball"))
            {
                Rigidbody ballRigidbody = collision.gameObject.GetComponent<Rigidbody>();
                if (ballRigidbody != null)
                {
                    Vector3 pushDirection = collision.transform.position - transform.position;
                    pushDirection = pushDirection.normalized; 
                    
                    ballRigidbody.AddForce(pushDirection * pushForce, ForceMode.Impulse);
                    lastPushTime = Time.time;

                    if (X1Log.InfoEnabled)
                        Debug.Log($"Pushed ball with force: {pushDirection * pushForce}");
                }
            }
        }
    }



    private void OnCollisionStay(Collision collision)
    {
        if (isServer && Time.time - lastPushTime >= pushCooldown)
        {
            if (collision.gameObject.CompareTag("Ball"))
            {
                Rigidbody ballRigidbody = collision.gameObject.GetComponent<Rigidbody>();
                if (ballRigidbody != null)
                {
                    Vector3 pushDirection = collision.transform.position - transform.position;
                    pushDirection = pushDirection.normalized;

                    ballRigidbody.AddForce(pushDirection * pushForce, ForceMode.Impulse);

                    // lastPushTime раньше НЕ обновлялся, поэтому условие cooldown было истинным
                    // каждый FixedUpdate: мяч получал импульс 50 раз в секунду, а сервер писал
                    // Debug.Log на каждый из них. Откат мяча разгонялся до огромных скоростей,
                    // координаты уезжали в бесконечность, а лог забивался синхронными записями.
                    lastPushTime = Time.time;

                    if (X1Log.InfoEnabled)
                        Debug.Log($"Pushed ball with force: {pushDirection * pushForce}");
                }
            }
        }
    }
}


// using UnityEngine;
// using Mirror;

// public class PushBall : NetworkBehaviour
// {
//     public float pushForce = 10f; 

//     private void OnCollisionEnter(Collision collision)
//     {
//         if (isServer)
//         {
//             if (collision.gameObject.tag == "Ball")
//             {
//                 Rigidbody ballRigidbody = collision.gameObject.GetComponent<Rigidbody>();
//                 if (ballRigidbody != null)
//                 {
//                     Vector3 pushDirection = collision.contacts[0].point - transform.position;
//                     pushDirection = -pushDirection.normalized; 
//                     ballRigidbody.AddForce(pushDirection * pushForce, ForceMode.Impulse); 
//                 }
//             }
//         }
//     }
// }
