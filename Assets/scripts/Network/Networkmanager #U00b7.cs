using System.Collections;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.Playables;

// Підключає гру до Photon Cloud, спавнить гравця в спільній кімнаті і коректно
// респавнить його після PhotonNetwork.LoadLevel (кнопка "New Game").
//
// ВАЖЛИВО: у сцені має бути ТІЛЬКИ ОДИН об'єкт з цим скриптом.
//
// ПІДКЛЮЧЕННЯ:
//  - Старт через 1 кадр після Start(), коли всі інші Start() уже відпрацювали.
//  - Якщо вже в кімнаті - спавн напряму (перезапуск сцени).
//  - Якщо вже на Master Server (повернулись з меню) - одразу JoinOrCreateRoom.
//  - Інакше: спроба 1 - протокол з PhotonServerSettings, спроба 2 - TCP,
//    далі Offline Mode (одиночна гра).
public class NetworkManager : MonoBehaviourPunCallbacks
{
    [Header("Назва префабу гравця (файл має лежати в Assets/Resources/)")]
    [SerializeField] private string playerPrefabName = "mainhero_animated";

    [Header("Назва кімнати (усі гравці з однаковою назвою потраплять разом)")]
    [SerializeField] private string roomName = "MainRoom";

    [Header("Точки спавну (можна лишити порожнім - тоді спавн у (0,0,0))")]
    [Tooltip("Якщо точок кілька - обирається випадкова. Якщо одна - завжди вона.")]
    [SerializeField] private Transform[] spawnPoints;

    [Header("Синематика, що грається одразу після спавну ЛОКАЛЬНОГО гравця")]
    [SerializeField] private PlayableDirector spawnCutscene;

    [Header("Стійкість підключення")]
    [Tooltip("Скільки разів пробувати онлайн (1-а спроба - за налаштуваннями Photon, наступні - TCP).")]
    [SerializeField] private int maxOnlineAttempts = 2;

    [Tooltip("Пауза перед повторною спробою, секунд.")]
    [SerializeField] private float retryDelay = 1.5f;

    [Tooltip("Скільки мс клієнт чекає відповіді сервера перед TimeoutDisconnect (стандартно 10000).")]
    [SerializeField] private int disconnectTimeoutMs = 15000;

    [Tooltip("Якщо всі онлайн-спроби невдалі - запустити гру в Offline Mode (одиночна).")]
    [SerializeField] private bool fallbackToOffline = true;

    private int connectAttempts;
    private bool playerSpawned;
    private bool goingOffline;

    private void Awake()
    {
        PhotonNetwork.AutomaticallySyncScene = true;
        Application.runInBackground = true;
    }

    private void Start()
    {
        if (PhotonNetwork.IsConnectedAndReady && PhotonNetwork.InRoom)
        {
            SpawnPlayer();
            return;
        }

        StartCoroutine(ConnectRoutine(0f));
    }

    private IEnumerator ConnectRoutine(float delay)
    {
        yield return null;

        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        // Чекаємо кінця перехідних станів (напр. Leaving після виходу з кімнати).
        float waited = 0f;
        while (PhotonNetwork.IsConnected && !PhotonNetwork.IsConnectedAndReady && waited < 10f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        if (PhotonNetwork.InRoom)
        {
            SpawnPlayer();
            yield break;
        }

        // Повернулись з меню: клієнт уже на Master Server - нового connect не треба.
        if (PhotonNetwork.IsConnectedAndReady)
        {
            PhotonNetwork.JoinOrCreateRoom(roomName, new RoomOptions(), TypedLobby.Default);
            yield break;
        }

        ConnectOnline();
    }

    private void ConnectOnline()
    {
        connectAttempts++;
        bool useTcp = connectAttempts > 1;

        AppSettings settings = new AppSettings();
        PhotonNetwork.PhotonServerSettings.AppSettings.CopyTo(settings);

        if (useTcp)
            settings.Protocol = ExitGames.Client.Photon.ConnectionProtocol.Tcp;

        PhotonNetwork.NetworkingClient.LoadBalancingPeer.DisconnectTimeout = disconnectTimeoutMs;

        if (!PhotonNetwork.ConnectUsingSettings(settings))
        {
            Debug.LogError("PhotonNetwork.ConnectUsingSettings повернув false - клієнт уже підключений/в перехідному стані або порожній AppId у PhotonServerSettings.");
            HandleConnectionFailed();
        }
    }

    private void HandleConnectionFailed()
    {
        if (playerSpawned || goingOffline) return;

        if (connectAttempts < maxOnlineAttempts)
        {
            Debug.LogWarning($"Повторна спроба підключення через {retryDelay} с...");
            StartCoroutine(ConnectRoutine(retryDelay));
        }
        else if (fallbackToOffline)
        {
            StartCoroutine(GoOfflineRoutine());
        }
        else
        {
            Debug.LogError("Не вдалось підключитись до Photon, а fallbackToOffline вимкнено - гравець не заспавниться.");
        }
    }

    private IEnumerator GoOfflineRoutine()
    {
        yield return null;

        if (playerSpawned || goingOffline) yield break;
        goingOffline = true;

        // OfflineMode не можна вмикати, поки клієнт підключений - спершу відключаємось.
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            PhotonNetwork.Disconnect();

            float waited = 0f;
            while (PhotonNetwork.IsConnected && waited < 5f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        Debug.LogWarning("Онлайн недоступний - запускаю Offline Mode (одиночна гра).");
        PhotonNetwork.OfflineMode = true;
    }

    public override void OnConnectedToMaster()
    {
        PhotonNetwork.JoinOrCreateRoom(roomName, new RoomOptions(), TypedLobby.Default);
    }

    public override void OnJoinedRoom()
    {
        SpawnPlayer();
    }

    private void SpawnPlayer()
    {
        GameObject existing = PhotonNetwork.LocalPlayer.TagObject as GameObject;
        if (existing != null)
        {
            Debug.LogWarning("Гравець вже заспавнений для цього актора - пропускаю повторний Instantiate.");
            return;
        }

        Vector3 spawnPosition = Vector3.zero;
        Quaternion spawnRotation = Quaternion.identity;

        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            Transform point = spawnPoints[Random.Range(0, spawnPoints.Length)];
            if (point != null)
            {
                spawnPosition = point.position;
                spawnRotation = point.rotation;
            }
        }

        GameObject player = PhotonNetwork.Instantiate(playerPrefabName, spawnPosition, spawnRotation);
        PhotonNetwork.LocalPlayer.TagObject = player;
        playerSpawned = true;

        if (spawnCutscene != null)
            spawnCutscene.Play();

        if (LoadingScreenController.Instance != null)
            LoadingScreenController.Instance.OnPlayerSpawned();
    }

    public override void OnLeftRoom()
    {
        PhotonNetwork.LocalPlayer.TagObject = null;
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning($"Відключено від Photon. Причина: {cause}");
        PhotonNetwork.LocalPlayer.TagObject = null;

        if (cause == DisconnectCause.DisconnectByClientLogic) return;

        HandleConnectionFailed();
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"Не вдалось зайти в кімнату: {message}");
    }
}