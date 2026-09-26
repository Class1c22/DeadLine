using UnityEngine;
using UnityEngine.SceneManagement;

// Живе в сцені меню, але переживає перехід в ігрову сцену
// (DontDestroyOnLoad), бо повинен лишатись видимим весь час завантаження.
[RequireComponent(typeof(AudioSource))]
public class LoadingScreenController : MonoBehaviour
{
    public static LoadingScreenController Instance { get; private set; }

    [SerializeField] private Animator jawAnimator;
    [SerializeField] private string readyTrigger = "Ready";
    [SerializeField] private string gameSceneName = "Game"; // назва ігрової сцени

    [Header("Звуки щелепи")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip jawOpenSound;
    [SerializeField] private AudioClip jawCloseSound;
    [Range(0f, 1f)][SerializeField] private float sfxVolume = 1f;

    private bool sceneLoaded;
    private bool playerSpawned;
    private bool readyFired; // тригер Ready шлемо лише один раз за сеанс гри

    private void Awake()
    {

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

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
        audioSource.playOnAwake = false;

        DontDestroyOnLoad(gameObject);
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
        playerSpawned = true;
        TryFireReady();
    }

    private void TryFireReady()
    {

        if (sceneLoaded && playerSpawned && !readyFired)
        {
            if (jawAnimator == null)
            {
                Debug.LogError("[LoadingScreenController] Jaw Animator не призначено в inspector!");
                return;
            }

            readyFired = true;
            jawAnimator.SetTrigger(readyTrigger);
        }
    }

    // --- Виклики для Animation Events ---
    // Додай Animation Event на клипі анімації щелепи:
    // на кадрі, де щелепа РОЗКРИВАЄТЬСЯ -> викликай PlayJawOpenSound()
    // на кадрі, де щелепа ЗАКРИВАЄТЬСЯ  -> викликай PlayJawCloseSound()

    public void PlayJawOpenSound()
    {
        PlaySfx(jawOpenSound);
    }

    public void PlayJawCloseSound()
    {
        PlaySfx(jawCloseSound);
    }

    private void PlaySfx(AudioClip clip)
    {
        if (clip == null || audioSource == null)
        {
            return;
        }

        audioSource.PlayOneShot(clip, sfxVolume);
    }
}