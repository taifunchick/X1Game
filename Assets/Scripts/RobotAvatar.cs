using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// Визуал игрока: робот в майке цвета команды вместо прежнего вертикального овала-капсулы.
///
/// Зачем это здесь, а не в префабе вручную: капсулу Infima (дочерний объект «Capsule»
/// с компонентом ColorChanger) видно только сбоку — на экране соперники выглядели
/// белыми вертикальными овалами. Теперь из кода собирается простой робот
/// (голова, корпус-майка, руки, штаны), а корпус и рукава красятся в красный или синий
/// цвет команды из NetworkCombatPlayer.team.
///
/// Скрипт висит на корне префаба игрока (P_LPSP_FP_CH). Он не трогает ни физику, ни
/// попадания: у робота нет коллайдеров, и он лежит на слое Ignore Raycast, поэтому
/// стрельба по-прежнему считается на сервере геометрически (DistanceToBody), а не по мешу.
///
/// Скины. Когда появятся перерисованные модели роботов, достаточно положить такой префаб
/// в поле «Skin Prefab» (визуал без NetworkIdentity!) — код его просто создаст как дочерний
/// объект. Если внутри скина есть дочерний объект с именем «Jersey», его материалы будут
/// перекрашиваться в цвет команды; остальная модель останется как нарисована.
/// </summary>
[DisallowMultipleComponent]
public class RobotAvatar : NetworkBehaviour
{
    [Header("Скин")]
    [Tooltip("Префаб визуала робота БЕЗ NetworkIdentity и без NetworkBehaviour — только меши. " +
             "Пусто — соберём простого робота из примитивов прямо в игре.")]
    [SerializeField] private GameObject _skinPrefab;

    [Tooltip("Имя дочернего объекта скина, материалы которого красятся в цвет команды. " +
             "Если такого объекта нет, скин не перекрашивается.")]
    [SerializeField] private string _jerseyObjectName = "Jersey";

    [Header("Показ")]
    [Tooltip("Не показывать робота самому игроку: в лазертаге камера от первого лица, и корпус " +
             "собственного робота закрывал бы прицел. Соперники видят его как обычно.")]
    [SerializeField] private bool _hideForLocalPlayer = true;

    [Tooltip("Спрятать старую капсулу Infima (её видно как вертикальный овал). " +
             "Капсулу заменяет робот.")]
    [SerializeField] private bool _hideLegacyCapsule = true;

    [Tooltip("Общий масштаб визуала робота. 1 — рост около 1.8 м, как у капсулы.")]
    [SerializeField] private float _scale = 1f;

    [Tooltip("Смещение визуала относительно корня игрока (метры).")]
    [SerializeField] private Vector3 _offset = Vector3.zero;

    [Header("Цвета команды")]
    [SerializeField] private Color _redJersey = new Color(0.85f, 0.13f, 0.13f);
    [SerializeField] private Color _blueJersey = new Color(0.13f, 0.33f, 0.9f);
    [Tooltip("Майка, пока команда не выбрана.")]
    [SerializeField] private Color _neutralJersey = new Color(0.72f, 0.73f, 0.76f);

    [Header("Цвета корпуса")]
    [SerializeField] private Color _trousersColor = new Color(0.13f, 0.14f, 0.17f);
    [SerializeField] private Color _metalColor = new Color(0.62f, 0.64f, 0.68f);
    [SerializeField] private Color _visorColor = new Color(0.1f, 0.8f, 0.9f);

    private const string IgnoreRaycastLayerName = "Ignore Raycast";

    private Transform _avatarRoot;
    private Material _jerseyMaterial;              // свой материал у каждого игрока
    private readonly List<MeshRenderer> _jerseyRenderers = new List<MeshRenderer>();
    private string _appliedTeam;

    private NetworkCombatPlayer _combat;
    private bool _built;

    // Общие материалы корпуса: одинаковы у всех игроков, поэтому создаются один раз.
    private static Material _staticTrousers;
    private static Material _staticMetal;
    private static Material _staticVisor;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    public override void OnStartClient()
    {
        base.OnStartClient();

        // На выделенном сервере (-nographics) визуал некому показывать: не строим.
        if (Mirror.Utils.IsHeadless())
            return;

        Build();
        ApplyTeam(_combat != null ? _combat.team : string.Empty);
    }

    private void Update()
    {
        if (!_built)
            return;

        // Команду можно выбрать уже после спавна (кнопка в меню или панель в сцене),
        // поэтому следим за SyncVar. Сравнение строк раз в кадр дешевле, чем событие
        // в NetworkCombatPlayer, и не требует менять его SyncVar-хуки.
        string team = _combat != null ? _combat.team : string.Empty;
        if (!string.Equals(team, _appliedTeam))
            ApplyTeam(team);
    }

    #region Сборка визуала

