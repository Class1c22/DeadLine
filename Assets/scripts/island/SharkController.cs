using Photon.Pun;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// ВАЖЛИВО про мультиплеєр: акула - об'єкт сцени (не спавниться рантайм),
// тому її PhotonView теж налаштований як "Scene Object" - власником
// автоматично є поточний MasterClient, отже photonView.IsMine тут
// еквівалентно PhotonNetwork.IsMasterClient.
//
// Лише MasterClient РЕАЛЬНО рухає акулу (Patrol/Update/LateUpdate нижче).
// На інших клієнтах цей скрипт не чіпає transform взагалі - позиція й
// поворот приходять по мережі через Photon Transform View (додати в
// інспекторі на PhotonView акули -> Observed Components), а тригери
// анімації (Bite/Eat/EatFish) - через Photon Animator View або явний RPC
// (див. TODO нижче), інакше на екранах гравців акула або не рухається,
// або в кожного пливе по-своєму й кусає в різний час.
//
// ЗВУКИ: укус острова та поїдання риби відбуваються лише на MasterClient
// (корутини BiteRoutine/EatFishRoutine), тому їхні звуки розсилаються всім
// через RPC_PlaySound (RpcTarget.All). Звуки лайка/дизлайка грають у
// ShowLikeMaterial()/ShowDislikeEffect(), які вже викликаються з
// RPC_AddProgress(RpcTarget.All) - тому окремий RPC для них не потрібен.
[RequireComponent(typeof(Animator), typeof(PhotonView))]
public class SharkController : MonoBehaviourPun
{
    private enum SharkSound
    {
        Bite = 0,
        EatFish = 1
    }

    [Header("Патрулювання (коло навколо порожнього об'єкта)")]
    public Transform orbitCenter;
    public float patrolRadius = 25f;
    public float patrolHeight = 2f;
    public float patrolSpeed = 15f;
    public float patrolBobAmplitude = 0.5f;
    public float patrolBobSpeed = 1f;
    public float rotateSpeed = 5f;
    public float visualForwardOffsetY = 0f;

    [Header("Укус (зупинка на потрібному градусі)")]
    public string biteTriggerName = "Bite";
    [Range(0f, 1f)] public float biteImpactFraction = 0.4f;

    [Header("Поїдання (після укусу, акула стоїть на місці)")]
    public string eatTriggerName = "Eat";
    public float eatHoldDuration = 4f;
    public float eatShakeAmplitude = 0.15f;
    public float eatShakeSpeed = 6f;

    [Header("Поїдання риби в морі")]
    public float minEatDistanceFromIsland = 20f;
    public string eatFishTriggerName = "EatFish";
    public float eatFishSwimSpeed = 8f;
    public float eatFishStopDistance = 1.2f;
    public float swimBackDuration = 1f;
    public float visualForwardOffsetYFacingIsland = 0f;

    [Header("Смаки акули (рандомізуються ОДИН РАЗ на весь ігровий сеанс, вирішує MasterClient)")]
    [Tooltip("Усі 5 можливих видів риби - значення мають ТОЧНО збігатись із Pickupable.fishSpeciesId на префабах риби.")]
    public string[] allFishSpeciesIds = new string[5];
    [Tooltip("Скільки видів із allFishSpeciesIds акула ЛЮБИТЬ (прогрес +1). Решта видів вона НЕ любить (прогрес -1).")]
    public int likedSpeciesCount = 2;
    [Tooltip("Назва Trigger-параметра в Animator акули, що грає, коли риба їй сподобалась")]
    public string likeTriggerName = "Like";
    [Tooltip("Назва Trigger-параметра в Animator акули, що грає, коли риба їй НЕ сподобалась")]
    public string dislikeTriggerName = "Dislike";
    [Tooltip("Пауза між тригером 'Eat' і тригером 'Like'/'Dislike', щоб анімації не накладались одна на одну")]
    public float reactionDelay = 1.5f;
    [Tooltip("Прогрес-бар цього КОНКРЕТНОГО клієнта (кожен гравець бачить свій локальний UI-об'єкт з тим самим сценним ієрархічним шляхом) - призначити в інспекторі, а не передавати ззовні, бо через RPC не можна передати посилання на Unity-об'єкт.")]
    public FishProgressBar progressBar;
    [Tooltip("Скільки риби треба, щоб бар заповнився і показався екран перемоги. Задається тут (на акулі), а не на кожному FishProgressBar окремо, і застосовується до progressBar автоматично в Start().")]
    public int fishNeededToWin = 10;
    [Tooltip("Скільки секунд максимум чекати RPC_SetLikedSpecies перед тим, як зарахувати рибу нейтрально (0), а не як 'не сподобалась' (-1). Захист від гонки на старті сесії.")]
    public float maxPreferencesWaitSeconds = 3f;

