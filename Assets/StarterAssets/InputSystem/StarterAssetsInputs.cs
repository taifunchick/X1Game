using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
    public class StarterAssetsInputs : MonoBehaviour
    {
        [Header("Character Input Values")]
        public Vector2 move;
        public Vector2 look;
        public bool jump;
        public bool sprint;

        /// <summary>
        /// Кнопка стрельбы (действие Player/Fire: ЛКМ / правый триггер геймпада / мобильная кнопка).
        /// true — кнопка нажата. Значение приходит из PlayerInput через OnShoot, из виртуальной
        /// кнопки через UICanvasControllerInput.VirtualShootInput, а ShootInput дополнительно
        /// пробрасывает его в существующую стрельбу Infima (Character.SetFireInput → оружие).
        /// </summary>
        public bool shoot;

        /// <summary>Кэш персонажа, в который пробрасывается флаг стрельбы.</summary>
        private InfimaGames.LowPolyShooterPack.Character character;

        /// <summary>Предупреждение об отсутствии персонажа Infima показываем один раз, а не на каждое нажатие.</summary>
        private bool warnedNoCharacter;

        [Header("Movement Settings")]
        public bool analogMovement;

        [Header("Mouse Cursor Settings")]
        public bool cursorLocked = true;
        public bool cursorInputForLook = true;

        void Update()
        {
            if (Input.GetMouseButtonDown(1)) // ��� ������� �� ������ ������ ����
            {
                Cursor.visible = false; // �������� ������
                Cursor.lockState = CursorLockMode.Locked; // ����������� ������
            }

            if (Input.GetKeyDown(KeyCode.Escape)) // ��� ������� �� ������� ESC
            {
                Cursor.visible = true; // ���������� ������
                Cursor.lockState = CursorLockMode.None; // ����������� ������
            }
        }

#if ENABLE_INPUT_SYSTEM
        public void OnMove(InputValue value)
        {
            MoveInput(value.Get<Vector2>());
        }

        public void OnLook(InputValue value)
        {
            if (cursorInputForLook)
            {
                LookInput(value.Get<Vector2>());
            }
        }

        public void OnJump(InputValue value)
        {
            JumpInput(value.isPressed);
        }

        public void OnSprint(InputValue value)
        {
            SprintInput(value.isPressed);
        }

        /// <summary>
        /// Кнопка стрельбы. Сигнатура с InputAction.CallbackContext, а не InputValue, —
        /// так вызывает методы PlayerInput в режиме «Invoke Unity Events», которым
        /// настроен PlayerInput на префабе игрока (P_LPSP_FP_CH, действие Player/Fire).
        /// </summary>
        public void OnShoot(InputAction.CallbackContext context)
        {
            // Started/Performed — кнопку нажали, Canceled — отпустили.
            if (context.phase == InputActionPhase.Canceled)
                ShootInput(false);
            else if (context.phase == InputActionPhase.Started || context.phase == InputActionPhase.Performed)
                ShootInput(true);
        }
#endif


        public void MoveInput(Vector2 newMoveDirection)
        {
            move = newMoveDirection;
        }

        public void LookInput(Vector2 newLookDirection)
        {
            look = newLookDirection;
        }

        public void JumpInput(bool newJumpState)
        {
            jump = newJumpState;
        }

        public void SprintInput(bool newSprintState)
        {
            sprint = newSprintState;
        }

        /// <summary>
        /// Установить кнопку стрельбы и пробросить её в существующую стрельбу Infima:
        /// Character.SetFireInput повторяет логику Character.OnTryFire (удержание,
        /// одиночный выстрел, очередь, патроны, перезарядка — всё как было).
        /// </summary>
        public void ShootInput(bool newShootState)
        {
            // PlayerInput шлёт Started, а затем Performed — обрабатываем смену один раз.
            if (shoot == newShootState)
                return;

            shoot = newShootState;

            if (character == null)
                character = GetComponent<InfimaGames.LowPolyShooterPack.Character>();

            if (character != null)
            {
                warnedNoCharacter = false;
                character.SetFireInput(newShootState);
                return;
            }

            // На этом игроке нет персонажа Infima (например, футбольный префаб) — сообщаем один раз.
            // Флаг shoot всё равно выставлен, и его может прочитать тот, кто умеет стрелять.
            if (!warnedNoCharacter)
            {
                warnedNoCharacter = true;
                Debug.LogWarning("StarterAssetsInputs: на игроке нет Character (Infima) — кнопка выстрела " +
                                 "только записывает shoot, стрельбу запускает другой компонент.");
            }
        }
    }

}