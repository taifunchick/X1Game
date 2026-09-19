using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;
using InfimaGames.LowPolyShooterPack;

/// Add to the player prefab (P_LPSP_FP_CH).
///
/// Правила лазертага: умирать нельзя, здоровья нет. Задача — попасть в других как можно больше раз.
/// Каждое попадание в любого другого игрока даёт стрелку +1 к счётчику hits (кому попал — неважно).
///
/// Патроны: магазин берётся от оружия Infima (Character → Inventory → оружие),
/// каждый выстрел расходует патрон. Когда патроны закончились — выстрел не идёт
/// и попадание НЕ засчитывается; через reloadTime секунд магазин перезаряжается.
///
/// У префаба игрока нет коллайдера, только CharacterController, поэтому попадания
/// рассчитывает СЕРВЕР геометрически: проверяем, пролетел ли луч выстрела достаточно близко
/// к "колонне" тела игрока. Стены по-прежнему блокируют выстрел (raycast по окружению).
public class NetworkCombatPlayer : NetworkBehaviour
{
    [SyncVar] public string team = "";

    /// Сколько раз этот игрок попал в других. Меняет только сервер.
    [SyncVar] public int hits = 0;

    [Header("Выстрел")]
    [SerializeField] float range = 200f;
    [SerializeField] LayerMask hitMask = ~0;      // что может блокировать выстрел (стены, пол, ...)
    [SerializeField] Transform fireOrigin;        // запасная точка выстрела, если камеры нет

    [Header("Патроны")]
    [Tooltip("Размер магазина, если на игроке нет оружия Infima (Character → Inventory)")]
    [SerializeField] int defaultMagazineSize = 30;
    [Tooltip("Сколько секунд длится перезарядка после пустого магазина")]
    [SerializeField] float reloadTime = 2f;

    [Header("Попадание без коллайдера на игроке")]
    [Tooltip("Высота центра тела игрока над его position (m_Center.y у CharacterController = 1)")]
    [SerializeField] float centerHeight = 1f;
    [Tooltip("Радиус попадания: радиус CharacterController (0.3) + запас")]
    [SerializeField] float hitRadius = 0.5f;
    [Tooltip("Высота колонны тела, которую проверяем (высота CharacterController = 1.8)")]
    [SerializeField] float bodyHeight = 1.8f;

    private Character _character;   // Infima: от него берём оружие и размер магазина
    private int _localAmmo = -1;    // -1 = ещё не инициализировано
    private bool _isReloading;
    private float _reloadReadyTime;

    void Awake()
    {
        _character = GetComponent<Character>();
    }

    void Update()
    {
        if (!isLocalPlayer) return;

        // Доводим перезарядку до конца (магазин становится полным).
        FinishReloadIfNeeded();

        if (NetworkRoundManager.Instance != null && NetworkRoundManager.Instance.finished) return;
        if (!Input.GetMouseButtonDown(0)) return;
        // Клик по UI (выбор команды, кнопки) не должен стрелять.
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        // Патроны: если магазин пуст — выстрела не будет и попадание не засчитается.
        if (!TryConsumeAmmo()) return;

        Vector3 origin;
        Vector3 direction;
        if (Camera.main != null)
        {
            origin = Camera.main.transform.position;
            direction = Camera.main.transform.forward;
        }
        else
        {
            origin = fireOrigin != null ? fireOrigin.position : transform.position;
            direction = transform.forward;
        }
        CmdFire(origin, direction);
    }

    /// Оружие Infima на этом игроке (может быть null, если оружие не готово).
    WeaponBehaviour LocalWeapon
    {
        get
        {
            if (_character == null)
                _character = GetComponent<Character>();
            InventoryBehaviour inventory = _character != null ? _character.GetInventory() : null;
            return inventory != null ? inventory.GetEquipped() : null;
        }
    }

    /// Вместимость магазина: от оружия Infima, fallback — defaultMagazineSize.
    int MagazineCapacity()
    {
        WeaponBehaviour weapon = LocalWeapon;
        if (weapon != null)
        {
            int total = weapon.GetAmmunitionTotal();
            if (total > 0)
                return total;
        }
        return Mathf.Max(1, defaultMagazineSize);
    }

    /// Перезарядка завершилась — магазин снова полный.
    void FinishReloadIfNeeded()
    {
        if (!_isReloading || Time.time < _reloadReadyTime) return;

        _isReloading = false;
        _localAmmo = MagazineCapacity();

        WeaponBehaviour weapon = LocalWeapon;
        if (weapon != null)
            weapon.FillAmmunition(0); // докинуть патроны в Magazine Infima
    }

