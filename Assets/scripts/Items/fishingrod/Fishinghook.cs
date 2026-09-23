using UnityEngine;

/// <summary>
/// Вішається на префаб гачка/грузила (той самий Rigidbody, який кидає FishingLineController).
/// Потрібен Collider (можна тригер) на об'єкті гачка.
/// Просто сповіщає підписників, коли гачок фізично торкнувся будь-якої з поверхонь,
/// вказаних у landableLayer (вода АБО суша - FishingLineController сам вирішує, яку саме
/// маску сюди підставити перед кидком).
///
/// ДОДАНО: якщо поверхня, на яку сів гачок, має компонент WaterZone - гачок
/// напряму викликає WaterZone.SpawnSplashAt(), програючи спленш-ефект точно в
/// момент фізичного дотику. Це навмисно НЕ покладається на автоматичну перевірку
/// швидкості падіння всередині WaterZone (яка орієнтована на звичайні Rigidbody-
/// об'єкти, що вільно падають), бо гачок під час польоту часто рухається
/// кінематично/по заданій траєкторії, і його rb.linearVelocity може не відображати
/// реальну швидкість - через що автоматичний спленш міг і не спрацювати.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FishingHook : MonoBehaviour
{
    [Tooltip("Шари поверхонь, дотик до яких вважається \"приземленням\" (зазвичай водаLayer | landLayer)")]
    public LayerMask landableLayer;

    /// <summary>Викликається один раз, коли гачок торкається будь-якої поверхні з landableLayer.</summary>
    public System.Action OnLanded;

    private bool alreadyLanded = false;
    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        // Гачок має спокійно "висіти" на вудці і НЕ падати під гравітацією,
        // поки FishingLineController.Cast() явно не увімкне на ньому фізику.
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryReportHit(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryReportHit(collision.gameObject);
    }

    private void TryReportHit(GameObject other)
    {
        if (alreadyLanded) return;

        if (((1 << other.layer) & landableLayer.value) == 0) return;

        alreadyLanded = true;

        // Якщо приземлились саме на воду (об'єкт з компонентом WaterZone) -
        // граємо спленш напряму через сам WaterZone, використовуючи вже
        // призначений у ньому splashPrefab. Не потрібно окремо тягнути
        // посилання на префаб частинок у сам FishingHook.
        WaterZone water = other.GetComponentInParent<WaterZone>();
        if (water != null)
        {
            water.SpawnSplashAt(transform.position);
        }

        OnLanded?.Invoke();
    }

    /// <summary>Скидає прапорець - треба викликати перед повторним закиданням того самого гачка.</summary>
    public void ResetHook()
    {
        alreadyLanded = false;
    }
}