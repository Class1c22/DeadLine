using UnityEngine;

// Повісити на об'єкт з Box/Mesh Collider (Is Trigger = true), що позначає
// об'єм води. Сам WaterZone більше НЕ вирішує, коли гравець "під водою" -
// він лише повідомляє PlayerBreath, що тіло гравця перебуває в об'ємі води,
// і передає висоту поверхні (верх колайдера). Остаточне рішення "камера під
// водою чи ні" приймає сам PlayerBreath, звіряючи Y камери з цією висотою -
// так бар кисню з'являється/зникає саме коли КАМЕРА перетинає поверхню,
// а не коли тіло торкнулось тригера.
//
// СПЛЕНШ-ЕФЕКТ грається двома способами:
// 1) Автоматично - для БУДЬ-ЯКОГО об'єкта, що зайшов у тригер води
//    (TrySpawnSplashFromRigidbody). Швидкість падіння більше НЕ перевіряється -
//    сплеск/звук грається завжди, незалежно від того, як швидко об'єкт падав
//    і чи є в нього Rigidbody взагалі.
// 2) Напряму - через публічний метод SpawnSplashAt(pos), який можуть
//    викликати інші скрипти (напр. FishingHook), коли вони САМІ точно
//    знають, що торкнулись води (напр. гачок рухається кінематично по дузі,
//    і rb.linearVelocity в нього не відображає реальну швидкість польоту).
public class WaterZone : MonoBehaviour
{
    private Collider waterCollider;

    [Header("Спленш-ефект")]
    [Tooltip("Префаб ParticleSystem, що програється один раз у точці входу в воду")]
    [SerializeField] private ParticleSystem splashPrefab;

    [Tooltip("Кулдаун між спленшами (сек). Спільний для всіх джерел спленшу цієї зони, " +
             "щоб частинки/звук не спамили, коли кілька об'єктів входять у воду одночасно.")]
    [SerializeField] private float splashCooldown = 0.5f;

    [Header("Звук спленшу")]
    [Tooltip("Звук булькання/сплеску, що програється в точці входу в воду (напр. коли буйок падає у воду)")]
    [SerializeField] private AudioClip splashSound;

    [Tooltip("Гучність звуку сплеску")]
    [SerializeField] private float splashVolume = 1f;

    [Tooltip("Розкид висоти тону (pitch), щоб однакові сплески не звучали монотонно однаково")]
    [SerializeField] private Vector2 splashPitchRange = new Vector2(0.9f, 1.1f);

    private float lastSplashTime = -999f;

    void Awake()
    {
        waterCollider = GetComponent<Collider>();
    }

    /// <summary>Y-координата поверхні води (верх колайдера зони).</summary>
    public float SurfaceY => waterCollider != null ? waterCollider.bounds.max.y : transform.position.y;

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[WaterZone] OnTriggerEnter: {other.name}, tag = {other.tag}");

        TrySpawnSplashFromRigidbody(other);

        if (!other.CompareTag("Player"))
        {
            Debug.Log($"[WaterZone] Пропущено - тег не 'Player' (реальний тег: {other.tag})");
            return;
        }

        // GetComponentInParent, бо колайдер гравця (CharacterController) часто
        // висить на дочірньому об'єкті (напр. mainhero_animated), тоді як
        // PhotonView і PlayerBreath - на кореневому об'єкті гравця.
        var photonView = other.GetComponentInParent<Photon.Pun.PhotonView>();
        if (photonView == null)
        {
            Debug.Log("[WaterZone] Пропущено - немає PhotonView на об'єкті (ні на ньому, ні на батьках)");
            return;
        }

        if (!photonView.IsMine)
        {
            Debug.Log("[WaterZone] Пропущено - це чужий гравець (IsMine = false)");
            return;
        }

        var breath = other.GetComponentInParent<PlayerBreath>();
        if (breath == null)
        {
            Debug.Log("[WaterZone] Пропущено - немає компонента PlayerBreath (ні на ньому, ні на батьках)");
            return;
        }