    [Header("Візуальна реакція (матеріал акули при 'улюбленій' рибі)")]
    [Tooltip("Renderer акули, чий матеріал змінюється. Якщо не задано - буде знайдено автоматично через GetComponentInChildren<Renderer>().")]
    public Renderer sharkRenderer;
    [Tooltip("Матеріал, що вмикається на короткий час, коли акула з'їла улюблену рибу.")]
    public Material likeMaterial;
    [Tooltip("Скільки секунд тримати 'улюблений' матеріал, перш ніж повернути стандартний.")]
    public float likeMaterialDuration = 2f;

    [Header("VFX улюбленої риби (сердечка вгору)")]
    [Tooltip("Префаб Particle System з сердечками. Спавниться ЛОКАЛЬНО на кожному клієнті всередині ShowLikeMaterial(), який вже викликається з RPC_AddProgress(RpcTarget.All) - додаткового RPC не потрібно, ефект і так синхронний для всіх.")]
    public GameObject likeHeartsEffectPrefab;
    [Tooltip("Точка спавну ефектів. Якщо не задано - трохи вище акули (transform.position + Vector3.up * 1.5). Використовується і для лайка, і для дизлайка.")]
    public Transform heartsSpawnPoint;
    [Tooltip("Через скільки секунд знищити заспавнений об'єкт ефекту лайка (має бути >= тривалості партиклів, щоб не обрізати анімацію).")]
    public float heartsEffectLifetime = 2.5f;

    [Header("VFX нелюбої риби (гнівні риски)")]
    [Tooltip("Префаб Particle System / об'єкт з 'гнівними рисками', що спавниться, коли рибу зараховано як НЕ улюблену (liked == false). Спавниться так само локально з RPC_AddProgress(RpcTarget.All), тобто синхронно на всіх клієнтах.")]
    public GameObject dislikeEffectPrefab;
    [Tooltip("Через скільки секунд знищити заспавнений об'єкт ефекту дизлайка.")]
    public float dislikeEffectLifetime = 2.5f;

    [Header("Звуки")]
    [Tooltip("AudioSource, через який граються всі звуки акули. Якщо не задано - береться AudioSource з цього ж об'єкта. Для звуку 'з акули' постав Spatial Blend = 1 (3D), для однакової гучності всюди - 0 (2D). Play On Awake вимкни.")]
    public AudioSource audioSource;

    [Tooltip("Звук укусу острова. Грається в момент ВЛУЧАННЯ укусу (коли острів фактично деформується) - синхронно на всіх клієнтах.")]
    public AudioClip biteSound;
    [Range(0f, 1f)] public float biteVolume = 1f;

    [Tooltip("Звук поїдання риби в морі. Грається разом із тригером EatFish - синхронно на всіх клієнтах.")]
    public AudioClip eatFishSound;
    [Range(0f, 1f)] public float eatFishVolume = 1f;

    [Tooltip("Звук, коли акулі СПОДОБАЛАСЬ риба (разом із сердечками).")]
    public AudioClip likeSound;
    [Range(0f, 1f)] public float likeVolume = 1f;

