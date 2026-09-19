using UnityEngine;
using Mirror;
using UnityEngine.EventSystems;

public class Lasertag : NetworkBehaviour
{
    [SyncVar] public string team = "";

    [SerializeField] private LayerMask _targetLayers = Physics.DefaultRaycastLayers;

    private Camera _camera;

    private const float _range = 100f;


    [SerializeField] private GameObject _shotPrefab;
    [SerializeField] private float _shotSpeed = 40f;
    [SerializeField] private float _shotDuration = 1.5f;
    [SerializeField] private float _shotScale = 0.2f;

    private void Start()
    {
        _camera = Camera.main;
        Debug.Log($"Lasertag Start: isLocalPlayer={isLocalPlayer}, isServer={isServer}, isClient={isClient}, team={team}");
    }

    private void Update()
    {
        if (!isLocalPlayer) return;

        if (Input.GetMouseButtonDown(0))
        {
            // Клик по UI (выбор команды, кнопки) не должен стрелять.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            Shoot();
        }
    }

    private void Shoot()
    {
        if (_camera == null) return;

        RaycastHit hit;
        if (Physics.Raycast(_camera.transform.position, _camera.transform.forward, out hit, _range, _targetLayers, QueryTriggerInteraction.Ignore))
        {
            Lasertag hitLasertag = hit.collider.GetComponentInParent<Lasertag>();
            if (hitLasertag != null)
            {
                Debug.Log($"Lasertag hit target team={hitLasertag.team}, local team={team}");
                if (!string.IsNullOrEmpty(hitLasertag.team) && hitLasertag.team != team)
                {
                    CmdReportHit(team);
                }
            }
            else
            {
                Debug.Log($"Lasertag raycast hit {hit.collider.gameObject.name}, but no Lasertag on parent.");
            }
        }
        else
        {
            Debug.Log("Lasertag: no target hit.");
        }

        SpawnShotSphere();
    }

    [Command]
    public void CmdReportHit(string teamToScore)
    {
        Debug.Log($"CmdReportHit called on server for team={teamToScore}");
        var manager = LasertagScoreManager.Instance;
        if (manager == null)
        {
            manager = FindObjectOfType<LasertagScoreManager>();
            if (manager == null)
            {
                Debug.LogWarning("Lasertag: ScoreManager not found on server.");
                return;
            }
        }

        manager.ServerAddScore(teamToScore);
    }

    private void SpawnShotSphere()
    {
        Vector3 spawnPosition = _camera.transform.position + _camera.transform.forward * 0.5f;
        Quaternion spawnRotation = Quaternion.identity;

        GameObject shot = null;
        if (_shotPrefab != null)
        {
            shot = Instantiate(_shotPrefab, spawnPosition, spawnRotation);
        }
        else
        {
            shot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shot.transform.position = spawnPosition;
            shot.transform.localScale = Vector3.one * _shotScale;
            Collider shotCollider = shot.GetComponent<Collider>();
            if (shotCollider != null)
                shotCollider.isTrigger = true;

            Renderer shotRenderer = shot.GetComponent<Renderer>();
            if (shotRenderer != null)
            {
                shotRenderer.material = new Material(Shader.Find("Standard")) { color = Color.yellow };
            }
        }

        if (shot != null)
        {
            SetLayerRecursively(shot, LayerMask.NameToLayer("Ignore Raycast"));

            Rigidbody rb = shot.GetComponent<Rigidbody>();
            if (rb == null)
                rb = shot.AddComponent<Rigidbody>();

            rb.useGravity = false;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.linearVelocity = _camera.transform.forward * _shotSpeed;
            Destroy(shot, _shotDuration);
        }
    }

    private void SetLayerRecursively(GameObject obj, int layer)
    {
        if (obj == null || layer < 0) return;
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    [Command]
    public void CmdSetTeam(string teamName)
    {
        team = teamName;
    }
}
