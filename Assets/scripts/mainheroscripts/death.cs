using System.Collections;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Обробляє "смерть" гравця (напр. акула з'їла, задихнувся) БЕЗ переходу в іншу сцену:
/// - ховає візуальну модель гравця;
/// - вимикає скрипти керування;
/// - перемикає камеру гравця на окрему "камеру смерті";
/// - за командою показує Game Over UI;
/// - прив'язує кнопки рестарту/меню;
/// - показує Game Over, коли острів ПОВНІСТЮ з'їдений акулою.
///
/// Повісити на persona-об'єкт з PhotonView (у твоїй сцені - на "mainhero").
///
/// ВИПРАВЛЕННЯ КАМЕРИ СМЕРТІ:
///  1. Якщо deathCamera лежить ВСЕРЕДИНІ об'єкта, який при смерті вимикається
///     (Main Camera / playerModel / gameplayUI) - вона лишалась неактивною в
///     ієрархії, хоча SetActive(true) викликався. Тепер перед вимкненням вона
///     виноситься з-під таких батьків (світова позиція зберігається).
///  2. Автопошук камер тепер не залежить від регістру, а для deathCamera
///     враховує і назву батьківських об'єктів (якщо "deathcamera" - це порожній
///     батько, а Camera сидить на дочірньому).
///  3. Усі неактивні батьки deathCamera всередині гравця вмикаються.
///  4. У Console друкується повний стан камери смерті та інших активних камер.
///  5. На ЧУЖИХ копіях гравця камера смерті завжди вимкнена.
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

    [Tooltip("Камера смерті. Якщо не задано - шукається камера, у назві якої (або її батька) є \"deathcamera\".")]
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

    // true, якщо deathCamera довелось винести в корінь сцени - тоді її треба
    // знищити разом з гравцем (вона вже не його дочірній об'єкт).
    private bool deathCameraDetachedToSceneRoot;

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

        if (deathCameraDetachedToSceneRoot && deathCamera != null)
            Destroy(deathCamera.gameObject);
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

        // Спершу камера смерті (за назвою САМОЇ камери або будь-якого її батька
        // до кореня гравця), щоб вона не потрапила в playerCamera.
        if (deathCamera == null)
        {
            foreach (var cam in allCameras)
            {
                if (NameOrParentNameContains(cam.transform, deathCameraObjectName))
                {
                    deathCamera = cam;
                    break;
                }
            }
        }

        if (playerCamera == null)
        {
            foreach (var cam in allCameras)
            {
                if (cam == deathCamera) continue;

                if (string.Equals(cam.gameObject.name, playerCameraObjectName,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    playerCamera = cam;
                    break;
                }
            }
        }

        if (playerCamera == null)
            Debug.LogError($"[PlayerDeathHandler] Не знайдено playerCamera (шукав об'єкт \"{playerCameraObjectName}\") і поле в інспекторі порожнє!");

        if (deathCamera == null)
            Debug.LogError($"[PlayerDeathHandler] Не знайдено deathCamera (шукав \"{deathCameraObjectName}\" у назві камери чи її батьків) і поле в інспекторі порожнє!");
    }

    private bool NameOrParentNameContains(Transform t, string part)
    {
        if (string.IsNullOrEmpty(part)) return false;

        for (Transform p = t; p != null && p != transform; p = p.parent)
        {
            if (p.name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
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
        if (!photonView.IsMine)
        {
            // Чужа копія гравця: її камера смерті на нашому екрані не потрібна ніколи.
            if (deathCamera != null)
                deathCamera.gameObject.SetActive(false);
            return;
        }

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

        // ВАЖЛИВО: камеру смерті рятуємо ДО того, як щось вимикається нижче.
        if (photonView.IsMine)
        {
            ResolveCamerasIfMissing();
            DetachDeathCameraFromDoomedObjects();
        }

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
            if (playerCamera != null)
            {
                playerCamera.enabled = false;
                playerCamera.gameObject.SetActive(false);
            }

            if (deathCamera != null)
            {
                // Вмикаємо всіх неактивних батьків камери смерті всередині гравця.
                for (Transform p = deathCamera.transform.parent; p != null && p != transform; p = p.parent)
                {
                    if (!p.gameObject.activeSelf)
                        p.gameObject.SetActive(true);
                }

                deathCamera.gameObject.SetActive(true);
                deathCamera.enabled = true;

                var deathListener = deathCamera.GetComponent<AudioListener>();
                if (deathListener != null) deathListener.enabled = true;

                LogDeathCameraState();
            }
            else
            {
                Debug.LogError("[PlayerDeathHandler] deathCamera відсутня - камера смерті НЕ увімкнеться!");
            }
        }
    }

    /// <summary>
    /// Якщо камера смерті є дочірньою для об'єкта, що зараз буде вимкнено
    /// (playerCamera, playerModel, елемент gameplayUI) - виносимо її з-під нього.
    /// Світова позиція й поворот зберігаються.
    /// </summary>
    private void DetachDeathCameraFromDoomedObjects()
    {
        if (deathCamera == null) return;

        Transform dc = deathCamera.transform;
        string reason = null;

        if (playerCamera != null && dc != playerCamera.transform && dc.IsChildOf(playerCamera.transform))
            reason = "playerCamera";
        else if (playerModel != null && dc != playerModel.transform && dc.IsChildOf(playerModel.transform))
            reason = "playerModel";
        else if (gameplayUI != null)
        {
            foreach (var ui in gameplayUI)
            {
                if (ui != null && dc != ui.transform && dc.IsChildOf(ui.transform))
                {
                    reason = "gameplayUI: " + ui.name;
                    break;
                }
            }
        }

        if (reason == null) return;

        // Якщо сам корінь гравця лежить під playerModel - прив'язка до кореня не допоможе,
        // виносимо камеру в корінь сцени (і знищимо в OnDestroy).
        bool rootIsDoomed = playerModel != null && transform.IsChildOf(playerModel.transform);

        Debug.LogWarning($"[PlayerDeathHandler] deathCamera лежить під '{reason}', який вимикається при смерті - " +
                         $"виношу її {(rootIsDoomed ? "в корінь сцени" : "під корінь гравця")}.");

        dc.SetParent(rootIsDoomed ? null : transform, true);
        deathCameraDetachedToSceneRoot = rootIsDoomed;
    }

    private void LogDeathCameraState()
    {
        if (deathCamera == null) return;

        string chain = "";
        for (Transform p = deathCamera.transform; p != null; p = p.parent)
            chain += $"{p.name}[{(p.gameObject.activeSelf ? "on" : "OFF")}] < ";

        if (!deathCamera.gameObject.activeInHierarchy)
            Debug.LogError("[PlayerDeathHandler] deathCamera ВСЕ ЩЕ неактивна в ієрархії - дивись ланцюжок батьків вище (OFF).");

        foreach (Camera cam in Camera.allCameras)
        {
            if (cam == deathCamera) continue;
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