        Debug.Log($"[WaterZone] {other.name} у зоні води. Поверхня на Y = {SurfaceY}");
        breath.SetInWaterVolume(true, SurfaceY);
    }

    private void OnTriggerExit(Collider other)
    {
        Debug.Log($"[WaterZone] OnTriggerExit: {other.name}, tag = {other.tag}");

        if (!other.CompareTag("Player")) return;

        var photonView = other.GetComponentInParent<Photon.Pun.PhotonView>();
        if (photonView == null || !photonView.IsMine) return;

        var breath = other.GetComponentInParent<PlayerBreath>();
        if (breath != null)
        {
            Debug.Log($"[WaterZone] {other.name} покинув зону води");
            breath.SetInWaterVolume(false, 0f);
        }
    }

    // ---------------------------------------------------------------
    // СПЛЕНШ-ЕФЕКТ (автоматичний варіант)
    //
    // Спрацьовує для БУДЬ-ЯКОГО об'єкта (не тільки гравця), що зайшов
    // у тригер води - без перевірки швидкості падіння. Ефект суто
    // косметичний і програється ЛОКАЛЬНО на кожному клієнті (Instantiate,
    // не PhotonNetwork.Instantiate) - це нормальна практика для частинок:
    // мережевий трафік на них не витрачається, а фізика синхронізованого
    // об'єкта у Photon все одно виконується на кожному клієнті приблизно
    // однаково, тож спленш з'явиться в потрібний момент і у гравця, і
    // у тих, хто на нього дивиться.
    // ---------------------------------------------------------------
    private void TrySpawnSplashFromRigidbody(Collider other)
    {
        if (splashPrefab == null && splashSound == null)
        {
            Debug.Log("[WaterZone] TrySpawnSplashFromRigidbody: і splashPrefab, і splashSound не призначені - виходжу");
            return;
        }

        Vector3 pos = other.transform.position;
        SpawnSplashInternal(new Vector3(pos.x, SurfaceY, pos.z));
    }

    /// <summary>
    /// ПУБЛІЧНИЙ метод для прямого виклику з інших скриптів (напр. FishingHook),
    /// коли вони самі точно знають, що торкнулись саме цієї води. worldPos -
    /// позиція об'єкта в момент дотику; Y автоматично підміняється на висоту
    /// поверхні (SurfaceY), тож можна передавати позицію самого об'єкта як є.
    /// </summary>
    public void SpawnSplashAt(Vector3 worldPos)
    {
        SpawnSplashInternal(new Vector3(worldPos.x, SurfaceY, worldPos.z));
    }

    private void SpawnSplashInternal(Vector3 splashPos)
    {
        Debug.Log($"[WaterZone] SpawnSplashInternal викликано в точці {splashPos}. splashPrefab={(splashPrefab != null)}, splashSound={(splashSound != null)}");

        // Якщо не призначено ні партикл, ні звук - робити нічого, навіть кулдаун не чіпаємо.
        if (splashPrefab == null && splashSound == null)
        {
            Debug.Log("[WaterZone] SpawnSplashInternal: і splashPrefab, і splashSound не призначені в інспекторі - виходжу");
            return;
        }

        if (Time.time - lastSplashTime < splashCooldown)
        {
            Debug.Log($"[WaterZone] SpawnSplashInternal: спрацював кулдаун ({Time.time - lastSplashTime:F2}с < {splashCooldown}с) - звук/спленш НЕ програється");
            return;
        }
        lastSplashTime = Time.time;

        if (splashPrefab != null)
        {
            ParticleSystem fx = Instantiate(splashPrefab, splashPos, Quaternion.identity);
            Destroy(fx.gameObject, 0.5f);
            Debug.Log("[WaterZone] Партикл спленшу заспавнено");
        }

        PlaySplashSound(splashPos);

        Debug.Log($"[WaterZone] Спленш у точці {splashPos}");
    }

    private void PlaySplashSound(Vector3 splashPos)
    {
        if (splashSound == null)
        {
            Debug.Log("[WaterZone] PlaySplashSound: splashSound не призначений в інспекторі - звук не грає");
            return;
        }

        Debug.Log($"[WaterZone] PlaySplashSound: створюю AudioSource для кліпу '{splashSound.name}', volume={splashVolume}");

        // AudioSource.PlayClipAtPoint сам створює тимчасовий GameObject зі своїм
        // AudioSource, програє звук і сам себе знищує - зручно для одноразових
        // звуків типу сплеску, не треба тримати AudioSource на WaterZone.
        // Гучність і pitch тут задати напряму не можна (PlayClipAtPoint не має
        // параметра pitch), тому для розкиду тону створюємо тимчасовий об'єкт вручну.
        GameObject tempAudio = new GameObject("SplashSound_Temp");
        tempAudio.transform.position = splashPos;

        AudioSource source = tempAudio.AddComponent<AudioSource>();
        source.clip = splashSound;
        source.volume = splashVolume;
        source.pitch = Random.Range(splashPitchRange.x, splashPitchRange.y);
        source.spatialBlend = 1f; // 3D-звук - гучність залежить від відстані до слухача
        source.Play();

        Debug.Log($"[WaterZone] PlaySplashSound: source.Play() викликано, isPlaying={source.isPlaying}, pitch={source.pitch}");

        Destroy(tempAudio, splashSound.length / Mathf.Max(source.pitch, 0.01f));
    }
}