    [Tooltip("Звук, коли акулі НЕ сподобалась риба (разом із гнівними рисками).")]
    public AudioClip dislikeSound;
    [Range(0f, 1f)] public float dislikeVolume = 1f;

    private Material defaultMaterial;
    private Coroutine likeMaterialRoutine;

    private readonly HashSet<string> likedSpecies = new HashSet<string>();
    private bool preferencesReady = false;

    private Animator animator;
    private float patrolAngle;
    private bool isBiting = false;
    private bool isEating = false;

    private float? pendingTargetAngle = null;
    private System.Action pendingOnImpact;
    private float pendingBiteDuration;

    private Vector3? forcedPos;
    private Quaternion? forcedRot;

    void Start()
    {
        animator = GetComponent<Animator>();
        animator.applyRootMotion = false;

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (orbitCenter == null)
            Debug.LogWarning("[SharkController] Orbit Center не задано - акула не буде патрулювати.");

        if (sharkRenderer == null)
            sharkRenderer = GetComponentInChildren<Renderer>();

        if (sharkRenderer != null)
            defaultMaterial = sharkRenderer.sharedMaterial;
        else
            Debug.LogWarning("[SharkController] Не знайдено Renderer - зміна матеріалу при уподобаній рибі працювати не буде.");

        // Застосовуємо потрібну кількість риби до локального прогрес-бару
        // ЦЬОГО клієнта. Виконується для всіх клієнтів (і мастера, і не-мастера),
        // бо в кожного progressBar - свій локальний UI-об'єкт.
        if (progressBar != null)
            progressBar.fishNeeded = fishNeededToWin;
        else
            Debug.LogWarning("[SharkController] progressBar не призначено в інспекторі - fishNeededToWin не буде застосовано.");

        if (!photonView.IsMine)
        {
            // Ми не MasterClient - не ініціалізуємо власний patrolAngle рандомом
            // (він все одно ігнорується, бо Update()/LateUpdate() нижче виходять
            // одразу для не-власника; позицію дає Photon Transform View).
            return;
        }

        patrolAngle = Random.Range(0f, 360f);

        RollFoodPreferences();
    }

    /// <summary>
    /// Викликається лише на MasterClient (див. виклик у Start()). Тасує
    /// allFishSpeciesIds і бере перші likedSpeciesCount як "улюблені".
    /// ВАЖЛИВО: результат застосовується ЛОКАЛЬНО одразу (синхронно, без
    /// чекання на власний RPC), тому MasterClient може коректно їсти рибу
    /// з першої ж секунди - незалежно від того, коли (і чи взагалі) він уже
    /// підключений до Photon. RPC потрібен лише щоб розповісти про смаки
    /// ІНШИМ гравцям (для їхнього UI/анімацій) і буферизується для тих,
    /// хто зайде в кімнату пізніше - його відправка чекає на PhotonNetwork.InRoom,
    /// інакше виклик падає з "Cannot send messages when not connected".
    /// </summary>
    private void RollFoodPreferences()
    {
        if (allFishSpeciesIds == null || allFishSpeciesIds.Length == 0)
        {
            Debug.LogWarning("[SharkController] allFishSpeciesIds порожній - неможливо визначити смаки акули.");
            return;
        }

        List<string> shuffled = new List<string>(allFishSpeciesIds);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        int count = Mathf.Clamp(likedSpeciesCount, 0, shuffled.Count);
        string[] liked = shuffled.GetRange(0, count).ToArray();

        // Застосовуємо одразу локально - MasterClient не залежить від мережі,
        // щоб знати власне рішення.
        ApplyLikedSpecies(liked);

        // А цим ділимося з рештою гравців, коли мережа справді готова.
        StartCoroutine(BroadcastLikedSpeciesWhenReady(liked));
    }

    private IEnumerator BroadcastLikedSpeciesWhenReady(string[] liked)
    {
        while (!PhotonNetwork.InRoom)
            yield return null;

        photonView.RPC(nameof(RPC_SetLikedSpecies), RpcTarget.OthersBuffered, (object)liked);
    }

