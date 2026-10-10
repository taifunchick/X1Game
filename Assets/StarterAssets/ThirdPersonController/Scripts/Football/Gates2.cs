using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using TMPro;

public class Gates2 : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnVariableChanged))]
    public int score = 0;
    [SerializeField] private TextMeshProUGUI _TMP;

    private void OnVariableChanged(int oldValue, int newValue)
    {
        // _TMP может быть не назначен, а hook вызывается на каждом изменении счёта:
        // без проверки это NullReferenceException на каждый гол, то есть поток ошибок в лог.
        if (_TMP == null)
            return;

        if (X1Log.InfoEnabled)
            Debug.Log($"Variable changed from {oldValue} to {newValue}");

        _TMP.text = score.ToString();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (_TMP != null)
            _TMP.text = score.ToString();
    }



    private void OnTriggerEnter(Collider other)
    {
        if (isServer)
        {
            if (other.gameObject.CompareTag("Ball"))
            {
                score += 1;
            }
        }
    }
}


