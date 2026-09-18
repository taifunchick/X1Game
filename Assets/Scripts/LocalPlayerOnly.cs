using Mirror;
using UnityEngine;

/// Put on the player root. Assign movement, input, camera and helper objects.
/// Remote copies stay visible but cannot steal this client's camera or input.
[DefaultExecutionOrder(-1000)]
public class LocalPlayerOnly : NetworkBehaviour
{
    [SerializeField] Behaviour[] localOnlyBehaviours;
    [SerializeField] GameObject[] localOnlyObjects;

    void Awake()
    {
        Apply(false);
    }

    public override void OnStartClient()
    {
        Apply(isLocalPlayer);
    }

    public override void OnStartLocalPlayer()
    {
        Apply(true);
    }

    void Apply(bool local)
    {
        if (localOnlyBehaviours != null)
        {
            foreach (var behaviour in localOnlyBehaviours)
            {
                if (behaviour != null)
                    behaviour.enabled = local;
            }
        }

        if (localOnlyObjects != null)
        {
            foreach (var go in localOnlyObjects)
            {
                if (go != null && go != gameObject)
                    go.SetActive(local);
            }
        }
    }
}