    private void Build()
    {
        if (_built)
            return;

        if (_combat == null)
            _combat = GetComponent<NetworkCombatPlayer>();

        _avatarRoot = new GameObject("RobotAvatar").transform;
        _avatarRoot.SetParent(transform, false);
        _avatarRoot.localPosition = _offset;
        _avatarRoot.localRotation = Quaternion.identity;
        _avatarRoot.localScale = Vector3.one * Mathf.Max(0.01f, _scale);

        if (_skinPrefab != null)
            BuildFromSkin();
        else
            BuildProceduralRobot();

        if (_avatarRoot.childCount == 0)
        {
            // Ничего не собралось (например, скин пуст) — капсулу не трогаем, чтобы
            // игрок вообще не остался без визуала.
            Destroy(_avatarRoot.gameObject);
            _avatarRoot = null;
            _built = false;
            return;
        }

        // Слой ставим уже после сборки: дети скина приходят со слоями из префаба.
        SetLayerRecursively(_avatarRoot.gameObject, LayerMask.NameToLayer(IgnoreRaycastLayerName));

        // Капсулу гасим только когда робот реально собран.
        if (_hideLegacyCapsule)
            HideLegacyCapsule();

        _built = true;
        RefreshVisibility();
    }

    /// <summary>
    /// Старая капсула Infima. Гасим только рендер (сам объект оставляем активным,
    /// чтобы не поломать SyncVar-хук ColorChanger).
    /// </summary>
    private void HideLegacyCapsule()
    {
        ColorChanger colorChanger = GetComponentInChildren<ColorChanger>(true);
        if (colorChanger == null)
            return;

        MeshRenderer capsuleRenderer = colorChanger.GetComponent<MeshRenderer>();
        if (capsuleRenderer != null)
            capsuleRenderer.enabled = false;
    }

    private void BuildFromSkin()
    {
        GameObject skin = Instantiate(_skinPrefab, _avatarRoot, false);
        skin.name = "Skin";
        skin.transform.localPosition = Vector3.zero;
        skin.transform.localRotation = Quaternion.identity;

        // Скин без NetworkIdentity — на всякий случай выключаем всё сетевое в нём.
        foreach (NetworkIdentity identity in skin.GetComponentsInChildren<NetworkIdentity>(true))
            identity.enabled = false;

        Transform jersey = FindChildByName(skin.transform, _jerseyObjectName);
        if (jersey == null)
            return;

        _jerseyMaterial = CreateMaterial(Color.white);
        foreach (MeshRenderer renderer in jersey.GetComponentsInChildren<MeshRenderer>(true))
        {
            renderer.sharedMaterial = _jerseyMaterial;
            _jerseyRenderers.Add(renderer);
        }
    }

    /// <summary>
    /// Простой робот из примитивов: рост ≈ 1.8 м, корпус и рукава — майка (цвет команды),
    /// ноги — тёмные, голова и предплечья — металл, на «лице» светящаяся полоса-визор.
    /// </summary>
    private void BuildProceduralRobot()
    {
        _jerseyMaterial = CreateMaterial(Color.white);
        Transform jerseyRoot = new GameObject("Jersey").transform;
        jerseyRoot.SetParent(_avatarRoot, false);

        // Корпус — майка.
        AddBox(jerseyRoot, "Torso", new Vector3(0f, 1.28f, 0f), new Vector3(0.44f, 0.54f, 0.26f), null);

        // Плечи и рукава.
        AddBox(jerseyRoot, "Sleeve L", new Vector3(-0.29f, 1.43f, 0f), new Vector3(0.13f, 0.24f, 0.15f), null);
        AddBox(jerseyRoot, "Sleeve R", new Vector3(0.29f, 1.43f, 0f), new Vector3(0.13f, 0.24f, 0.15f), null);

        Material metal = SharedMetalMaterial();
        Material trousers = SharedTrousersMaterial();
        Material visor = SharedVisorMaterial();

        // Предплечья и кисти.
        AddBox(_avatarRoot, "Forearm L", new Vector3(-0.29f, 1.14f, 0f), new Vector3(0.11f, 0.32f, 0.12f), metal);
        AddBox(_avatarRoot, "Forearm R", new Vector3(0.29f, 1.14f, 0f), new Vector3(0.11f, 0.32f, 0.12f), metal);

        // Голова, визор, антенна.
        AddBox(_avatarRoot, "Head", new Vector3(0f, 1.68f, 0f), new Vector3(0.26f, 0.24f, 0.24f), metal);
        AddBox(_avatarRoot, "Visor", new Vector3(0f, 1.70f, 0.12f), new Vector3(0.20f, 0.07f, 0.02f), visor);
        AddBox(_avatarRoot, "Antenna", new Vector3(0.07f, 1.86f, 0f), new Vector3(0.02f, 0.14f, 0.02f), metal);

        // Шея, таз, ноги, ступни.
        AddBox(_avatarRoot, "Neck", new Vector3(0f, 1.55f, 0f), new Vector3(0.12f, 0.08f, 0.12f), metal);
        AddBox(_avatarRoot, "Hips", new Vector3(0f, 0.94f, 0f), new Vector3(0.38f, 0.18f, 0.24f), trousers);
        AddBox(_avatarRoot, "Leg L", new Vector3(-0.11f, 0.5f, 0f), new Vector3(0.15f, 0.7f, 0.17f), trousers);
        AddBox(_avatarRoot, "Leg R", new Vector3(0.11f, 0.5f, 0f), new Vector3(0.15f, 0.7f, 0.17f), trousers);
        AddBox(_avatarRoot, "Foot L", new Vector3(-0.11f, 0.06f, 0.04f), new Vector3(0.16f, 0.1f, 0.28f), trousers);
        AddBox(_avatarRoot, "Foot R", new Vector3(0.11f, 0.06f, 0.04f), new Vector3(0.16f, 0.1f, 0.28f), trousers);

        // Рюкзак: он же «спина» робота — сразу видно, куда игрок смотрит.
        AddBox(_avatarRoot, "Backpack", new Vector3(0f, 1.32f, -0.17f), new Vector3(0.3f, 0.34f, 0.1f), metal);
    }

