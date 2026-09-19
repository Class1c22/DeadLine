using UnityEngine;
using UnityEngine.SceneManagement;

// Живе в сцені меню, але переживає перехід в ігрову сцену
// (DontDestroyOnLoad), бо повинен лишатись видимим весь час завантаження.
public class LoadingScreenController : MonoBehaviour
{
    public static LoadingScreenController Instance { get; private set; }

    [SerializeField] private Animator jawAnimator;
    [SerializeField] private string readyTrigger = "Ready";
    [SerializeField] private string gameSceneName = "Game"; // назва ігрової сцени

    private bool sceneLoaded;
    private bool playerSpawned;
    private bool readyFired; // тригер Ready шлемо лише один раз за сеанс гри

    private void Awake()
    {
        Debug.Log($"[LoadingScreenController] Awake на об'єкті '{gameObject.name}'. " +
                  $"Instance вже існує: {Instance != null}. " +
                  $"Батьківський об'єкт: {(transform.parent != null ? transform.parent.name : "немає (root)")}");

        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[LoadingScreenController] Знайшов дублікат - знищую цей об'єкт!");
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (transform.parent != null)
        {
            Debug.LogError("[LoadingScreenController] Об'єкт НЕ кореневий - DontDestroyOnLoad " +
                            "його не збереже! Прибери його з-під батьківського об'єкта.");
        }

        DontDestroyOnLoad(gameObject);
        Debug.Log("[LoadingScreenController] DontDestroyOnLoad викликано.");
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"[LoadingScreenController] OnSceneLoaded: '{scene.name}' (очікую '{gameSceneName}')");

        if (scene.name != gameSceneName)
        {
            // Повернулись у меню (або будь-яку не-ігрову сцену) - скидаємо стан,
            // щоб наступний Play чекав на НОВИЙ спавн, а не на застарілий.
            sceneLoaded = false;
            playerSpawned = false;
            readyFired = false;
            return;
        }

        // Перезапуск ігрової сцени (New Game через PhotonNetwork.LoadLevel):
        // екран завантаження вже відкритий - вдруге тригер Ready не шлемо.
        sceneLoaded = true;
        TryFireReady();
    }

    // Викликати з NetworkManager.SpawnPlayer() одразу після Instantiate
    public void OnPlayerSpawned()
    {
        Debug.Log("[LoadingScreenController] OnPlayerSpawned() викликано.");
        playerSpawned = true;
        TryFireReady();
    }

    private void TryFireReady()
    {
        Debug.Log($"[LoadingScreenController] TryFireReady: sceneLoaded={sceneLoaded}, playerSpawned={playerSpawned}, readyFired={readyFired}");

        if (sceneLoaded && playerSpawned && !readyFired)
        {
            if (jawAnimator == null)
            {
                Debug.LogError("[LoadingScreenController] Jaw Animator не призначено в inspector!");
                return;
            }

            Debug.Log($"[LoadingScreenController] Викликаю тригер '{readyTrigger}'.");
            readyFired = true;
            jawAnimator.SetTrigger(readyTrigger);
        }
    }
}