    private void ApplyLikedSpecies(string[] liked)
    {
        likedSpecies.Clear();
        foreach (string id in liked)
            likedSpecies.Add(id);

        preferencesReady = true;

        Debug.Log("[SharkController] Смаки акули визначено. Любить: " + string.Join(", ", liked));
    }

    [PunRPC]
    private void RPC_SetLikedSpecies(string[] liked)
    {
        ApplyLikedSpecies(liked);
    }

    [PunRPC]
    private void RPC_AddProgress(int delta, bool liked)
    {
        Debug.Log($"[SharkController] RPC_AddProgress отримано delta={delta}, liked={liked} (likedSpecies зараз: [{string.Join(", ", likedSpecies)}], preferencesReady={preferencesReady})");

        if (progressBar != null)
            progressBar.AddFish(delta);
        else
            Debug.LogWarning("[SharkController] progressBar не призначено в інспекторі - неможливо оновити бар.");

        if (liked)
            ShowLikeMaterial();
        else
            ShowDislikeEffect();
    }

    // ------------------------------------------------------------------
    // ЗВУКИ
    // ------------------------------------------------------------------

    /// <summary>Грає один звук локально (на цьому клієнті). Нічого не робить, якщо AudioSource або AudioClip не призначені.</summary>
    private void PlayClip(AudioClip clip, float volume)
    {
        if (audioSource == null || clip == null) return;

        audioSource.PlayOneShot(clip, volume);
    }

    /// <summary>
    /// Грає звук укусу/поїдання риби на ВСІХ клієнтах. Викликається лише з
    /// корутин, що виконуються на MasterClient (BiteRoutine/EatFishRoutine),
    /// тому без RPC інші гравці цих звуків просто не почули б. Якщо ми не в
    /// кімнаті (напр. одиночний тест сцени) - граємо тільки локально.
    /// </summary>
    private void PlaySoundForAll(SharkSound sound)
    {
        if (PhotonNetwork.InRoom && photonView != null)
            photonView.RPC(nameof(RPC_PlaySound), RpcTarget.All, (int)sound);
        else
            PlaySoundLocal(sound);
    }

    [PunRPC]
    private void RPC_PlaySound(int soundId)
    {
        PlaySoundLocal((SharkSound)soundId);
    }

    private void PlaySoundLocal(SharkSound sound)
    {
        switch (sound)
        {
            case SharkSound.Bite:
                PlayClip(biteSound, biteVolume);
                break;

            case SharkSound.EatFish:
                PlayClip(eatFishSound, eatFishVolume);
                break;
        }
    }

    /// <summary>
    /// Тимчасово перемикає матеріал акули на likeMaterial, а через
    /// likeMaterialDuration повертає стандартний, спавнить VFX сердечок і
    /// грає звук лайка. Викликається з RPC_AddProgress, тобто виконується
    /// ОДНАКОВО на всіх клієнтах - додаткового RPC для самого ефекту не потрібно.
    /// </summary>
    private void ShowLikeMaterial()
    {
        if (sharkRenderer != null && likeMaterial != null)
        {
            if (likeMaterialRoutine != null)
                StopCoroutine(likeMaterialRoutine);

            likeMaterialRoutine = StartCoroutine(LikeMaterialRoutine());
        }

        SpawnLikeHearts();
        PlayClip(likeSound, likeVolume);
    }

    private IEnumerator LikeMaterialRoutine()
    {
        sharkRenderer.material = likeMaterial;

        yield return new WaitForSeconds(likeMaterialDuration);

        if (sharkRenderer != null && defaultMaterial != null)
            sharkRenderer.material = defaultMaterial;

        likeMaterialRoutine = null;
    }

