using System.Reflection;
using System.Runtime.InteropServices;
using Mirror;
using UnityEngine;
using InfimaGames.LowPolyShooterPack;

namespace StarterAssets
{
    /// <summary>
    /// Мобильный (виртуальный) ввод.
    ///
    /// Виртуальные джойстики и кнопки вызывают методы этого компонента, а он передаёт
    /// значения напрямую персонажу Infima — тем же полям, которые заполняет PlayerInput
    /// для клавиатуры/мыши/геймпада (axisMovement, axisLook, holdingButtonRun).
    ///
    /// Раньше значения уходили только в StarterAssetsInputs. Но PlayerInput на префабе
    /// игрока работает в режиме «Invoke Unity Events» и зовёт Character.OnMove/OnLook/
    /// OnTryJump/OnTryRun, поэтому StarterAssetsInputs.move/jump/sprint никто не читает,
    /// и мобильные кнопки с джойстиками не работали. Поэтому пишем в оба места:
    /// в StarterAssetsInputs и напрямую в персонажа Infima.
    /// </summary>
    public class UICanvasControllerInput : NetworkBehaviour
    {
        [Header("Output")]
        public StarterAssetsInputs starterAssetsInputs;

        private Character character;
        private Movement movement;

        // Поля персонажа Infima приватные — работаем через reflection, как это уже
        // делалось для axisMovement/axisLook. Имена соответствуют Character.cs.
        private static FieldInfo axisMovementField;
        private static FieldInfo axisLookField;
        private static FieldInfo holdingButtonRunField;
        private static FieldInfo cursorLockedField;

        [SerializeField] private float cameraJoystickSensivity = 1f;

        [Header("Editor debug")]
        [Tooltip("Для теста мобильного UI в редакторе/на ПК. В новой логике редактор всегда показывает мобильный UI, а ПК-билд — нет.")]
        public bool forceShowMobileControlsInEditor = true;

        [Header("Mobile root")]
        [Tooltip("Если назначить сюда отдельный корневой объект мобильного UI, скрипт будет включать/выключать именно его, а не сам компонент. Если оставить пустым — сам объект не будет выключаться автоматически.")]
        public GameObject mobileRoot;

        // ---------------------------------------------------------------------
        // WebGL: определение мобильного браузера через .jslib
        // ---------------------------------------------------------------------
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int IsMobileBrowser();
#endif

