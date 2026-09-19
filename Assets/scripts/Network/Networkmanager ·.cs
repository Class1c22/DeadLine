using System.Collections;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.Playables;

// Цей скрипт підключає гру до Photon Cloud (мережа + голос одночасно),
// спавнить гравця в спільній кімнаті і коректно респавнить його після
// PhotonNetwork.LoadLevel (напр. коли гру перезапускають кнопкою "New Game").
//
// НАЛАШТУВАННЯ:
// 1. Перенеси свій префаб гравця (mainhero_animated) у папку Assets/Resources/.
// 2. Створи порожній GameObject "NetworkManager" у сцені, додай цей скрипт.
// 3. У полі Player Prefab Name встав точну назву префабу з Resources.
// 4. Створи порожні GameObject-точки спавну (напр. "SpawnPoint1", "SpawnPoint2")
//    і перетягни їх у масив Spawn Points. Якщо масив порожній - спавн у (0,0,0).
// 5. Переконайся, що сцена додана в File -> Build Settings -> Scenes In Build
//    (PhotonNetwork.LoadLevel вимагає, щоб сцена мала build index).
//
// ВАЖЛИВО: у сцені має бути ТІЛЬКИ ОДИН об'єкт з цим скриптом —
// два NetworkManager викликають OnJoinedRoom двічі і спавнять двох персонажів.
//
// ПІДКЛЮЧЕННЯ (нове):
//  - Підключення стартує через 1 кадр після Start(), коли всі інші скрипти
//    (острів, пальми, акула) уже відпрацювали свої Start().
//  - Спроба 1: протокол із PhotonServerSettings (зазвичай UDP).
//    Якщо таймаут - спроба 2: TCP (UDP часто блокують VPN/фаєрвол/провайдер).
//  - Якщо всі спроби невдалі - Offline Mode (одиночна гра), щоб екран
//    завантаження не висів вічно.
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
        // Без цього PhotonNetwork.LoadLevel перезавантажить сцену лише в того,
        // хто його викликав (MasterClient) - усі інші лишаться в старій сцені.
        PhotonNetwork.AutomaticallySyncScene = true;

        // Без цього, коли вікно Unity/гри втрачає фокус, Update/FixedUpdate
        // зупиняються, Photon не диспатчить пакети і ловить TimeoutDisconnect
        // (у логах це "AppOutOfFocus").
        Application.runInBackground = true;
    }

    private void Start()
    {
        if (PhotonNetwork.IsConnectedAndReady && PhotonNetwork.InRoom)
        {
            // Ми вже підключені й у кімнаті - це означає, що сцена щойно
            // перезавантажилась через PhotonNetwork.LoadLevel (наприклад,
            // після натискання "New Game"), а не перший запуск гри.
            // OnConnectedToMaster/OnJoinedRoom вдруге НЕ викличуться (ми і так
            // вже в кімнаті), тому спавнимось напряму тут.
            Debug.Log("Сцена перезавантажена - спавню гравця напряму.");
            SpawnPlayer();
            return;
        }

        StartCoroutine(ConnectRoutine(0f));
    }

    private IEnumerator ConnectRoutine(float delay)
    {
        // Чекаємо кадр: до цього моменту всі Start() у сцені виконані
        // (генерація острова, пальми, акула), тож головний потік не буде
        // заблокований під час handshake з Photon.
        yield return null;

        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        ConnectOnline();
    }

    private void ConnectOnline()
    {
        connectAttempts++;
        bool useTcp = connectAttempts > 1;

        Debug.Log($"Підключення до Photon... (спроба {connectAttempts}/{maxOnlineAttempts}, " +
                  $"протокол: {(useTcp ? "TCP" : "з PhotonServerSettings")})");

        // Копія налаштувань - щоб не змінювати сам ассет PhotonServerSettings.
        AppSettings settings = new AppSettings();
        PhotonNetwork.PhotonServerSettings.AppSettings.CopyTo(settings);

        if (useTcp)
            settings.Protocol = ExitGames.Client.Photon.ConnectionProtocol.Tcp;

        PhotonNetwork.NetworkingClient.LoadBalancingPeer.DisconnectTimeout = disconnectTimeoutMs;

        if (!PhotonNetwork.ConnectUsingSettings(settings))
        {
            Debug.LogError("PhotonNetwork.ConnectUsingSettings повернув false - перевір AppId у PhotonServerSettings.");
            HandleConnectionFailed();
        }
    }

    // Єдина точка, де вирішуємо: ще одна спроба, офлайн чи здатись.
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
        // Чекаємо кадр, щоб клієнт Photon встиг повністю перейти у стан Disconnected.
        yield return null;

        if (playerSpawned || goingOffline) yield break;
        goingOffline = true;

        Debug.LogWarning("Онлайн недоступний - запускаю Offline Mode (одиночна гра).");

        // Увімкнення OfflineMode одразу викликає OnConnectedToMaster,
        // а той - JoinOrCreateRoom -> OnJoinedRoom -> SpawnPlayer.
        PhotonNetwork.OfflineMode = true;
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log("Підключено до Photon Master Server. Приєднуюсь до кімнати...");
        PhotonNetwork.JoinOrCreateRoom(roomName, new RoomOptions(), TypedLobby.Default);
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"Зайшов у кімнату '{roomName}'. Гравців у кімнаті: {PhotonNetwork.CurrentRoom.PlayerCount}");
        SpawnPlayer();
    }

    private void SpawnPlayer()
    {
        // Захист від подвійного спавну ТІЛЬКИ в межах одного й того самого
        // життя сцени: TagObject перевіряємо через Unity-cast, бо після
        // PhotonNetwork.LoadLevel стара посилання-обгортка технічно не null
        // на рівні C#, хоча сам GameObject уже знищений.
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

        // PhotonNetwork.Instantiate виконується локально для того клієнта,
        // що спавниться - тому це саме той момент "гравець заспавнився",
        // а не спавн чужих гравців по мережі.
        if (spawnCutscene != null)
        {
            spawnCutscene.Play();
        }

        if (LoadingScreenController.Instance != null)
        {
            LoadingScreenController.Instance.OnPlayerSpawned();
        }
    }

    public override void OnLeftRoom()
    {
        PhotonNetwork.LocalPlayer.TagObject = null;
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning($"Відключено від Photon. Причина: {cause}");
        PhotonNetwork.LocalPlayer.TagObject = null;

        // Свідоме відключення (LeaveRoom/Disconnect з коду) - не помилка.
        if (cause == DisconnectCause.DisconnectByClientLogic) return;

        // Ретраї/офлайн лише поки гравець ще не в грі - розрив посеред гри тут не чіпаємо.
        HandleConnectionFailed();
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"Не вдалось зайти в кімнату: {message}");
    }
}