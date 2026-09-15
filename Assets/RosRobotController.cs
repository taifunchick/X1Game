using UnityEngine;
using Mirror;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;

[RequireComponent(typeof(CharacterController))] // Защита от дурака: юнити сама добавит компонент, если его нет
public class RosRobotController : NetworkBehaviour
{
    [Header("Настройки движения")]
    public float moveSpeed = 5f;
    public float turnSpeed = 100f;
    public float gravity = -9.81f; // CharacterController нуждается в ручной гравитации
    
    private CharacterController cc;
    
    // Сюда будем записывать данные, пришедшие из интернета
    private float moveInput = 0f;
    private float turnInput = 0f;
    
    // Текущая вертикальная скорость (для падения/гравитации)
    private float velocityY = 0f;

    void Start()
    {
        cc = GetComponent<CharacterController>();

        // ПОДПИСЫВАЕМСЯ НА КОМАНДЫ (Слушаем эфир)
        if (isServer) 
        {
            ROSConnection.GetOrCreateInstance().Subscribe<TwistMsg>("cmd_vel", OnReceiveMovementCommand);
        }
    }

    // Эта функция вызывается АВТОМАТИЧЕСКИ каждый раз, когда приходит сообщение от ROS
    private void OnReceiveMovementCommand(TwistMsg message)
    {
        // Читаем команду: linear.x - это движение вперед/назад
        moveInput = (float)message.linear.x; 
        
        // angular.z - это поворот влево/вправо
        turnInput = (float)message.angular.z; 
    }

    // Для CharacterController используем Update вместо FixedUpdate
    void Update()
    {
        // Двигать робота имеет право только сервер!
        if (!isServer) return;

        // 1. ПОВОРОТ
        // CharacterController не управляет вращением, поэтому крутим сам объект
        transform.Rotate(0f, turnInput * turnSpeed * Time.deltaTime, 0f);

        // 2. ДВИЖЕНИЕ ВПЕРЕД/НАЗАД
        Vector3 movement = transform.forward * moveInput * moveSpeed;

        // 3. ГРАВИТАЦИЯ
        if (cc.isGrounded)
        {
            // Если стоим на земле, постоянно слегка "давим" вниз, чтобы контроллер плотно прилегал к полу
            velocityY = -2f; 
        }
        else
        {
            // Если летим, ускоряемся вниз по законам физики
            velocityY += gravity * Time.deltaTime;
        }
        
        // Применяем вертикальную скорость к нашему вектору движения
        movement.y = velocityY;

        // 4. ПЕРЕМЕЩЕНИЕ КОНТРОЛЛЕРА
        cc.Move(movement * Time.deltaTime);
    }
}