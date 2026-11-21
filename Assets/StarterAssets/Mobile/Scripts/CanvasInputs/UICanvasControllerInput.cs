using System.Reflection;
using Mirror;
using UnityEngine;
using InfimaGames.LowPolyShooterPack;

namespace StarterAssets
{
    public class UICanvasControllerInput : MonoBehaviour
    {
        [Header("Output")]
        public StarterAssetsInputs starterAssetsInputs;

        private Character character;
        private Movement movement;

        private static FieldInfo axisMovementField;
        private static FieldInfo axisLookField;
        private static FieldInfo holdingButtonRunField;
        private static FieldInfo cursorLockedField;

        [SerializeField] public float cameraJoystickSensivity = 1f;

        [Header("Editor debug")]
        public bool forceShowMobileControlsInEditor = true;

        [Header("Mobile root")]
        public GameObject mobileRoot;

        private GameObject GetMobileRoot()
        {
            return mobileRoot != null ? mobileRoot : gameObject;
        }

        private bool ShouldShowMobileControls()
        {
            if (Application.isEditor) return forceShowMobileControlsInEditor;
            // Единый закэшированный детектор (jslib + touchSupported + InputSystem),
            // см. Assets/Scripts/X1DeviceDetect.cs. Ничего не делает «молча».
            return X1DeviceDetect.IsMobileDevice();
        }

        private void Awake()
        {
            CacheCharacterFields();
            ApplyMobileControlsVisibility();
        }

        private void Start() => ApplyMobileControlsVisibility();
        private void OnEnable() => ApplyMobileControlsVisibility();

        private void ApplyMobileControlsVisibility()
        {
            bool shouldBeActive = ShouldShowMobileControls();
            GameObject root = GetMobileRoot();
            if (root == null) return;

            if (root == gameObject)
            {
                if (gameObject.activeSelf != shouldBeActive)
                    gameObject.SetActive(shouldBeActive);
                return;
            }

            if (root.activeSelf != shouldBeActive)
                root.SetActive(shouldBeActive);
        }

        private void Update()
        {
            ApplyMobileControlsVisibility();
            if (!ShouldShowMobileControls()) return;
            ResolveLocalPlayer();
        }

        private static void CacheCharacterFields()
        {
            if (axisMovementField == null) axisMovementField = typeof(Character).GetField("axisMovement", BindingFlags.Instance | BindingFlags.NonPublic);
            if (axisLookField == null) axisLookField = typeof(Character).GetField("axisLook", BindingFlags.Instance | BindingFlags.NonPublic);
            if (holdingButtonRunField == null) holdingButtonRunField = typeof(Character).GetField("holdingButtonRun", BindingFlags.Instance | BindingFlags.NonPublic);
            if (cursorLockedField == null) cursorLockedField = typeof(Character).GetField("cursorLocked", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private void ResolveLocalPlayer()
        {
            if (NetworkClient.localPlayer == null) return;
            GameObject playerObject = NetworkClient.localPlayer.gameObject;

            if (starterAssetsInputs == null) starterAssetsInputs = playerObject.GetComponent<StarterAssetsInputs>();
            if (character == null)
            {
                character = playerObject.GetComponent<Character>();
                if (character != null) CacheCharacterFields();
            }
            if (movement == null) movement = playerObject.GetComponent<Movement>();
        }

        private void EnsureCursorLocked()
        {
            if (character == null || cursorLockedField == null) return;
            if (!(bool)cursorLockedField.GetValue(character))
                cursorLockedField.SetValue(character, true);
        }

        private void ApplyCharacterMovement(Vector2 input)
        {
            EnsureCursorLocked();
            if (character == null || axisMovementField == null) return;
            axisMovementField.SetValue(character, input);
        }

        private void ApplyCharacterLook(Vector2 input)
        {
            EnsureCursorLocked();
            if (character == null || axisLookField == null) return;
            axisLookField.SetValue(character, input);
        }

        private void ApplyCharacterRun(bool input)
        {
            EnsureCursorLocked();
            if (character == null || holdingButtonRunField == null) return;
            holdingButtonRunField.SetValue(character, input);
        }

        public void VirtualMoveInput(Vector2 virtualMoveDirection)
        {
            Vector2 move = Vector2.ClampMagnitude(virtualMoveDirection, 1f);
            if (starterAssetsInputs != null) starterAssetsInputs.MoveInput(move);
            ApplyCharacterMovement(move);
        }

        public void VirtualLookInput(Vector2 virtualLookDirection)
        {
            // Возвращаем ограничение: джойстик камеры двигает ТОЛЬКО по горизонтали.
            Vector2 look = Vector2.ClampMagnitude(new Vector2(
                virtualLookDirection.x,
                0f) * cameraJoystickSensivity,
                1f);

            if (starterAssetsInputs != null)
                starterAssetsInputs.LookInput(look);

            ApplyCharacterLook(look);
        }

        public void VirtualJumpInput(bool virtualJumpState)
        {
            if (starterAssetsInputs != null) starterAssetsInputs.JumpInput(virtualJumpState);
            if (virtualJumpState) EnsureCursorLocked();
            if (!virtualJumpState || movement == null) return;
            movement.Jump();
        }

        public void VirtualSprintInput(bool virtualSprintState)
        {
            EnsureCursorLocked();
            if (starterAssetsInputs != null) starterAssetsInputs.SprintInput(virtualSprintState);
            ApplyCharacterRun(virtualSprintState);
        }

        public void VirtualShootInput(bool virtualShootState)
        {
            if (character == null)
            {
                if (starterAssetsInputs != null) starterAssetsInputs.ShootInput(virtualShootState);
                return;
            }
            EnsureCursorLocked();
            character.SetFireInput(virtualShootState);
            if (starterAssetsInputs != null) starterAssetsInputs.shoot = virtualShootState;
        }
    }
}