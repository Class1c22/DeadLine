using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal; // якщо URP; для HDRP - UnityEngine.Rendering.HighDefinition

// Дихання/кисень - суто локальний стан свого гравця (як і рух чи камера).
// Іншим клієнтам не потрібно знати про чужий рівень кисню чи бачити чужий
// UI-бар, тому скрипт вимикається на чужих копіях так само, як
// PlayerController і FirstPersonCamera.
//
// UI кисню зроблено як ДВА Image (Type = Filled, Method = Horizontal),
// що спадають одночасно з обох боків до центру:
//  - oxygenImageLeft:  Fill Origin = Right (ховається зліва)
//  - oxygenImageRight: Fill Origin = Left  (ховається справа)
// Обидва отримують однаковий fillAmount = currentOxygen / maxOxygen.
//
// ВАЖЛИВО: WaterZone більше НЕ вирішує "гравець під водою чи ні" - він лише
// повідомляє через SetInWaterVolume(), що ТІЛО гравця перебуває в об'ємі
// води, і передає висоту поверхні. Тут, у Update(), ми звіряємо позицію
// КАМЕРИ з цією висотою - бар кисню (і витрата кисню) вмикається тільки
// коли камера реально опустилась нижче поверхні, а не коли тіло торкнулось
// тригера.
[RequireComponent(typeof(PhotonView))]
public class PlayerBreath : MonoBehaviourPun
{
    [Header("UI")]
    [Tooltip("Ліва половина бару. Image Type = Filled, Fill Method = Horizontal, Fill Origin = Right")]
    public Image oxygenImageLeft;
    [Tooltip("Права половина бару. Image Type = Filled, Fill Method = Horizontal, Fill Origin = Left")]
    public Image oxygenImageRight;
    [Tooltip("Батьківський об'єкт бару (весь Canvas-елемент, що містить обидві половини), який треба ховати/показувати цілком")]
    public GameObject oxygenBarRoot;

    [Header("Параметри кисню")]
    public float maxOxygen = 100f;
    public float depleteRate = 10f;   // одиниць/сек під водою
    public float refillRate = 25f;    // одиниць/сек на поверхні

    [Header("Vignette (ефект нестачі кисню)")]
    [Tooltip("Global Volume зі сцени, у профілі якого є override Vignette і Film Grain. МОЖНА ЛИШИТИ ПОРОЖНІМ - буде знайдено автоматично на старті через FindObjectOfType, бо Global Volume - об'єкт СЦЕНИ і посилання на нього неможливо надійно зберегти на префабі гравця (mainhero спавниться через PhotonNetwork.Instantiate з Resources).")]
    public Volume postProcessVolume;
    [Tooltip("Intensity віньєтки, коли кисню повно")]
    public float vignetteMinIntensity = 0.2f;
    [Tooltip("Intensity віньєтки, коли кисень на нулі")]
    public float vignetteMaxIntensity = 0.491f;
    private Vignette vignette;
    private bool hasVignette;

    [Header("Film Grain (ефект нестачі кисню)")]
    [Tooltip("Intensity зерна, коли кисню повно")]
    public float filmGrainMinIntensity = 0.158f;
    [Tooltip("Intensity зерна, коли кисень на нулі")]
    public float filmGrainMaxIntensity = 0.6f;
    private FilmGrain filmGrain;
    private bool hasFilmGrain;

    [Header("Посилання")]
    public PlayerController playerController; // щоб повідомляти про underwater-гравітацію
    [Tooltip("Камера гравця, за позицією якої визначається занурення. Якщо не задано - береться з playerController.cameraTransform")]
    public Transform cameraTransform;

    [Tooltip("Обробник смерті гравця (той самий, що й для акули). Якщо не задано - береться GetComponent на цьому ж об'єкті.")]
    public PlayerDeathHandler deathHandler;

    [Tooltip("Затримка (сек) перед показом Game Over після смерті від нестачі кисню - дає час, наприклад, програти анімацію \"захлинання\", якщо вона є")]
    public float drownGameOverDelay = 1.5f;

    private float currentOxygen;
    private bool isUnderwater;      // камера реально нижче поверхні - витрачається кисень, показаний бар
    private bool isInWaterVolume;   // тіло в тригері води (може бути true, коли камера ще над поверхнею)
    private float waterSurfaceY;
    private bool hasDied;           // щоб Die() не викликався щокадру, поки currentOxygen лишається на нулі

