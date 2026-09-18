using System.Collections;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Показує екран перемоги ("YOU WIN"), коли акула повністю нагодована
/// (заповнився FishProgressBar). Працює НЕЗАЛЕЖНО від PlayerDeathHandler -
/// підписується на ту саму подію FishProgressBar.OnFishBarFull, вмикає
/// winUI, вимикає керування гравцем і ігровий HUD, а також прив'язує кнопки
/// "New Game" / "Меню" усередині winUI до відповідних менеджерів сцени.
///
/// Повісити на persona-об'єкт з PhotonView (той самий "mainhero", де
/// висить PlayerDeathHandler).
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class PlayerWinHandler : MonoBehaviourPun
{
    [Header("Win UI (перемога - акула повністю ситa)")]
    [Tooltip("UI екрана перемоги (\"YOU WIN\"). Признач вручну в інспекторі (напр. об'єкт \"winscreen\").")]
    public GameObject winUI;

    [Tooltip("Кнопка \"New Game\" усередині winUI. Прив'язується в коді до GameRestartManager, знайденого на сцені.")]
    public Button winRestartButton;

    [Tooltip("Кнопка \"Меню\" усередині winUI. Прив'язується в коді - вихід з Photon-кімнати і завантаження сцени меню.")]
    public Button winMenuButton;

    [Tooltip("Точна назва сцени головного меню (має бути додана в File -> Build Settings -> Scenes In Build).")]
    [SerializeField] private string menuSceneName = "Menu";

    [Header("Що вимикати на перемозі")]
    [Tooltip("Скрипти керування гравцем, які треба вимкнути (той самий список, що й у PlayerDeathHandler).")]
    public MonoBehaviour[] scriptsToDisable;

    [Tooltip("Ігровий HUD, який треба сховати одночасно з появою winUI (напр. oxygenBarRoot, інвентар тощо).")]
    public GameObject[] gameplayUI;

    [Tooltip("Чи вимикати керування гравцем (scriptsToDisable), коли з'являється екран перемоги.")]
    public bool freezePlayerOnWin = true;

    // FishProgressBar завжди шукається автоматично на сцені (FindObjectOfType) -
    // ручне призначення в інспекторі не потрібне, бо цей об'єкт належить
    // сцені, а не префабу гравця.
    private FishProgressBar fishProgressBar;

    [Tooltip("Якщо на об'єкті є PlayerDeathHandler - перемога НЕ покажеться, якщо гравець вже програв (IsDead == true). Можна лишити порожнім, якщо PlayerDeathHandler не використовується.")]
    public PlayerDeathHandler deathHandler;

    private bool hasWon;
    public bool HasWon => hasWon;

    void Awake()
    {
        fishProgressBar = FindObjectOfType<FishProgressBar>();

        if (deathHandler == null)
            deathHandler = GetComponent<PlayerDeathHandler>();

        if (fishProgressBar != null)
            fishProgressBar.OnFishBarFull += HandleFishBarFull;
        else
            Debug.LogWarning("[PlayerWinHandler] FishProgressBar не знайдено (ні вручну, ні автопошуком на сцені) - екран перемоги НЕ зможе з'явитись автоматично.");

        BindRestartButton(winRestartButton);
        BindMenuButton(winMenuButton);
    }

    void OnDestroy()
    {
        if (fishProgressBar != null)
            fishProgressBar.OnFishBarFull -= HandleFishBarFull;
    }

    void Start()
    {
        if (!photonView.IsMine) return;

        if (winUI != null && winUI.activeSelf)
            winUI.SetActive(false);
    }

    /// <summary>
    /// Викликається подією FishProgressBar.OnFishBarFull, коли акула
    /// назбирала достатньо риби (бар заповнився). Показує екран перемоги -
    /// лише для власного (photonView.IsMine) гравця і лише один раз.
    /// </summary>
    private void HandleFishBarFull()
    {
        if (!photonView.IsMine) return;
        if (hasWon) return;
        if (deathHandler != null && deathHandler.IsDead) return; // гравець вже програв - не показуємо перемогу поверх Game Over

        hasWon = true;
        ShowWin();
    }

    /// <summary>
    /// Показує екран перемоги (winUI), за потреби вимикає керування гравцем
    /// і ховає ігровий HUD.
    /// </summary>
    public void ShowWin()
    {
        if (!photonView.IsMine) return;

        if (freezePlayerOnWin)
        {
            foreach (var script in scriptsToDisable)
                if (script != null) script.enabled = false;
        }

        if (gameplayUI != null)
        {
            foreach (var ui in gameplayUI)
                if (ui != null) ui.SetActive(false);
        }

        if (winUI != null)
            winUI.SetActive(true);
        else
            Debug.LogWarning("[PlayerWinHandler] winUI не призначено в інспекторі - екран перемоги не покажеться.");

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>
    /// Прив'язує клік кнопки winRestartButton до GameRestartManager.RestartGame()
    /// у коді. GameRestartManager - синглтон-об'єкт СЦЕНИ, тому Inspector
    /// OnClick на префабі гравця не може на нього посилатись (посилання
    /// обнулиться після PhotonNetwork.Instantiate).
    /// </summary>
    private void BindRestartButton(Button button)
    {
        if (button == null) return;

        GameRestartManager restartManager = FindObjectOfType<GameRestartManager>();
        if (restartManager == null)
        {
            Debug.LogWarning("[PlayerWinHandler] GameRestartManager не знайдено на сцені - кнопка New Game не буде працювати.");
            return;
        }

        button.onClick.RemoveListener(restartManager.RestartGame);
        button.onClick.AddListener(restartManager.RestartGame);
    }

    /// <summary>
    /// Прив'язує клік кнопки winMenuButton до ReturnToMenu() у коді.
    /// </summary>
    private void BindMenuButton(Button button)
    {
        if (button == null) return;

        button.onClick.RemoveListener(ReturnToMenu);
        button.onClick.AddListener(ReturnToMenu);
    }

    /// <summary>
    /// Викликається кнопкою "Меню" на Win екрані. Коректно виходить з
    /// Photon-кімнати і лише ПІСЛЯ виходу завантажує сцену головного меню.
    /// </summary>
    public void ReturnToMenu()
    {
        if (!photonView.IsMine) return;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (winMenuButton != null)
            winMenuButton.interactable = false;

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
}