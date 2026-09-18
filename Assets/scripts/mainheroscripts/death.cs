using System.Collections;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Обробляє "смерть" гравця (напр. акула з'їла) БЕЗ переходу в іншу сцену:
/// - ховає візуальну модель гравця;
/// - вимикає скрипти керування;
/// - перемикає камеру гравця на окрему "камеру смерті";
/// - за командою показує Game Over UI;
/// - прив'язує кнопку рестарту до GameRestartManager зі сцени;
/// - показує Game Over, коли острів ПОВНІСТЮ з'їдений акулою (незалежно
///   від того, що саме стало причиною - природний останній укус чи
///   форсоване поїдання після смерті гравця).
///
/// Повісити на persona-об'єкт з PhotonView (у твоїй сцені - на "mainhero").
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class PlayerDeathHandler : MonoBehaviourPun
{
    [Header("Що ховати/вимикати")]
    public GameObject playerModel;
    public MonoBehaviour[] scriptsToDisable;
    public Collider[] collidersToDisable;

    [Header("Камери (можна лишити порожнім - знайдуться самі за назвою об'єкта)")]
    [Tooltip("Звичайна ігрова камера. Якщо не задано - шукається дочірній об'єкт з ім'ям \"Main Camera\".")]
    public Camera playerCamera;

    [Tooltip("Камера смерті. Якщо не задано - шукається дочірній об'єкт з ім'ям \"deathcamera\".")]
    public Camera deathCamera;

    [Header("Назви об'єктів для автопошуку камер (якщо поля вище порожні)")]
    [SerializeField] private string playerCameraObjectName = "Main Camera";
    [SerializeField] private string deathCameraObjectName = "deathcamera";

    [Header("UI")]
    [Tooltip("Якщо не задано - шукається автоматично серед дочірніх об'єктів mainhero за назвою, що містить \"gameover\".")]
    public GameObject gameOverUI;

    [Tooltip("Ігровий HUD, який треба сховати одночасно зі смертю (напр. oxygenBarRoot з PlayerBreath, інвентар тощо).")]
    public GameObject[] gameplayUI;

    [Header("Кнопка перезапуску")]
    [Tooltip("Кнопка \"New Game\" усередині gameOverUI. Клік прив'язується В КОДІ до GameRestartManager, знайденого на сцені - бо GameRestartManager є ОБ'ЄКТОМ СЦЕНИ, і Inspector-посилання на нього на префабі гравця завжди обнулиться після PhotonNetwork.Instantiate.")]
    public Button restartButton;

    [Header("Кнопка \"Меню\"")]
    [Tooltip("Кнопка \"Меню\" усередині gameOverUI. За кліком гравець виходить з Photon-кімнати і завантажується сцена головного меню. Прив'язується в коді автоматично (Awake), як і restartButton.")]
    public Button menuButton;

    [Tooltip("Точна назва сцени головного меню (має бути додана в File -> Build Settings -> Scenes In Build).")]
    [SerializeField] private string menuSceneName = "Menu";

    [Header("Острів (фінальний ефект)")]
    [Tooltip("SharkBiteController зі сцени. Якщо не задано - шукається автоматично через FindObjectOfType.")]
    public SharkBiteController sharkBiteController;

    [Tooltip("HeightmapIsland зі сцени. Якщо не задано - шукається автоматично через FindObjectOfType. Потрібен, щоб підписатись на подію 'острів повністю з'їдений' і показати Game Over.")]
    public HeightmapIsland island;

    [Tooltip("Затримка (сек) після смерті гравця, перш ніж острів почне зникати.")]
    public float islandDevourStartDelay = 1f;

    private bool isDead;
    public bool IsDead => isDead;

    void Awake()
    {
        ResolveCamerasIfMissing();
        ResolveGameOverUIIfMissing();

        if (sharkBiteController == null)
            sharkBiteController = FindObjectOfType<SharkBiteController>();

        if (island == null)
            island = FindObjectOfType<HeightmapIsland>();

        // Підписуємось на "острів повністю з'їдений" незалежно від причини:
        // спрацює і від природного останнього укусу SharkBiteController
        // (гравець ще живий, острова більше немає - все одно Game Over),
        // і від DevourWholeIslandNow() після Die() (тоді Die() всередині
        // обробника просто зробить нічого через isDead-гвард, а ShowGameOver
        // покаже екран, якщо його ще не показали).
        if (island != null)
            island.OnIslandDevoured += HandleIslandDevoured;

        BindRestartButton(restartButton);
        BindMenuButton(menuButton);
    }

    void OnDestroy()
    {
        if (island != null)
            island.OnIslandDevoured -= HandleIslandDevoured;
    }

    /// <summary>
    /// Викликається локально на КОЖНОМУ клієнті одразу, як тільки прийшов
    /// RPC фінального укусу (острів повністю зник). Death/UI логіка тут
    /// стосується лише ВЛАСНОГО гравця (перевірка photonView.IsMine
    /// відбувається всередині Die() і ShowGameOverDelayed()).
    /// </summary>
    private void HandleIslandDevoured(float sinkDuration)
    {
        if (!photonView.IsMine) return;

        Die();
        ShowGameOverDelayed(sinkDuration);
    }

    private void ResolveCamerasIfMissing()
    {
        if (playerCamera != null && deathCamera != null) return;

        Camera[] allCameras = GetComponentsInChildren<Camera>(true);

        foreach (var cam in allCameras)
        {
            if (playerCamera == null && cam.gameObject.name == playerCameraObjectName)
                playerCamera = cam;

            if (deathCamera == null && cam.gameObject.name == deathCameraObjectName)
                deathCamera = cam;
        }

        if (playerCamera == null)
            Debug.LogError($"[PlayerDeathHandler] Не знайдено playerCamera (шукав об'єкт \"{playerCameraObjectName}\") і поле в інспекторі порожнє!");

        if (deathCamera == null)
            Debug.LogError($"[PlayerDeathHandler] Не знайдено deathCamera (шукав об'єкт \"{deathCameraObjectName}\") і поле в інспекторі порожнє!");
    }

    /// <summary>
    /// Якщо gameOverUI не призначено вручну - шукає серед ДОЧІРНІХ об'єктів
    /// mainhero (вони гарантовано на місці, бо це той самий префаб) той,
    /// чия назва містить "gameover" (без урахування регістру).
    /// </summary>
    private void ResolveGameOverUIIfMissing()
    {
        if (gameOverUI != null) return;

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.gameObject == gameObject) continue;

            if (t.name.ToLower().Contains("gameover"))
            {
                gameOverUI = t.gameObject;
                Debug.Log($"[PlayerDeathHandler] gameOverUI автоматично знайдено: {t.name}");
                break;
            }
        }

        if (gameOverUI == null)
            Debug.LogWarning("[PlayerDeathHandler] gameOverUI не знайдено ні вручну, ні автопошуком за назвою \"gameover\".");
    }

    /// <summary>
    /// Прив'язує клік кнопки restartButton до GameRestartManager.RestartGame()
    /// у коді. GameRestartManager - синглтон-об'єкт СЦЕНИ, тому Inspector
    /// OnClick на префабі гравця не може на нього посилатись.
    /// </summary>
    private void BindRestartButton(Button button)
    {
        if (button == null) return;

        GameRestartManager restartManager = FindObjectOfType<GameRestartManager>();
        if (restartManager == null)
        {
            Debug.LogWarning("[PlayerDeathHandler] GameRestartManager не знайдено на сцені - кнопка New Game не буде працювати.");
            return;
        }

        button.onClick.RemoveListener(restartManager.RestartGame);
        button.onClick.AddListener(restartManager.RestartGame);
    }

    /// <summary>
    /// Прив'язує клік кнопки menuButton до ReturnToMenu() у коді - для
    /// консистентності (і щоб не залежати від Inspector OnClick, який
    /// зазвичай теж злітає після PhotonNetwork.Instantiate префабу гравця).
    /// </summary>
    private void BindMenuButton(Button button)
    {
        if (button == null) return;

        button.onClick.RemoveListener(ReturnToMenu);
        button.onClick.AddListener(ReturnToMenu);
    }

    /// <summary>
    /// Викликається кнопкою "Меню" на Game Over екрані. Коректно виходить
    /// з Photon-кімнати (щоб інші гравці бачили, що ти вийшов, і щоб не
    /// лишався "привидом" у кімнаті) і лише ПІСЛЯ виходу завантажує сцену
    /// головного меню.
    /// </summary>
    public void ReturnToMenu()
    {
        if (!photonView.IsMine) return;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (menuButton != null)
            menuButton.interactable = false;

        StartCoroutine(ReturnToMenuRoutine());
    }

    private IEnumerator ReturnToMenuRoutine()
    {
        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.LeaveRoom();
            while (PhotonNetwork.InRoom)
                yield return null;
        }

        SceneManager.LoadScene(menuSceneName);
    }

    void Start()
    {
        if (!photonView.IsMine) return;

        if (deathCamera != null)
        {
            deathCamera.enabled = false;
            if (deathCamera.gameObject.activeSelf)
            {
                Debug.LogWarning("[PlayerDeathHandler] deathCamera була активна на старті - вимикаю.");
                deathCamera.gameObject.SetActive(false);
            }

            var deathListener = deathCamera.GetComponent<AudioListener>();
            if (deathListener != null) deathListener.enabled = false;
        }

        if (gameOverUI != null && gameOverUI.activeSelf)
            gameOverUI.SetActive(false);
    }

    public void Die()
    {
        if (isDead) return;
        if (!photonView.IsMine) return;

        photonView.RPC(nameof(RPC_Die), RpcTarget.All);
    }

    [PunRPC]
    private void RPC_Die()
    {
        if (isDead) return;
        isDead = true;

        foreach (var script in scriptsToDisable)
            if (script != null) script.enabled = false;

        foreach (var col in collidersToDisable)
            if (col != null) col.enabled = false;

        if (playerModel != null)
            playerModel.SetActive(false);

        if (photonView.IsMine && gameplayUI != null)
        {
            foreach (var ui in gameplayUI)
                if (ui != null) ui.SetActive(false);
        }

        if (PhotonNetwork.IsMasterClient && sharkBiteController != null)
            StartCoroutine(DevourWholeIslandDelayedRoutine());

        if (photonView.IsMine)
        {
            ResolveCamerasIfMissing();

            Debug.Log($"[PlayerDeathHandler] RPC_Die: playerCamera={(playerCamera != null ? playerCamera.name : "NULL")}, deathCamera={(deathCamera != null ? deathCamera.name : "NULL")}");

            if (playerCamera != null)
            {
                playerCamera.enabled = false;
                playerCamera.gameObject.SetActive(false);
            }

            if (deathCamera != null)
            {
                deathCamera.gameObject.SetActive(true);
                deathCamera.enabled = true;

                var deathListener = deathCamera.GetComponent<AudioListener>();
                if (deathListener != null) deathListener.enabled = true;
            }
            else
            {
                Debug.LogError("[PlayerDeathHandler] deathCamera відсутня - камера смерті НЕ увімкнеться!");
            }
        }
    }

    public void ShowGameOver()
    {
        if (!photonView.IsMine) return;
        if (gameOverUI != null)
            gameOverUI.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void ShowGameOverDelayed(float delay)
    {
        if (!photonView.IsMine) return;
        StartCoroutine(ShowGameOverRoutine(delay));
    }

    private IEnumerator ShowGameOverRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        ShowGameOver();
    }

    private IEnumerator DevourWholeIslandDelayedRoutine()
    {
        yield return new WaitForSeconds(islandDevourStartDelay);
        sharkBiteController.DevourWholeIslandNow();
    }
}