    /// Снимает один патрон. false = патронов нет (выстрел запрещён).
    bool TryConsumeAmmo()
    {
        if (_localAmmo < 0)
            _localAmmo = MagazineCapacity();

        if (_localAmmo <= 0)
        {
            // Патроны закончились: попадание не засчитываем, запускаем перезарядку.
            if (!_isReloading)
            {
                _isReloading = true;
                _reloadReadyTime = Time.time + Mathf.Max(0.1f, reloadTime);
                WeaponBehaviour reloadWeapon = LocalWeapon;
                if (reloadWeapon != null)
                    reloadWeapon.Reload(); // анимация и звук перезарядки, если они есть
            }
            return false;
        }

        _localAmmo--;
        WeaponBehaviour weapon = LocalWeapon;
        if (weapon != null)
            weapon.FillAmmunition(-1); // синхронно уменьшаем Magazine Infima
        return true;
    }

    [Command]
    public void CmdFire(Vector3 origin, Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.5f) return;
        direction.Normalize();

        // Сферы-выстрелы лежат на Ignore Raycast и не должны блокировать другие выстрелы.
        int shotLayerBit = 1 << LayerMask.NameToLayer("Ignore Raycast");
        int mask = hitMask.value & ~shotLayerBit;

        // 1) Raycast: если CharacterController виден для raycast, первый игрок на луче —
        //    и есть цель. Иначе raycast находит стену и выстрел не пролетает сквозь неё.
        float wallDistance = float.MaxValue;
        if (Physics.Raycast(origin, direction, out RaycastHit hit, range, mask, QueryTriggerInteraction.Ignore))
        {
            var target = hit.collider.GetComponentInParent<NetworkCombatPlayer>();
            if (target != null && target != this)
            {
                RegisterHit(target);
                return;
            }
            wallDistance = hit.distance;
        }

        // 2) У игрока нет коллайдера — геометрия: ищем ближайшего игрока, чьё тело
        //    луч выстрела "задевает" в пределах hitRadius.
        float maxDistance = Mathf.Min(range, wallDistance);
        NetworkCombatPlayer closest = null;
        float closestDistance = float.MaxValue;

        NetworkCombatPlayer[] players = FindObjectsOfType<NetworkCombatPlayer>();
        foreach (var other in players)
        {
            if (other == null || other == this) continue;

            float dist = DistanceToBody(origin, direction, maxDistance, other);
            if (dist >= 0f && dist < closestDistance)
            {
                closest = other;
                closestDistance = dist;
            }
        }

        if (closest != null)
            RegisterHit(closest);
    }

    /// Вернёт расстояние вдоль луча до тела игрока, если луч проходит мимо тела
    /// на расстоянии <= hitRadius; иначе -1. Тело = вертикальная колонна (капсула)
    /// высотой bodyHeight вокруг centerHeight, проверяем по нескольким точкам —
    /// так попадание засчитывается в любую часть тела (в ноги, в голову).
    [Server]
    float DistanceToBody(Vector3 origin, Vector3 direction, float maxDistance, NetworkCombatPlayer target)
    {
        Vector3 basePos = target.transform.position;
        float half = bodyHeight * 0.5f;
        float best = -1f;

        const int samples = 5;
        for (int i = 0; i < samples; i++)
        {
            float h = centerHeight - half + bodyHeight * (i / (float)(samples - 1));
            Vector3 point = basePos + Vector3.up * h;

            float along = Vector3.Dot(point - origin, direction);
            if (along <= 0f || along > maxDistance) continue;

            float perpendicular = (point - (origin + direction * along)).magnitude;
            if (perpendicular <= hitRadius && (best < 0f || along < best))
                best = along;
        }
        return best;
    }

    /// Сервер: стрелку +1. Здоровья и смерти нет — только попадания.
    [Server]
    void RegisterHit(NetworkCombatPlayer victim)
    {
        hits++;
        Debug.Log($"[Combat] {name} (team={team}) попал в {victim.name}. Всего попаданий: {hits}");
    }

    /// Команду присылает UI выбора команды (Метеор / Вымпел).
    [Command]
    public void CmdSetTeam(string teamName)
    {
        if (string.IsNullOrWhiteSpace(teamName) || team == teamName) return;
        team = teamName;
        Debug.Log($"[Combat] {name}: назначена команда {teamName}");
    }
}