    /// <summary>
    /// Спавнить одноразовий партикл-ефект (сердечка) над акулою.
    /// Викликається локально на кожному клієнті з ShowLikeMaterial(),
    /// яка сама вже викликана через RPC_AddProgress(RpcTarget.All) -
    /// тому ефект з'являється синхронно на всіх екранах без окремого RPC.
    /// </summary>
    private void SpawnLikeHearts()
    {
        if (likeHeartsEffectPrefab == null) return;

        Vector3 pos = heartsSpawnPoint != null
            ? heartsSpawnPoint.position
            : transform.position + Vector3.up * 1.5f;

        GameObject fx = Instantiate(likeHeartsEffectPrefab, pos, Quaternion.identity);
        Destroy(fx, heartsEffectLifetime);
    }

    /// <summary>
    /// Викликається з RPC_AddProgress, коли liked == false (рибу зараховано
    /// як НЕ улюблену). На відміну від лайка, матеріал акули тут НЕ міняється -
    /// лише спавниться VFX "гнівних рисок" і грає звук дизлайка. Так само
    /// локально на кожному клієнті, синхронно, бо сам RPC_AddProgress вже
    /// прийшов з RpcTarget.All.
    /// </summary>
    private void ShowDislikeEffect()
    {
        SpawnDislikeEffect();
        PlayClip(dislikeSound, dislikeVolume);
    }

    private void SpawnDislikeEffect()
    {
        if (dislikeEffectPrefab == null) return;

        Vector3 pos = heartsSpawnPoint != null
            ? heartsSpawnPoint.position
            : transform.position + Vector3.up * 1.5f;

        GameObject fx = Instantiate(dislikeEffectPrefab, pos, Quaternion.identity);
        Destroy(fx, dislikeEffectLifetime);
    }

    void Update()
    {
        // Тільки MasterClient рахує рух і стан укусу. Інші клієнти отримують
        // готову позицію/поворот з мережі (Photon Transform View) і сюди
        // взагалі не заходять - інакше кожен порахував би свій власний
        // patrolAngle і свій власний момент початку укусу.
        if (!photonView.IsMine) return;

        if (isBiting || isEating) return;

        Patrol();

        if (pendingTargetAngle.HasValue && patrolAngle >= pendingTargetAngle.Value)
        {
            patrolAngle = pendingTargetAngle.Value;
            pendingTargetAngle = null;
            ApplyPatrolTransform(patrolAngle, 0f);

            var onImpact = pendingOnImpact;
            var duration = pendingBiteDuration;
            pendingOnImpact = null;
            StartCoroutine(BiteRoutine(onImpact, duration));
        }
    }

    void LateUpdate()
    {
        if (!photonView.IsMine) return;

        if (forcedPos.HasValue) transform.position = forcedPos.Value;
        if (forcedRot.HasValue) transform.rotation = forcedRot.Value;
    }

    void Patrol()
    {
        if (orbitCenter == null) return;

        patrolAngle += patrolSpeed * Time.deltaTime;
        float bob = Mathf.Sin(Time.time * patrolBobSpeed) * patrolBobAmplitude;
        ApplyPatrolTransform(patrolAngle, bob);
    }

    void ApplyPatrolTransform(float angleDeg, float bob)
    {
        if (orbitCenter == null) return;

        float rad = angleDeg * Mathf.Deg2Rad;

        Vector3 targetPos = orbitCenter.position + new Vector3(
            Mathf.Cos(rad) * patrolRadius,
            patrolHeight + bob,
            Mathf.Sin(rad) * patrolRadius
        );
        transform.position = targetPos;

        Vector3 tangent = new Vector3(-Mathf.Sin(rad), 0, Mathf.Cos(rad));
        if (tangent != Vector3.zero)
        {
            Quaternion lookRot = Quaternion.LookRotation(tangent) * Quaternion.Euler(0f, visualForwardOffsetY, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, rotateSpeed * Time.deltaTime);
        }
    }

    public bool IsBusyWithBite => isBiting || isEating;
    public bool HasPendingOrActiveBite => isBiting || isEating || pendingTargetAngle.HasValue;