    void Start()
    {
        // Як і в інших локальних скриптах - чужим копіям ця логіка не потрібна.
        if (!photonView.IsMine)
        {
            enabled = false;
            if (oxygenBarRoot != null) oxygenBarRoot.SetActive(false);
            return;
        }

        if (playerController == null)
            playerController = GetComponent<PlayerController>();

        if (cameraTransform == null && playerController != null)
            cameraTransform = playerController.cameraTransform;

        if (deathHandler == null)
            deathHandler = GetComponent<PlayerDeathHandler>();

        // Global Volume - об'єкт СЦЕНИ, а не дитина префабу mainhero, тому
        // Inspector-посилання на нього завжди обнулиться після
        // PhotonNetwork.Instantiate. Шукаємо його прямо в ієрархії сцени -
        // так само, як SharkBiteController шукає акулу/острів.
        if (postProcessVolume == null)
            postProcessVolume = FindGlobalVolumeInScene();

        if (postProcessVolume != null && postProcessVolume.profile != null)
        {
            hasVignette = postProcessVolume.profile.TryGet(out vignette);
            hasFilmGrain = postProcessVolume.profile.TryGet(out filmGrain);
        }
        else
        {
            Debug.LogWarning("[PlayerBreath] Не знайдено Global Volume на сцені - ефекти нестачі кисню (Vignette/Film Grain) не працюватимуть.");
        }

        currentOxygen = maxOxygen;

        UpdateBarVisual();
        UpdateOxygenEffects();

        if (oxygenBarRoot != null)
            oxygenBarRoot.SetActive(false);
    }

    /// <summary>
    /// Шукає Global Volume прямо в сцені. Спочатку - той, де isGlobal == true
    /// (типовий кейс: один загальний Volume на всю сцену); якщо такого немає -
    /// бере перший-ліпший Volume зі сцени.
    /// </summary>
    private Volume FindGlobalVolumeInScene()
    {
        Volume[] allVolumes = FindObjectsOfType<Volume>(true);

        foreach (var v in allVolumes)
            if (v.isGlobal) return v;

        return allVolumes.Length > 0 ? allVolumes[0] : null;
    }

    void Update()
    {
        UpdateSubmergedState();

        if (isUnderwater)
        {
            currentOxygen -= depleteRate * Time.deltaTime;
            currentOxygen = Mathf.Max(currentOxygen, 0f);

            UpdateBarVisual();
            UpdateOxygenEffects();

            if (currentOxygen <= 0f && !hasDied)
            {
                hasDied = true;
                Die();
            }
        }
        else if (currentOxygen < maxOxygen)
        {
            currentOxygen += refillRate * Time.deltaTime;
            currentOxygen = Mathf.Min(currentOxygen, maxOxygen);

            UpdateBarVisual();
            UpdateOxygenEffects();

            if (currentOxygen >= maxOxygen && oxygenBarRoot != null)
                oxygenBarRoot.SetActive(false);
        }
    }

    private void UpdateSubmergedState()
    {
        bool shouldBeUnderwater = isInWaterVolume
            && cameraTransform != null
            && cameraTransform.position.y < waterSurfaceY;

        if (shouldBeUnderwater && !isUnderwater)
            EnterWater();
        else if (!shouldBeUnderwater && isUnderwater)
            ExitWater();
    }

    private void UpdateBarVisual()
    {
        float fill = currentOxygen / maxOxygen;

        if (oxygenImageLeft != null)
            oxygenImageLeft.fillAmount = fill;

        if (oxygenImageRight != null)
            oxygenImageRight.fillAmount = fill;
    }

    private void UpdateOxygenEffects()
    {
        float ratio = currentOxygen / maxOxygen;

        if (hasVignette && vignette != null)
        {
            float vIntensity = Mathf.Lerp(vignetteMaxIntensity, vignetteMinIntensity, ratio);
            vignette.intensity.Override(vIntensity);
        }

        if (hasFilmGrain && filmGrain != null)
        {
            float gIntensity = Mathf.Lerp(filmGrainMaxIntensity, filmGrainMinIntensity, ratio);
            filmGrain.intensity.Override(gIntensity);
        }
    }

    public void SetInWaterVolume(bool inVolume, float surfaceY)
    {
        isInWaterVolume = inVolume;
        waterSurfaceY = surfaceY;

        if (!inVolume && isUnderwater)
            ExitWater();
    }

    private void EnterWater()
    {
        isUnderwater = true;

        if (oxygenBarRoot != null) oxygenBarRoot.SetActive(true);
        if (playerController != null) playerController.SetUnderwater(true);
    }

    private void ExitWater()
    {
        isUnderwater = false;

        if (playerController != null) playerController.SetUnderwater(false);
    }

    private void Die()
    {

        if (deathHandler == null)
        {
            Debug.LogWarning("[PlayerBreath] Не задано PlayerDeathHandler - смерть від нестачі кисню не оброблена.");
            return;
        }

        deathHandler.Die();
        deathHandler.ShowGameOverDelayed(drownGameOverDelay);
    }
}