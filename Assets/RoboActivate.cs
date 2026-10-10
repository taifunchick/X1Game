using UnityEngine;
using Mirror;
using Newtonsoft.Json;

public class RoboActivate : NetworkBehaviour
{
    [SerializeField] private GameObject _robotPrefab;
    public string _name;

    private bool _isShuttingDown;

    public override void OnStartClient()
    {
        base.OnStartClient();
        string _name = PlayerPrefs.GetString("name");

        if (isLocalPlayer) 
        {
            CmdSpawnRobot(_name);
        }
    }


    [Command]
    private void CmdSpawnRobot(string name)
    {
        _name = name;
        // GameObject robo = Instantiate(_robotPrefab); 
        // NetworkServer.Spawn(robo); 
                                                                //UNCOMMENT ALL TO SPAVN ROBOT
        // robo.GetComponent<PlayerName>().enabled = true;  

        // robo.GetComponent<PlayerName>().Name = name;

        // Debug.Log("Spawn New Robot, with name: " + name);
    }

    private void OnDestroy()
    {
        // OnDestroy вызывается в том числе при выгрузке сцены и при выходе из приложения.
        // В этот момент искать объекты и тем более Destroy() их — небезопасно: можно получить
        // исключение на финализации, которое на выделенном сервере уронит процесс при остановке.
        if (_isShuttingDown)
            return;

        try
        {
            DestroyRobotByName();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("RoboActivate: не удалось убрать робота при уничтожении: " + e.Message);
        }
    }

    private void OnApplicationQuit()
    {
        _isShuttingDown = true;
    }

    private void DestroyRobotByName()
    {
        PlayerName[] RobotNames = FindObjectsByType<PlayerName>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        GameObject robo = null;

        foreach (PlayerName RobotName in RobotNames)
        {
            if (RobotName.Name == _name)
            {
                robo = RobotName.gameObject;
            }
        }

        if (robo != null)
        {
            Destroy(robo);

            if (X1Log.InfoEnabled)
                Debug.Log("Robot Destroyed");
        }
        else if (X1Log.InfoEnabled)
        {
            Debug.Log("No Robot to destroy");
        }

    }
}
