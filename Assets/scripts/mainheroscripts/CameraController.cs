using Photon.Pun;
using UnityEngine;
using UnityEngine.EventSystems;

public class FirstPersonCamera : MonoBehaviourPun
{
    [Header("Тіло персонажа")]
    public Transform playerBody; // mainhero_animated

    [Header("Позиція камери (голова)")]
    public Transform headTarget;
    public Vector3 headOffsetIfNoTarget = new Vector3(0f, 1.6f, 0f);

    [Header("Ціль для повороту голови (Multi-Aim Constraint)")]
    public Transform headLookTarget;
    public float headLookDistance = 3f;

    [Header("Чутливість")]
    public float mouseSensitivity = 3f;
    public float minVerticalAngle = -80f;
    public float maxVerticalAngle = 80f;

    [Header("Налаштування орієнтації")]
    public bool invertYaw = false;
    public float modelYawOffset = 180f;

    private float yaw;
    private float pitch;
    private bool justLocked = true;

    void Start()
    {
        // Чужі копії не повинні захоплювати курсор і не повинні крутити playerBody
        // за нашою локальною мишею - інакше на екрані клієнта А курсор блокується
        // N разів (по одному на кожного заспавненого гравця), і миша А крутить
        // голови всіх аватарів у сцені.
        if (!photonView.IsMine)
        {
            enabled = false;
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        justLocked = true;
        yaw = 0f;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
        {
            // ВАЖЛИВО: якщо клік стався по UI-елементу (кнопка паузи, налаштувань,
            // інвентаря тощо) - НЕ блокуємо курсор назад. Раніше блокування
            // спрацьовувало в тому самому кадрі, що й клік, курсор миттєво ховався,
            // і клік по кнопці "з'їдався" - саме тому кнопки нібито не працювали.
            bool clickedOnUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (clickedOnUI)
                return;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            justLocked = true;
        }

        if (justLocked)
        {
            justLocked = false;
            return;
        }

        // Поки курсор розблокований (відкритий будь-який UI) - персонажем
        // і камерою керувати не можна. GameplayUIPanel.Open() додатково вимикає
        // цей скрипт повністю, це - друга лінія захисту про всяк випадок.
        if (Cursor.lockState != CursorLockMode.Locked)
            return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * (invertYaw ? -1f : 1f);
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        yaw += mouseX;
        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, minVerticalAngle, maxVerticalAngle);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        if (playerBody != null)
            playerBody.rotation = Quaternion.Euler(0f, yaw + modelYawOffset, 0f);

        if (headTarget != null)
            transform.position = headTarget.position;
        else if (playerBody != null)
            transform.position = playerBody.position + headOffsetIfNoTarget;

        if (headLookTarget != null)
            headLookTarget.position = transform.position + transform.forward * headLookDistance;
    }
}