    private void AddBox(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;

        // Коллайдер не нужен: попадания считает сервер геометрически, а лишний
        // коллайдер мог бы перехватывать лучи и портить регистрацию попаданий.
        Collider collider = box.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPosition;
        box.transform.localRotation = Quaternion.identity;
        box.transform.localScale = localScale;
        box.layer = LayerMask.NameToLayer(IgnoreRaycastLayerName);

        MeshRenderer renderer = box.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material != null ? material : _jerseyMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;

            if (material == null)
                _jerseyRenderers.Add(renderer);
        }
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        if (target == null || layer < 0)
            return;

        target.layer = layer;
        foreach (Transform child in target.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    private static Transform FindChildByName(Transform root, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(child.name, name, System.StringComparison.OrdinalIgnoreCase))
                return child;
        }

        return null;
    }

    #endregion

    #region Цвет команды и видимость

    private void ApplyTeam(string team)
    {
        _appliedTeam = team;

        Color jersey = _neutralJersey;
        if (string.Equals(team, TeamSelection.Red, System.StringComparison.OrdinalIgnoreCase))
            jersey = _redJersey;
        else if (string.Equals(team, TeamSelection.Blue, System.StringComparison.OrdinalIgnoreCase))
            jersey = _blueJersey;

        if (_jerseyMaterial != null)
            SetMaterialColor(_jerseyMaterial, jersey);

        // У скина майка может состоять из нескольких материалов.
        for (int i = 0; i < _jerseyRenderers.Count; i++)
        {
            MeshRenderer renderer = _jerseyRenderers[i];
            if (renderer == null || renderer.sharedMaterial == null)
                continue;

            if (renderer.sharedMaterial != _jerseyMaterial)
                SetMaterialColor(renderer.sharedMaterial, jersey);
        }

        RefreshVisibility();
    }

    private void RefreshVisibility()
    {
        if (_avatarRoot == null)
            return;

        bool hideForMe = _hideForLocalPlayer && isLocalPlayer;
        _avatarRoot.gameObject.SetActive(!hideForMe);
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        RefreshVisibility();
    }

    #endregion

    #region Материалы

    private static void SetMaterialColor(Material material, Color color)
    {
        color.a = 1f;

        if (material == null)
            return;

        if (material.HasProperty(BaseColorId))
            material.SetColor(BaseColorId, color);

        if (material.HasProperty(ColorId))
            material.SetColor(ColorId, color);

        // На всякий случай: Material.color работает только если у шейдера есть _Color.
        if (!material.HasProperty(BaseColorId) && !material.HasProperty(ColorId))
            material.color = color;
    }

    private static Material CreateMaterial(Color color)
    {
        Shader shader = FindLitShader();
        if (shader == null)
        {
            Debug.LogWarning("[RobotAvatar] Не найден ни один подходящий шейдер (URP/Lit, Standard) — " +
                             "робот останется с материалом по умолчанию.");
            return null;
        }

        Material material = new Material(shader) { name = "X1 Robot Material" };
        SetMaterialColor(material, color);
        return material;
    }

    /// <summary>
    /// Проект на URP, поэтому сначала URP/Lit. Standard оставлен запасным вариантом
    /// для сборок, где URP-шейдер вырезан стриппингом.
    /// </summary>
    private static Shader FindLitShader()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        return shader;
    }

    // Материал корпуса одинаков у всех игроков, поэтому он один на всех (статический кэш):
    // копия на каждого соперника — это лишние материалы и память.

    private Material SharedTrousersMaterial()
    {
        if (_staticTrousers == null)
        {
            _staticTrousers = CreateMaterial(_trousersColor);
            if (_staticTrousers != null)
                _staticTrousers.name = "X1 Robot Trousers";
        }
        return _staticTrousers;
    }

    private Material SharedMetalMaterial()
    {
        if (_staticMetal == null)
        {
            _staticMetal = CreateMaterial(_metalColor);
            if (_staticMetal != null)
                _staticMetal.name = "X1 Robot Metal";
        }
        return _staticMetal;
    }

    private Material SharedVisorMaterial()
    {
        if (_staticVisor == null)
        {
            _staticVisor = CreateMaterial(_visorColor);
            if (_staticVisor != null)
                _staticVisor.name = "X1 Robot Visor";
        }
        return _staticVisor;
    }

    #endregion
}
