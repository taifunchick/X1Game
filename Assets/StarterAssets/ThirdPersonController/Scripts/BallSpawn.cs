
using UnityEngine;
using Mirror;

public class BallSpawn : NetworkBehaviour
{
    [SerializeField] private GameObject _ballPrefab;

    [SerializeField] private Vector3 _position = new Vector3(0,0,0);

    [Tooltip("Спавнить мяч на сервере. В сцене Lasertag мяч не нужен: он там только ест CPU " +
             "(Rigidbody + NetworkRigidbodyUnreliable синхронизируются каждому клиенту каждый тик) " +
             "и катится/падает без ограничений. Для футбола оставьте включённым.")]
    [SerializeField] private bool _spawnOnServer = true;

    public override void OnStartServer()
    {
        if (!isServer) return;

        if (!_spawnOnServer) return;

        if (_ballPrefab == null)
        {
            Debug.LogWarning("BallSpawn: _ballPrefab не назначен — мяч не создан.");
            return;
        }

        GameObject ball = Instantiate(_ballPrefab, _position, Quaternion.identity);
        NetworkServer.Spawn(ball);
    }

}