        private static bool DetectWebGLMobileBrowser()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                return IsMobileBrowser() == 1;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[UICanvasControllerInput] IsMobileBrowser() failed: " + e.Message);
                return false;
            }
#else
            return false;
#endif
        }

        private GameObject GetMobileRoot()
        {
            return mobileRoot != null ? mobileRoot : gameObject;
        }

        private bool ShouldShowMobileControls()
        {
            // В редакторе мобильный UI должен быть виден для тестов (управляется флагом).
            if (Application.isEditor)
                return forceShowMobileControlsInEditor;

            // На реальном мобильном устройстве — всегда показываем.
            if (Application.isMobilePlatform)
                return true;

            if (SystemInfo.deviceType == DeviceType.Handheld)
                return true;

            if (Application.platform == RuntimePlatform.Android ||
                Application.platform == RuntimePlatform.IPhonePlayer)
                return true;

            // WebGL: спрашиваем у браузера.
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                return DetectWebGLMobileBrowser();

            // На ПК/десктопе — мобильный HUD выключен.
            return false;
        }

        private void Awake()
        {
            CacheCharacterFields();
            ApplyMobileControlsVisibility();
        }

        private void Start()
        {
            ApplyMobileControlsVisibility();
        }

        private void OnEnable()
        {
            ApplyMobileControlsVisibility();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            ApplyMobileControlsVisibility();
        }

        private void ApplyMobileControlsVisibility()
        {
            bool shouldBeActive = ShouldShowMobileControls();
            GameObject root = GetMobileRoot();

            if (root == null)
                return;

            if (root == gameObject)
            {
                // Если mobileRoot не назначен, скрипт не должен форсировать активность
                // всего Canvas'а или некой родительской иерархии. Это мешает сцене
                // корректно управлять видимостью интерфейса.
                if (gameObject.activeSelf != shouldBeActive)
                    gameObject.SetActive(shouldBeActive);
                return;
            }

            // Важно: не включать родительские объекты по цепочке и не форсировать
            // весь canvas в active=true. Если в сцене корневой Canvas/Panel специально
            // скрыт, этот скрипт не должен переопределять это состояние.
            if (root.activeSelf != shouldBeActive)
                root.SetActive(shouldBeActive);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ResolveLocalPlayer();
        }

        private void Update()
        {
            ApplyMobileControlsVisibility();

            if (!ShouldShowMobileControls())
                return;

            ResolveLocalPlayer();
        }

        private static void CacheCharacterFields()
        {
            if (axisMovementField == null)
                axisMovementField = typeof(Character).GetField("axisMovement", BindingFlags.Instance | BindingFlags.NonPublic);

            if (axisLookField == null)
                axisLookField = typeof(Character).GetField("axisLook", BindingFlags.Instance | BindingFlags.NonPublic);

            if (holdingButtonRunField == null)
                holdingButtonRunField = typeof(Character).GetField("holdingButtonRun", BindingFlags.Instance | BindingFlags.NonPublic);

            if (cursorLockedField == null)
                cursorLockedField = typeof(Character).GetField("cursorLocked", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private void ResolveLocalPlayer()
        {
            if (NetworkClient.localPlayer == null)
                return;

            GameObject playerObject = NetworkClient.localPlayer.gameObject;

            if (starterAssetsInputs == null)
                starterAssetsInputs = playerObject.GetComponent<StarterAssetsInputs>();

            if (character == null)
            {
                character = playerObject.GetComponent<Character>();
                if (character != null)
                    CacheCharacterFields();
            }

            if (movement == null)
                movement = playerObject.GetComponent<Movement>();
        }

        /// <summary>
        /// На телефоне курсора нет, но персонаж Infima игнорирует весь ввод, пока
        /// cursorLocked == false (так устроены OnMove/OnLook/SetFireInput/OnTryRun).
        /// Поэтому на мобильном всегда держим курсор «запертым».
        /// </summary>
        private void EnsureCursorLocked()
        {
            if (character == null || cursorLockedField == null)
                return;

            if (!(bool)cursorLockedField.GetValue(character))
                cursorLockedField.SetValue(character, true);
        }

        private void ApplyCharacterMovement(Vector2 input)
        {
            EnsureCursorLocked();

            if (character == null || axisMovementField == null)
                return;

            axisMovementField.SetValue(character, input);
        }

        private void ApplyCharacterLook(Vector2 input)
        {
            EnsureCursorLocked();

            if (character == null || axisLookField == null)
                return;

            axisLookField.SetValue(character, input);
        }

        /// <summary>
        /// Бег в Infima: персонаж сам считает running = holdingButtonRun && CanRun(),
        /// поэтому достаточно выставить этот флаг — ровно как это делает OnTryRun
        /// </summary>
        private void ApplyCharacterRun(bool input)
        {
            EnsureCursorLocked();

            if (character == null || holdingButtonRunField == null)
                return;

            holdingButtonRunField.SetValue(character, input);
        }

        public void VirtualMoveInput(Vector2 virtualMoveDirection)
        {
            Vector2 move = Vector2.ClampMagnitude(virtualMoveDirection, 1f);

            if (starterAssetsInputs != null)
                starterAssetsInputs.MoveInput(move);

            ApplyCharacterMovement(move);
        }

        public void VirtualLookInput(Vector2 virtualLookDirection)
        {
            Vector2 look = Vector2.ClampMagnitude(virtualLookDirection * cameraJoystickSensivity, 1f);

            if (starterAssetsInputs != null)
                starterAssetsInputs.LookInput(look);

            ApplyCharacterLook(look);
        }

        public void VirtualJumpInput(bool virtualJumpState)
        {
            if (starterAssetsInputs != null)
                starterAssetsInputs.JumpInput(virtualJumpState);

            if (virtualJumpState)
                EnsureCursorLocked();

            if (!virtualJumpState || movement == null)
                return;

            // Infima прыгает так же, как по пробелу: Character.OnTryJump зовёт movement.Jump().
            movement.Jump();
        }

        public void VirtualSprintInput(bool virtualSprintState)
        {
            EnsureCursorLocked();

            if (starterAssetsInputs != null)
                starterAssetsInputs.SprintInput(virtualSprintState);

            ApplyCharacterRun(virtualSprintState);
        }

        /// <summary>
        /// Кнопка выстрела на мобильном/виртуальном интерфейсе — ровно так же, как
        /// джойстик (VirtualMoveInput), прыжок и бег выше.
        /// </summary>
        public void VirtualShootInput(bool virtualShootState)
        {
            // Если персонажа Infima нет (например, футбольный префаб) — остаётся записать
            // флаг в StarterAssetsInputs, его прочитает тот, кто умеет стрелять.
            if (character == null)
            {
                if (starterAssetsInputs != null)
                    starterAssetsInputs.ShootInput(virtualShootState);
                return;
            }

            EnsureCursorLocked();
            character.SetFireInput(virtualShootState);

            if (starterAssetsInputs != null)
                starterAssetsInputs.shoot = virtualShootState;
        }
    }
}