    /// <summary>Викликається лише SharkBiteController, який сам вже гарантує PhotonNetwork.IsMasterClient.</summary>
    public void RequestBite(float desiredAngleDeg, System.Action onBiteImpact, float biteDuration)
    {
        if (!photonView.IsMine) return; // подвійна підстраховка
        if (IsBusyWithBite) return;

        float delta = Mathf.Repeat(desiredAngleDeg - patrolAngle, 360f);
        if (delta < 1f) delta += 360f;

        pendingTargetAngle = patrolAngle + delta;
        pendingOnImpact = onBiteImpact;
        pendingBiteDuration = biteDuration;
    }

    /// <summary>Викликається зовнішнім кодом поїдання риби. Має сенс лише на MasterClient.</summary>
    public void RequestEatFish(Transform fish)
    {
        if (!photonView.IsMine) return;
        if (isBiting || isEating) return;
        if (fish == null) return;

        pendingTargetAngle = null;
        pendingOnImpact = null;

        StartCoroutine(EatFishRoutine(fish));
    }

    private IEnumerator EatFishRoutine(Transform fish)
    {
        isEating = true;

        // Визначаємо вид риби ДО Destroy() нижче, поки об'єкт ще існує.
        string speciesId = null;
        if (fish != null)
        {
            Pickupable pickupable = fish.GetComponent<Pickupable>();
            if (pickupable != null)
                speciesId = pickupable.fishSpeciesId;
        }

        if (string.IsNullOrEmpty(speciesId))
            Debug.LogWarning($"[SharkController] У риби '{fish?.name}' не задано fishSpeciesId - вважаю, що акулі вона не смакує.");

        // Захист від гонки: якщо RPC_SetLikedSpecies ще не долетів (наприклад,
        // риба з'їдена одразу на старті сесії), почекаємо трохи, а не одразу
        // рахуємо рибу "нелюбою" (-1). Якщо смаки так і не прийшли за
        // maxPreferencesWaitSeconds - це, найімовірніше, проблема конфігурації
        // (allFishSpeciesIds/likedSpeciesCount в інспекторі), і краще зарахувати
        // рибу нейтрально (0), ніж систематично займати прогрес у мінус.
        bool preferencesTimedOut = false;
        if (!preferencesReady)
        {
            float waited = 0f;
            while (!preferencesReady && waited < maxPreferencesWaitSeconds)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            if (!preferencesReady)
            {
                preferencesTimedOut = true;
                Debug.LogError("[SharkController] Смаки акули (likedSpecies) так і не прийшли за " +
                    maxPreferencesWaitSeconds + " сек. Перевір allFishSpeciesIds/likedSpeciesCount в " +
                    "інспекторі та значення Pickupable.fishSpeciesId на префабах риби. Ця риба буде " +
                    "зарахована нейтрально (0), щоб не псувати прогрес.");
            }
        }

        bool liked = !preferencesTimedOut && !string.IsNullOrEmpty(speciesId) && likedSpecies.Contains(speciesId);

        Debug.Log($"[SharkController] Риба з'їдена: speciesId='{speciesId}', likedSpecies=[{string.Join(", ", likedSpecies)}], liked={liked}, preferencesTimedOut={preferencesTimedOut}");

        Vector3 circleReturnPos = transform.position;
        Quaternion circleReturnRot = transform.rotation;
        float returnAngle = patrolAngle;

        Vector3 eatPos = transform.position;
        if (fish != null)
        {
            eatPos = fish.position;
            eatPos.y = patrolHeight;

            if (orbitCenter != null)
            {
                Vector3 fromCenter = eatPos - orbitCenter.position;
                fromCenter.y = 0f;
                float distFromCenter = fromCenter.magnitude;

                if (distFromCenter > 0.01f && distFromCenter < minEatDistanceFromIsland)
                {
                    Vector3 dirOut = fromCenter.normalized;
                    Vector3 pushedPos = orbitCenter.position + dirOut * minEatDistanceFromIsland;
                    eatPos.x = pushedPos.x;
                    eatPos.z = pushedPos.z;
                }
            }

            transform.position = eatPos;
        }

        Quaternion faceIslandRot = transform.rotation;
        if (orbitCenter != null)
        {
            Vector3 dirToIsland = orbitCenter.position - transform.position;
            dirToIsland.y = 0f;
            if (dirToIsland != Vector3.zero)
                faceIslandRot = Quaternion.LookRotation(dirToIsland.normalized)
                              * Quaternion.Euler(0f, visualForwardOffsetY + visualForwardOffsetYFacingIsland, 0f);
        }
        transform.rotation = faceIslandRot;

        forcedPos = eatPos;
        forcedRot = faceIslandRot;

        animator.SetTrigger(eatFishTriggerName);

        // Звук поїдання риби - на всіх клієнтах одночасно з анімацією
        PlaySoundForAll(SharkSound.EatFish);

        if (fish != null)
            Destroy(fish.gameObject);

        // Пауза перед реакцією - щоб анімація "Eat" встигла зіграти
        // окремо від наступної "Like"/"Dislike".
        yield return new WaitForSeconds(reactionDelay);

        // Захист від гонки зі сценою: якщо сцена перезавантажується (напр.
        // натиснули "New Game") саме поки ця корутина чекала - об'єкт/View
        // можуть бути вже в процесі знищення. Викликати RPC у такому стані
        // якраз і спричиняє Photon-помилку "missing MonoBehaviours".
        if (this == null || !gameObject.activeInHierarchy || photonView == null || !PhotonNetwork.InRoom)
            yield break;

        animator.SetTrigger(preferencesTimedOut ? dislikeTriggerName : (liked ? likeTriggerName : dislikeTriggerName));

        int delta = preferencesTimedOut ? 0 : (liked ? 1 : -1);
        photonView.RPC(nameof(RPC_AddProgress), RpcTarget.All, delta, liked);

        float remainingHold = Mathf.Max(0f, eatHoldDuration - reactionDelay);
        yield return new WaitForSeconds(remainingHold);

        if (this == null || !gameObject.activeInHierarchy)
            yield break;

        forcedPos = null;
        forcedRot = null;
        transform.position = circleReturnPos;
        transform.rotation = circleReturnRot;
        patrolAngle = returnAngle;

        isEating = false;
    }

