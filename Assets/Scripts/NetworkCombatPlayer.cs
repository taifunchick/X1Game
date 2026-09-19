using System.Collections.Generic;
using Mirror;
using UnityEngine;
using InfimaGames.LowPolyShooterPack;

/// Add to the player prefab (P_LPSP_FP_CH).
///
/// Правила лазертага: умирать нельзя, здоровья нет. Задача — попасть в других как можно больше раз.
/// Каждое попадание в любого другого игрока даёт стрелку +1 к счётчику hits (кому попал — неважно).
///
/// Патроны. Единственный источник правды — оружие Infima (Character → Inventory → оружие):
/// именно оно считает патроны, показывает их в HUD, играет анимацию и перезарядку (R).
/// Этот компонент НЕ ведёт свой счётчик патронов и НЕ слушает кнопку мыши — он подписан на
/// событие WeaponBehaviour.ShotFired, которое оружие поднимает только тогда, когда выстрел
/// действительно произошёл и патрон действительно потрачен. Отсюда следствия:
///  - патронов нет  → оружие не стреляет → попадания нет, как бы игрок ни спамил мышкой;
///  - очередь (зажал кнопку на автоматическом оружии) → проверяется КАЖДЫЙ выстрел очереди,
///    а не только первый;
///  - дробовик/гранатомёт (несколько пуль за выстрел) → это один выстрел, одно попадание;
///  - перезарядка — штатная, Infima, никаких своих таймеров, которые могут разойтись с HUD.
///
/// У префаба игрока нет коллайдера, только CharacterController, поэтому попадания
/// рассчитывает СЕРВЕР геометрически: проверяем, пролетел ли луч выстрела достаточно близко
/// к "колонне" тела игрока. Стены по-прежнему блокируют выстрел (raycast по окружению).
public class NetworkCombatPlayer : NetworkBehaviour
{
    /// <summary>
    /// Все активные игроки в сцене. Есть и на сервере, и на клиенте.
    /// Заменяет FindObjectsOfType, который раньше вызывался на каждый выстрел и каждый кадр.
    /// </summary>
    private static readonly List<NetworkCombatPlayer> instances = new List<NetworkCombatPlayer>();

    /// <summary>Все активные NetworkCombatPlayer в сцене.</summary>
    public static IReadOnlyList<NetworkCombatPlayer> Instances => instances;

    [SyncVar] public string team = "";

    /// Сколько раз этот игрок попал в других. Меняет только сервер.
    [SyncVar] public int hits = 0;

    [Header("Выстрел")]
    [SerializeField] float range = 200f;
    [SerializeField] LayerMask hitMask = ~0;      // что может блокировать выстрел (стены, пол, ...)
    [SerializeField] Transform fireOrigin;        // запасная точка выстрела, если камеры нет

    [Header("Защита от спама на сервере")]
    [Tooltip("Минимальный интервал между выстрелами одного игрока, который принимает сервер. " +
             "Самое быстрое оружие Infima — 800 выстрелов/мин (0.075 c), поэтому честной стрельбе " +
             "это не мешает, а спам командами от взломанного клиента отсекается.")]
    [SerializeField] float minShotInterval = 0.05f;

    [Header("Попадание без коллайдера на игроке")]
    [Tooltip("Высота центра тела игрока над его position (m_Center.y у CharacterController = 1)")]
    [SerializeField] float centerHeight = 1f;
    [Tooltip("Радиус попадания: радиус CharacterController (0.3) + запас")]
    [SerializeField] float hitRadius = 0.5f;
    [Tooltip("Высота колонны тела, которую проверяем (высота CharacterController = 1.8)")]
    [SerializeField] float bodyHeight = 1.8f;

    private Character _character;                 // Infima: от него берём оружие и камеру
    private double _lastShotServerTime = double.MinValue;
    private bool _warnedNoWeapon;

    // Оружия, на событие выстрела которых мы подписаны, и временный список для поиска.
    private readonly List<WeaponBehaviour> subscribedWeapons = new List<WeaponBehaviour>();
    private readonly List<WeaponBehaviour> foundWeapons = new List<WeaponBehaviour>();

    void Awake()
    {
        _character = GetComponent<Character>();
    }

    void OnEnable()
    {
        if (!instances.Contains(this))
            instances.Add(this);
    }

    void OnDisable()
    {
        instances.Remove(this);
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        EnsureWeaponSubscriptions();
    }

    void OnDestroy()
    {
        UnsubscribeWeapons();
    }

    void Update()
    {
        if (!isLocalPlayer) return;

        // Оружие можно переключить (колесо мыши / X) — следим, чтобы его выстрелы долетали до сервера.
        EnsureWeaponSubscriptions();
    }

    /// Оружие Infima, которое сейчас в руках (может быть null, если инвентарь не готов).
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

