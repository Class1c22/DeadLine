using UnityEngine;

// Повісити на об'єкт з Box/Mesh Collider (Is Trigger = true), що позначає
// об'єм води. Сам WaterZone більше НЕ вирішує, коли гравець "під водою" -
// він лише повідомляє PlayerBreath, що тіло гравця перебуває в об'ємі води,
// і передає висоту поверхні (верх колайдера). Остаточне рішення "камера під
// водою чи ні" приймає сам PlayerBreath, звіряючи Y камери з цією висотою -
// так бар кисню з'являється/зникає саме коли КАМЕРА перетинає поверхню,
// а не коли тіло торкнулось тригера.
//
// ДОДАНО: спленш-ефект частинками для БУДЬ-ЯКОГО об'єкта з Rigidbody
// (гравець, риба, предмети з Pickupable тощо), який падає в воду.
public class WaterZone : MonoBehaviour
{
    private Collider waterCollider;

    [Header("Спленш-ефект")]
    [Tooltip("Префаб ParticleSystem, що програється один раз у точці входу в воду")]
    [SerializeField] private ParticleSystem splashPrefab;

    [Tooltip("Мінімальна швидкість падіння вниз (м/с), щоб з'явився спленш. " +
             "Захищає від спрацювання, коли об'єкт просто повільно спливає/тоне.")]
    [SerializeField] private float minFallSpeedForSplash = 1.5f;

    [Tooltip("Кулдаун між спленшами для одного й того ж об'єкта (сек), щоб не спамило частинками")]
    [SerializeField] private float splashCooldown = 0.5f;

    void Awake()
    {
        waterCollider = GetComponent<Collider>();
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[WaterZone] OnTriggerEnter: {other.name}, tag = {other.tag}");

        TrySpawnSplash(other);

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

        float surfaceY = waterCollider != null ? waterCollider.bounds.max.y : transform.position.y;

        Debug.Log($"[WaterZone] {other.name} у зоні води. Поверхня на Y = {surfaceY}");
        breath.SetInWaterVolume(true, surfaceY);
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
    // СПЛЕНШ-ЕФЕКТ
    //
    // Спрацьовує для БУДЬ-ЯКОГО об'єкта з Rigidbody (не тільки гравця),
    // якщо той падає вниз досить швидко. Ефект суто косметичний і
    // програється ЛОКАЛЬНО на кожному клієнті (Instantiate, не
    // PhotonNetwork.Instantiate) - це нормальна практика для частинок:
    // мережевий трафік на них не витрачається, а фізика синхронізованого
    // об'єкта у Photon все одно виконується на кожному клієнті приблизно
    // однаково, тож спленш з'явиться в потрібний момент і у гравця, і
    // у тих, хто на нього дивиться.
    // ---------------------------------------------------------------
    private float lastSplashTime = -999f;

    private void TrySpawnSplash(Collider other)
    {
        if (splashPrefab == null) return;

        Rigidbody rb = other.attachedRigidbody;

        // Якщо є Rigidbody - перевіряємо, що об'єкт саме падає вниз досить швидко
        // (захист від спленшу при повільному спливанні/зависанні у воді).
        // Якщо Rigidbody немає (напр. буй, що рухається кінематично/скриптом,
        // без фізики) - пропускаємо цю перевірку і рахуємо вхід у тригер сам по собі.
        if (rb != null && rb.linearVelocity.y > -minFallSpeedForSplash) return;

        if (Time.time - lastSplashTime < splashCooldown) return;
        lastSplashTime = Time.time;

        float surfaceY = waterCollider != null ? waterCollider.bounds.max.y : transform.position.y;

        // Точка спленшу: XZ - де об'єкт увійшов у воду, Y - рівно на поверхні
        Vector3 pos = other.transform.position;
        Vector3 splashPos = new Vector3(pos.x, surfaceY, pos.z);

        ParticleSystem fx = Instantiate(splashPrefab, splashPos, Quaternion.identity);
        Destroy(fx.gameObject, 0.5f);

        Debug.Log($"[WaterZone] Спленш для {other.name} у точці {splashPos}");
    }
}