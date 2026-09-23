using System.Collections;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Логіка риболовлі: гравець тримає вудку в руці (equipAnimTrigger == "EquipRod",
/// див. PlayerPickup.fishingRodTriggerName).
///
/// Флоу:
/// 1. ПКМ, поки вудка в руках і гравець вільний (Idle) - "закидає гачок":
///    Raycast від камери; якщо влучили у воду (waterLayer) АБО у сушу (landLayer) -
///    гачок фізично летить туди (FishingLineController.Cast), грається анімація
///    ThrowHook, і після того, як гачок РЕАЛЬНО приземлиться, починається наступний крок.
/// 2. ПОКИ гачок летить або лежить і чекає на клювання (Casting / WaitingForBite),
///    гравець будь-якої миті може клікнути ПКМ ЩЕ РАЗ, щоб СКАСУВАТИ закидання - леска
///    змотається назад без риби, і вудка одразу повернеться в Idle.
/// 3a. Якщо гачок приземлився У ВОДІ - через випадковий час (biteWaitMin..biteWaitMax)
///     спершу грається UI-анімація catchscreen (catchScreenAnimator, тригер
///     catchScreenTriggerName) і триває catchScreenAnimDelay секунд. Увесь цей час
///     стан ЛИШАЄТЬСЯ WaitingForBite, тому клік ПКМ у цей момент лише скасовує
///     закидання (як і в п.2), а НЕ підсікає рибу. Тільки ПІСЛЯ цієї затримки риба
///     фактично "клює" - відкривається вікно (strikeWindow, 3 сек), протягом якого
///     на вудці ПОВТОРЮЄТЬСЯ (лупиться) анімація клювання (риба смикається), поки
///     гравець не клікне ПКМ, щоб підсікти. Автоматично риба НЕ ловиться.
/// 3b. Якщо гачок приземлився НА СУШІ - риба ніколи не клює. Гачок просто лежить,
///     гравець може в будь-який момент клікнути ПКМ, щоб змотати його назад (Cancel).
/// 4. Якщо гравець встиг клікнути вчасно (тільки у воді) - грається анімація
///    витягування, леска підтягується (ReelIn), випадкова риба з fishPrefabs
///    потрапляє в інвентар, і вудка ПРИМУСОВО повертається в анімацію idle
///    (не покладаємось лише на Exit Time в аніматорі).
/// 5. Якщо гравець НЕ встиг клікнути протягом strikeWindow - риба зривається,
///    леска підтягується назад без риби, вудка повертається в Idle (теж примусово).
///
/// Повісити на того самого persona-об'єкта, де є PlayerPickup.
/// </summary>
public class FishingController : MonoBehaviour
{
    private enum FishingState
    {
        Idle,             // вудка вільна, можна закидати
        Casting,          // гачок фізично летить до цілі (можна скасувати кліком)
        WaitingForBite,   // гачок приземлився, чекаємо клювання (тільки у воді) або просто лежить (на суші) - можна скасувати кліком
        BiteAnimation,    // риба клюнула, грається UI-анімація (catchscreen) - клік тут НЕ скасовує закидання, а запам'ятовується як завчасна підсічка
        WaitingForStrike, // риба клюнула - клік підсікає рибу
        Reeling           // йде анімація витягування + змотування лески (клік ігнорується)
    }

    [Header("Посилання")]
    [Tooltip("Скрипт підбору/екіпірування - звідси береться, чи саме вудка зараз в руці")]
    public PlayerPickup playerPickup;

    [Tooltip("Куди складати впійману рибу")]
    public InventoryManager inventoryManager;

    [Tooltip("Камера гравця - звідси йде промінь закидання. Якщо не задано - Camera.main")]
    public Camera playerCamera;

    [Tooltip("Аніматор самої вудки (окремий Animator на об'єкті вудки/руки, що тримає її) - для анімацій закидання/клювання/витягування")]
    public Animator rodAnimator;

    [Tooltip("Назва стану idle в rodAnimator (Base Layer) - використовується, щоб примусово повертати вудку в спокій після підсічки/промаху/скасування")]
    public string idleStateName = "idle";

    [Tooltip("Контролер фізичної лески/гачка (LineRenderer + Verlet-мотузка). Якщо не задано - закидання відбувається без фізичної лески.")]
    public FishingLineController fishingLine;

    [Header("Закидання")]
    [Tooltip("Шар, на якому знаходиться вода (Plane з HeightmapIsland.seaLevel). Тільки тут може клюнути риба.")]
    public LayerMask waterLayer;
    [Tooltip("Шар суші/землі, куди теж можна закидати гачок, але риба там НІКОЛИ не клює.")]
    public LayerMask landLayer;
    [Tooltip("Максимальна дистанція закидання гачка")]
    public float castRange = 25f;
    [Tooltip("Скільки секунд летить гачок до точки влучання (для розрахунку траєкторії)")]
    public float castFlightTime = 0.8f;
    [Tooltip("Скільки максимум чекати фізичного приземлення гачка, перш ніж вважати закидання невдалим (страховка)")]
    public float castLandingTimeout = 3f;
    [Tooltip("Назва Trigger-параметра в rodAnimator, що грає в момент закидання гачка. Пусто = без анімації.")]
    public string castThrowTriggerName = "ThrowHook";

    [Header("UI-анімація клювання (catchscreen)")]
    [Tooltip("Аніматор UI-екрану клювання (catchscreen/anim), що грає ПЕРЕД тим, як риба фактично клюне")]
    public Animator catchScreenAnimator;
    [Tooltip("Назва СТАНУ (не тригера!) в catchScreenAnimator, який відповідає за саму анімацію клювання (напр. \"fishcatch\"). Запускається через Play(), тому спрацює навіть якщо в контролері немає зворотного переходу.")]
    public string catchScreenClipStateName = "fishcatch";
    [Tooltip("Назва стану idle в catchScreenAnimator (напр. \"New State\"), у який аніматор примусово повертається після затримки - щоб наступного разу анімацію можна було так само примусово перезапустити.")]
    public string catchScreenIdleStateName = "New State";
    [Tooltip("Скільки секунд чекати (граючи UI-анімацію), перш ніж риба фактично клюне і відкриється вікно підсічки")]
    public float catchScreenAnimDelay = 1f;

    [Header("Клювання (тільки якщо гачок у воді)")]
    [Tooltip("Мінімальний і максимальний час очікування клювання (секунди)")]
    public float biteWaitMin = 2f;
    public float biteWaitMax = 5f;
    [Tooltip("Назва Trigger-параметра в rodAnimator, що грає в момент клювання (риба смикається). Поки триває вікно підсічки - буде повторюватись кожні biteRepeatInterval секунд. Пусто = без анімації.")]
    public string biteTriggerName = "Bite";
    [Tooltip("Як часто (сек) повторно смикати bite-тригер, поки триває вікно підсічки - щоб анімація клювання \"лупилась\", а не грала один раз")]
    public float biteRepeatInterval = 0.4f;

    [Header("Підсічка (гравець мусить клікнути вчасно)")]
    [Tooltip("Скільки секунд є в гравця, щоб клікнути ПКМ і підсікти рибу після того, як вона клюнула. Не встиг - риба зірветься.")]
    public float strikeWindow = 3f;

    [Header("Витягування (грається одразу після вдалої підсічки)")]
    [Tooltip("Назва Trigger-параметра в rodAnimator, що грає під час витягування впійманої риби. Пусто = без анімації.")]
    public string reelInTriggerName = "CatchFish";
    [Tooltip("Тривалість анімації/змотування лески (сек) перед тим, як риба фактично потрапляє в інвентар.")]
    public float reelInAnimDuration = 0.8f;
    [Tooltip("За скільки секунд леска змотується назад, коли риба зірвалась або гравець сам скасував закидання")]
    public float reelInOnMissDuration = 0.5f;

    [Header("Риба (перетягни сюди 5 префабів Pickupable-риб). ВАЖЛИВО: усі префаби мають лежати в папці Resources - інакше PhotonNetwork.Instantiate їх не знайде.")]
    public Pickupable[] fishPrefabs;

    private FishingState state = FishingState.Idle;
    private Coroutine activeRoutine;     // поточна "фонова" корутина закидання/очікування (щоб можна було скасувати)
    private Coroutine biteLoopRoutine;   // корутина повторення bite-анімації
    private bool lastCastWasWater;       // чи ціль останнього закидання - вода (визначає, чи можлива поклівка)
    private bool wasHoldingRod;          // чи вудка була в руках у ПОПЕРЕДНЬОМУ кадрі (щоб зловити момент, коли її прибрали)
    private bool earlyStrikeRequested;   // гравець клікнув ПКМ ПІД ЧАС BiteAnimation - зарахувати улов одразу, як анімація дограє

    void Update()
    {
        bool holdingRodNow = playerPickup != null && playerPickup.IsHoldingFishingRod;

        if (rodAnimator != null)
            rodAnimator.SetBool("HasRod", holdingRodNow);

        if (wasHoldingRod && !holdingRodNow)
            ForceResetRod();

        wasHoldingRod = holdingRodNow;

        // Керувати вудкою можна лише поки вона в руках
        if (!holdingRodNow) return;

        if (!Input.GetMouseButtonDown(1)) return;

        switch (state)
        {
            case FishingState.Idle:
                TryCast();
                break;

            case FishingState.Casting:
            case FishingState.WaitingForBite:
                // Гравець сам передумав (або закинув на сушу, де риба не клює) - скасовуємо
                CancelFishing();
                break;

            case FishingState.BiteAnimation:
                // Риба вже клюнула, грається UI-анімація - гравець клікнув ВЧАСНО.
                // НЕ скасовуємо закидання і не ловимо рибу прямо зараз (анімація ще
                // не дограла) - лише запам'ятовуємо, щоб зарахувати улов одразу
                // після завершення анімації (див. AfterLandingRoutine).
                earlyStrikeRequested = true;
                break;

            case FishingState.WaitingForStrike:
                Strike();
                break;

            // Reeling - клік ігнорується, йде фінальна анімація
            default:
                break;
        }
    }

    private void TryCast()
    {
        Camera cam = playerCamera != null ? playerCamera : Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[FishingController] Не задано камеру - неможливо визначити напрямок закидання.");
            return;
        }

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);

        // Раєкаст одразу по воді ТА по суші - гачок можна закинути будь-куди в межах дистанції
        LayerMask castMask = waterLayer | landLayer;

        if (Physics.Raycast(ray, out RaycastHit hit, castRange, castMask))
        {
            lastCastWasWater = ((1 << hit.collider.gameObject.layer) & waterLayer.value) != 0;

            state = FishingState.Casting;

            if (rodAnimator != null && !string.IsNullOrEmpty(castThrowTriggerName))
                rodAnimator.SetTrigger(castThrowTriggerName);

            if (fishingLine != null)
            {
                fishingLine.Cast(hit.point, castFlightTime);
                activeRoutine = StartCoroutine(WaitForHookLandingRoutine());
            }
            else
            {
                // Немає лески - одразу вважаємо, що гачок "приземлився"
                activeRoutine = StartCoroutine(AfterLandingRoutine());
            }
        }
        else
        {
            Debug.Log("[FishingController] Гачок нікуди не влучив - спробуй прицілитись точніше.");
        }
    }

    /// <summary>Гравець сам скасовує закидання (або закинув на сушу, де ловити нема на що, або ще триває UI-анімація клювання).</summary>
    private void CancelFishing()
    {
        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
            activeRoutine = null;
        }
        if (biteLoopRoutine != null)
        {
            StopCoroutine(biteLoopRoutine);
            biteLoopRoutine = null;
        }

        Debug.Log("[FishingController] Закидання скасовано.");
        state = FishingState.Reeling;

        if (fishingLine != null)
            fishingLine.ReelIn(reelInOnMissDuration, () => ReturnToIdle());
        else
            ReturnToIdle();
    }

    private IEnumerator WaitForHookLandingRoutine()
    {
        bool landed = false;
        void OnLanded() => landed = true;

        fishingLine.OnHookLanded += OnLanded;

        float t = 0f;
        while (!landed && t < castLandingTimeout)
        {
            t += Time.deltaTime;
            yield return null;
        }

        fishingLine.OnHookLanded -= OnLanded;

        if (!landed)
        {
            Debug.LogWarning("[FishingController] Гачок так і не зафіксував приземлення вчасно - скасовую закидання.");
            fishingLine.CancelLine();
            ReturnToIdle();
            activeRoutine = null;
            yield break;
        }

        activeRoutine = StartCoroutine(AfterLandingRoutine());
    }

    /// <summary>
    /// Гачок приземлився. У воді - чекаємо biteWait, тоді грається UI-анімація
    /// catchscreen і ЛИШЕ ПІСЛЯ неї риба фактично "клює" (відкривається вікно
    /// підсічки). На суші - просто лежимо, чекаємо, поки гравець скасує.
    /// </summary>
    private IEnumerator AfterLandingRoutine()
    {
        state = FishingState.WaitingForBite;

        if (!lastCastWasWater)
        {
            Debug.Log("[FishingController] Гачок на суші - риба тут не клює. Клікни ще раз, щоб змотати.");
            activeRoutine = null;
            yield break;
        }

        float waitTime = Random.Range(biteWaitMin, biteWaitMax);
        yield return new WaitForSeconds(waitTime);

        // --- Крок 1: спершу граємо UI-анімацію, ДО того як риба фактично клюнула.
        // Стан переходить у BiteAnimation: клік ПКМ зараз НЕ скасовує закидання
        // (обробляється в Update -> earlyStrikeRequested = true), а лише
        // запам'ятовується, щоб одразу зарахувати улов, коли анімація дограє.
        //
        // Використовуємо Animator.Play(..., 0, 0f) замість SetTrigger - це ПРИМУСОВО
        // перезапускає клип з самого початку, навіть якщо аніматор досі "застряг"
        // у стані fishcatch з попереднього улову (тобто навіть без зворотного
        // переходу fishcatch -> idle у самому контролері). SetTrigger тут не
        // спрацював би вдруге, бо з поточного стану може не бути валідного переходу.
        Debug.Log("[FishingController] Йде анімація клювання...");

        state = FishingState.BiteAnimation;
        earlyStrikeRequested = false;

        if (catchScreenAnimator != null && !string.IsNullOrEmpty(catchScreenClipStateName))
            catchScreenAnimator.Play(catchScreenClipStateName, 0, 0f);

        yield return new WaitForSeconds(catchScreenAnimDelay);

        // Повертаємо UI-аніматор в idle-стан, щоб наступного разу Play() знову
        // "стартанув" клип з нуля (а не просто лишався в тому ж кадрі).
        if (catchScreenAnimator != null && !string.IsNullOrEmpty(catchScreenIdleStateName))
            catchScreenAnimator.Play(catchScreenIdleStateName, 0, 0f);

        // Якщо гравець уже клікнув під час анімації - зараховуємо улов одразу,
        // не чекаючи ще одного кліку у вікні підсічки.
        if (earlyStrikeRequested)
        {
            Debug.Log("[FishingController] Клікнув вчасно під час анімації - риба впіймана!");
            state = FishingState.Reeling;
            activeRoutine = null;
            StartCoroutine(ReelInRoutine());
            yield break;
        }

        // --- Крок 2: тепер риба реально клюнула - відкриваємо вікно підсічки.
        state = FishingState.WaitingForStrike;
        Debug.Log("[FishingController] Клює! Клікни ще раз (ПКМ), щоб підсікти!");

        activeRoutine = StartCoroutine(StrikeTimeoutRoutine());
        biteLoopRoutine = StartCoroutine(BiteAnimationLoopRoutine());
    }

    /// <summary>Повторює bite-анімацію на вудці кожні biteRepeatInterval секунд, поки триває вікно підсічки.</summary>
    private IEnumerator BiteAnimationLoopRoutine()
    {
        if (rodAnimator == null || string.IsNullOrEmpty(biteTriggerName))
            yield break;

        while (state == FishingState.WaitingForStrike)
        {
            rodAnimator.SetTrigger(biteTriggerName);
            yield return new WaitForSeconds(biteRepeatInterval);
        }
    }

    private IEnumerator StrikeTimeoutRoutine()
    {
        yield return new WaitForSeconds(strikeWindow);

        // Якщо гравець так і не клікнув - риба зривається
        if (state == FishingState.WaitingForStrike)
        {
            Debug.Log("[FishingController] Не встиг підсікти - риба зірвалась.");
            state = FishingState.Reeling;
            activeRoutine = null;

            if (fishingLine != null)
                fishingLine.ReelIn(reelInOnMissDuration, () => ReturnToIdle());
            else
                ReturnToIdle();
        }
    }

    private void Strike()
    {
        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
            activeRoutine = null;
        }
        if (biteLoopRoutine != null)
        {
            StopCoroutine(biteLoopRoutine);
            biteLoopRoutine = null;
        }

        state = FishingState.Reeling;
        StartCoroutine(ReelInRoutine());
    }

    private IEnumerator ReelInRoutine()
    {
        // Підсічка вдалась - грається анімація витягування.
        if (rodAnimator != null && !string.IsNullOrEmpty(reelInTriggerName))
            rodAnimator.SetTrigger(reelInTriggerName);

        if (fishingLine != null)
        {
            bool reeled = false;
            fishingLine.ReelIn(reelInAnimDuration, () => reeled = true);
            yield return new WaitUntil(() => reeled);
        }
        else
        {
            yield return new WaitForSeconds(reelInAnimDuration);
        }

        CatchRandomFish();

        // Примусово повертаємо вудку в idle, а не покладаємось лише на Exit Time в аніматорі.
        ReturnToIdle();
    }

    /// <summary>Переводить ігровий стан у Idle і примусово ставить rodAnimator у стан idle.</summary>
    private void ReturnToIdle()
    {
        state = FishingState.Idle;

        if (rodAnimator != null && !string.IsNullOrEmpty(idleStateName))
            rodAnimator.Play(idleStateName, 0, 0f);
    }

    /// <summary>
    /// Викликається, коли вудку прибрали з рук (кинули клавішею R, або переключили слот
    /// інвентаря на інший предмет/пусто), поки закидання чи підсічка ще тривали.
    /// На відміну від CancelFishing() - тут НЕ грається анімація змотування (дивитись
    /// на вудку вже нема кому, вона в кишені/на землі), тому ліска/гачок прибираються
    /// миттєво через CancelLine(), а всі активні корутини зупиняються одразу.
    /// rodAnimator.Play (а не SetTrigger) в ReturnToIdle() гарантовано перериває будь-яку
    /// поточну анімацію (напр. застряглий кадр ThrowHook чи Bite) і ставить дефолтну позу.
    /// </summary>
    private void ForceResetRod()
    {
        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
            activeRoutine = null;
        }
        if (biteLoopRoutine != null)
        {
            StopCoroutine(biteLoopRoutine);
            biteLoopRoutine = null;
        }

        if (fishingLine != null)
            fishingLine.CancelLine();

        earlyStrikeRequested = false;
        ReturnToIdle();
    }

    private void CatchRandomFish()
    {
        if (fishPrefabs == null || fishPrefabs.Length == 0)
        {
            Debug.LogWarning("[FishingController] Список fishPrefabs порожній - нема з чого ловити.");
            return;
        }

        if (inventoryManager == null)
        {
            Debug.LogWarning("[FishingController] InventoryManager не призначено.");
            return;
        }

        if (!inventoryManager.HasFreeSlot())
        {
            Debug.Log("[FishingController] Спіймали рибу, але інвентар повний - вона зірвалась.");
            return;
        }

        Pickupable prefab = fishPrefabs[Random.Range(0, fishPrefabs.Length)];

        if (!PhotonNetwork.InRoom)
        {
            Debug.LogWarning("[FishingController] Немає з'єднання з кімнатою - рибу не вдалось заспавнити (перевірте Run In Background / мережу).");
            return;
        }

        // PhotonNetwork.Instantiate замість звичайного Instantiate - інакше об'єкт
        // не отримує справжній PhotonView.ViewID (лишається 0), і будь-який подальший
        // RPC на ньому (напр. RPC_Drop при викиданні з інвентаря) мовчки провалюється
        // з помилкою "Illegal view ID:0", через що риба "зникає" назавжди.
        // Гравець, що зловив рибу, автоматично стає Owner цього об'єкта.
        GameObject fishGO = PhotonNetwork.Instantiate(prefab.name, transform.position, transform.rotation);
        if (fishGO == null)
        {
            Debug.LogWarning("[FishingController] PhotonNetwork.Instantiate не вдався - рибу не спіймано.");
            return;
        }

        Pickupable fishInstance = fishGO.GetComponent<Pickupable>();

        // Одразу ховаємо як "предмет у кишені" і кладемо в перший вільний слот -
        // так само, як звичайний підбір через PlayerPickup.
        fishInstance.Store();
        inventoryManager.AddItem(fishInstance);

        Debug.Log($"[FishingController] Спіймано рибу: {prefab.name}");
    }
}