    /// <summary>
    /// Подписка на выстрелы ВСЕХ оружий игрока, а не только текущего.
    /// Так переключение оружия не может потерять выстрел из-за порядка Update, а патроны
    /// каждое оружие считает свои — нас интересует только факт реального выстрела.
    /// </summary>
    void EnsureWeaponSubscriptions()
    {
        // Ничего не пересобираем, пока текущее оружие уже под подпиской (обычный случай).
        WeaponBehaviour equipped = LocalWeapon;
        if (equipped != null && subscribedWeapons.Contains(equipped))
        {
            _warnedNoWeapon = false;
            return;
        }

        // Оружия в руках пока нет (инвентарь ещё не инициализирован), но подписки мы уже собрали.
        // Пересобирать их каждый кадр смысла нет.
        if (equipped == null && subscribedWeapons.Count > 0)
            return;

        // Ищем оружия заново. Инвентарь Infima хранит их дочерними объектами префаба игрока,
        // часть из них выключена — поэтому includeInactive: true. Перегрузка со списком не мусорит.
        foundWeapons.Clear();
        GetComponentsInChildren<WeaponBehaviour>(true, foundWeapons);

        // Забываем те, которых больше нет.
        for (int i = subscribedWeapons.Count - 1; i >= 0; i--)
        {
            WeaponBehaviour weapon = subscribedWeapons[i];
            if (weapon != null && foundWeapons.Contains(weapon)) continue;

            if (weapon != null)
                weapon.ShotFired -= OnWeaponShot;
            subscribedWeapons.RemoveAt(i);
        }

        // Подписываемся на новые.
        foreach (WeaponBehaviour weapon in foundWeapons)
        {
            if (weapon == null || subscribedWeapons.Contains(weapon)) continue;

            weapon.ShotFired += OnWeaponShot;
            subscribedWeapons.Add(weapon);
        }

        // Совсем без оружия Infima выстрелов не бывает — попаданий тоже.
        if (subscribedWeapons.Count == 0)
            WarnNoWeaponOnce();
        else
            _warnedNoWeapon = false;
    }

    void UnsubscribeWeapons()
    {
        foreach (WeaponBehaviour weapon in subscribedWeapons)
        {
            if (weapon != null)
                weapon.ShotFired -= OnWeaponShot;
        }
        subscribedWeapons.Clear();
    }

    void WarnNoWeaponOnce()
    {
        if (_warnedNoWeapon) return;
        _warnedNoWeapon = true;
        Debug.LogWarning("[Combat] У локального игрока нет оружия Infima (Character → Inventory) — " +
                         "выстрелов и попаданий не будет.");
    }

    /// <summary>
    /// Оружие Infima РЕАЛЬНО выстрелило: патрон потрачен, анимация и эффекты сыграны.
    /// Это единственная точка, из которой выстрел уходит на сервер, поэтому попадание
    /// никогда не засчитывается без патрона и никогда не теряется внутри очереди.
    /// </summary>
    void OnWeaponShot(WeaponBehaviour weapon)
    {
        // Стрелять за нас может только наш собственный игрок.
        if (!isLocalPlayer) return;

        // Раунд закончен — попадания больше не считаем.
        if (RoundFinished()) return;

        if (!TryGetAim(out Vector3 origin, out Vector3 direction)) return;

        CmdFire(origin, direction);
    }

    /// Точка и направление выстрела. Берём мировую камеру Infima — по ней же летят пули оружия
    /// и по ней рисуется прицел, так что попадание совпадает с тем, что видит игрок.
    bool TryGetAim(out Vector3 origin, out Vector3 direction)
    {
        if (_character == null)
            _character = GetComponent<Character>();

        Camera aimCamera = _character != null ? _character.GetCameraWorld() : null;
        if (aimCamera == null)
            aimCamera = Camera.main;

        if (aimCamera != null)
        {
            origin = aimCamera.transform.position;
            direction = aimCamera.transform.forward;
        }
        else if (fireOrigin != null)
        {
            origin = fireOrigin.position;
            direction = fireOrigin.forward;
        }
        else
        {
            origin = transform.position + Vector3.up * centerHeight;
            direction = transform.forward;
        }

        return direction.sqrMagnitude > 0.0001f;
    }

    static bool RoundFinished()
    {
        return NetworkRoundManager.Instance != null && NetworkRoundManager.Instance.finished;
    }

    [Command]
    public void CmdFire(Vector3 origin, Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.5f) return;
        direction.Normalize();

        // Раунд закончен — попадания не считаем.
        if (RoundFinished()) return;

        /*
         * Анти-спам. Клиент отправляет выстрел только по событию реального выстрела оружия,
         * но доверять клиенту полностью нельзя: модифицированный клиент может слать CmdFire
         * пачками. Настоящее оружие Infima стреляет не чаще 800 выстрелов/мин (0.075 c),
         * поэтому всё, что приходит чаще minShotInterval, просто игнорируем.
         */
        if (NetworkTime.time - _lastShotServerTime < Mathf.Max(0f, minShotInterval))
            return;
        _lastShotServerTime = NetworkTime.time;

        // Сферы-выстрелы лежат на Ignore Raycast и не должны блокировать другие выстрелы.
        int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        int shotLayerBit = ignoreRaycastLayer >= 0 ? 1 << ignoreRaycastLayer : 0;
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

        for (int i = 0; i < instances.Count; i++)
        {
            NetworkCombatPlayer other = instances[i];
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
