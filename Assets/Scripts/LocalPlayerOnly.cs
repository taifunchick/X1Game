using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Enables input and presentation objects only for the player owned by this client.
/// Remote player copies remain visible, but can never process input or own a camera.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class LocalPlayerOnly : NetworkBehaviour
{
    [SerializeField] private Behaviour[] localOnlyBehaviours;
    [SerializeField] private GameObject[] localOnlyObjects;

    private readonly List<Camera> disabledSceneCameras = new List<Camera>();
    private readonly List<AudioListener> disabledSceneListeners = new List<AudioListener>();

    private void Awake()
    {
        // Player prefabs can be instantiated before Mirror has assigned ownership.
        Apply(false);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        Apply(isLocalPlayer);
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        Apply(true);
        ClaimLocalCamera();
    }

    public override void OnStopLocalPlayer()
    {
        RestoreSceneCamera();
        Apply(false);
        base.OnStopLocalPlayer();
    }

    private void Apply(bool local)
    {
        if (localOnlyBehaviours != null)
        {
            foreach (Behaviour behaviour in localOnlyBehaviours)
            {
                if (behaviour != null)
                    behaviour.enabled = local;
            }
        }

        if (localOnlyObjects != null)
        {
            foreach (GameObject localObject in localOnlyObjects)
            {
                if (localObject != null && localObject != gameObject)
                    localObject.SetActive(local);
            }
        }
    }

    private void ClaimLocalCamera()
    {
        Camera localCamera = GetComponentInChildren<Camera>(true);
        if (localCamera == null)
            return; // Third-person prefabs intentionally use the scene camera.

        localCamera.gameObject.SetActive(true);
        localCamera.enabled = true;

        foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (camera != localCamera && !camera.transform.IsChildOf(transform) && camera.enabled)
            {
                camera.enabled = false;
                disabledSceneCameras.Add(camera);
            }
        }

        AudioListener localListener = localCamera.GetComponent<AudioListener>();
        if (localListener != null)
            localListener.enabled = true;

        foreach (AudioListener listener in FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (listener != localListener && !listener.transform.IsChildOf(transform) && listener.enabled)
            {
                listener.enabled = false;
                disabledSceneListeners.Add(listener);
            }
        }
    }

    private void RestoreSceneCamera()
    {
        foreach (Camera camera in disabledSceneCameras)
        {
            if (camera != null)
                camera.enabled = true;
        }
        disabledSceneCameras.Clear();

        foreach (AudioListener listener in disabledSceneListeners)
        {
            if (listener != null)
                listener.enabled = true;
        }
        disabledSceneListeners.Clear();
    }
}
