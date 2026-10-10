using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class Reset : NetworkBehaviour
{
    [SerializeField] private Gates2 _gates;
    [SerializeField] private Gates2 _gates1;

    private void OnTriggerEnter(Collider other)
    {
        if (isServer)
        {
            if (other.gameObject.CompareTag("Player"))
            {
                // Ссылки назначаются в инспекторе и могут отсутствовать: без проверки это
                // NullReferenceException на каждый вход игрока в триггер, то есть поток ошибок
                // в лог сервера (а лог на headless пишется синхронно).
                if (_gates != null)
                    _gates.score = 0;
                else
                    Debug.LogWarning("Reset: не назначен _gates — счёт первых ворот не сброшен.");

                if (_gates1 != null)
                    _gates1.score = 0;
                else
                    Debug.LogWarning("Reset: не назначен _gates1 — счёт вторых ворот не сброшен.");
            }
        }
    }
}