    private IEnumerator BiteRoutine(System.Action onBiteImpact, float biteDuration)
    {
        isBiting = true;

        Vector3 bitePos = transform.position;
        Quaternion biteRot = transform.rotation;
        forcedPos = bitePos;
        forcedRot = biteRot;

        animator.SetTrigger(biteTriggerName);

        yield return new WaitForSeconds(biteDuration * biteImpactFraction);

        // Звук укусу - у момент влучання, разом із деформацією острова.
        // (Щоб грав на самому початку анімації - перенеси цей виклик
        // одразу після animator.SetTrigger(biteTriggerName) вище.)
        PlaySoundForAll(SharkSound.Bite);

        onBiteImpact?.Invoke();

        yield return new WaitForSeconds(biteDuration * (1f - biteImpactFraction));

        animator.SetTrigger(eatTriggerName);

        Vector3 latchPos = transform.position;
        Quaternion latchRot = transform.rotation;
        float eatElapsed = 0f;

        while (eatElapsed < eatHoldDuration)
        {
            eatElapsed += Time.deltaTime;

            float shakeX = Mathf.Sin(eatElapsed * eatShakeSpeed) * eatShakeAmplitude;
            float shakeY = Mathf.Sin(eatElapsed * eatShakeSpeed * 1.7f) * eatShakeAmplitude * 0.5f;
            forcedPos = latchPos + new Vector3(shakeX, shakeY, 0f);
            forcedRot = latchRot;

            yield return null;
        }

        forcedPos = null;
        forcedRot = null;
        transform.position = latchPos;
        transform.rotation = latchRot;

        isBiting = false;
    }
}