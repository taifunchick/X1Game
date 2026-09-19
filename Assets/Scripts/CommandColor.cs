using UnityEngine;
using Mirror;

// Зона-триггер: красит зашедшего игрока в заданный цвет (цвет зоны = команда).
public class CommandColor : NetworkBehaviour
{
    [SerializeField] private Color _color; // цвет, в который красится игрок

    private void OnTriggerEnter(Collider other)
    {
        if (isServer)
        {
            if (other.gameObject.CompareTag("Player"))
            {
                // ColorChanger может висеть на дочерней Capsule — ищем по всему игроку.
                ColorChanger changer = other.gameObject.GetComponentInChildren<ColorChanger>();
                if (changer != null)
                    changer.SetColor(_color);
            }
        }
    }
}
