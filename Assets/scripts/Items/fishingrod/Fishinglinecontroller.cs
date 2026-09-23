using System.Collections;
using UnityEngine;

/// <summary>
/// Малює й симулює фізичну леску (Verlet-мотузка на LineRenderer) між кінчиком вудки
/// та гачком, який кидається справжньою параболічною траєкторією і має Rigidbody.
///
/// Гачок можна закинути як у воду, так і на сушу (FishingController сам вирішує, куди
/// саме дозволено цілитись, передаючи сюди готову цільову точку) - ця частина коду
/// просто фізично доносить гачок до вказаної точки і повідомляє, коли він там опинився.
///
/// Використання (з FishingController):
///   fishingLine.Cast(targetPoint, castTime);
///   fishingLine.OnHookLanded += () => { /* гачок фізично долетів і торкнувся поверхні */ };
///   fishingLine.ReelIn(duration, () => { /* мотузка змотана */ });
///
/// Повісити на той самий об'єкт, де є LineRenderer (або він додасться автоматично),
/// бажано на об'єкт вудки/руки.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class FishingLineController : MonoBehaviour
{
    [Header("Прив'язки")]
    [Tooltip("Точка на вудці, звідки виходить леска (останнє кільце на кінчику вудки)")]
    public Transform rodTip;

    [Tooltip("Префаб гачка/грузила: Rigidbody + Collider (можна тригер) + скрипт FishingHook")]
    public FishingHook hookPrefab;

    [Tooltip("Шар води - потрібен тут окремо, щоб гачок міг \"приземлитись\" саме на воду")]
    public LayerMask waterLayer;

    [Tooltip("Шар суші/землі - гачок теж може \"приземлитись\" сюди (просто без риби, це вирішує FishingController)")]
    public LayerMask landLayer;

    [Tooltip("Шар справжнього дна під водою (напр. окремий невидимий Plane \"under\"), на якому гачок фізично зупиняється, ПРОВалившись крізь воду. Додається лише до landableLayer гачка (щоб OnLanded спрацював), але НЕ впливає на прицільний raycast у FishingController - гравець не зможе навмисно цілитись прямо в дно.")]
    public LayerMask seaFloorLayer;

    [Header("Матеріал лески")]
    [Tooltip("Матеріал для LineRenderer. Якщо не призначити - буде згенеровано найпростіший однотонний матеріал у Play Mode, щоб леска НЕ була фіолетовою (типовий колір Unity для відсутнього/несумісного шейдера).")]
    public Material lineMaterial;
    public Color fallbackLineColor = new Color(0.15f, 0.15f, 0.15f);

    [Header("Леска (Verlet-мотузка)")]
    [Tooltip("Кількість сегментів мотузки - більше = плавніший провис, але дорожче")]
    [Range(4, 40)] public int segmentCount = 16;
    public float lineWidth = 0.008f;
    [Tooltip("Кількість ітерацій розв'язання обмежень довжини за кадр - більше = менш еластична, точніша мотузка")]
    [Range(1, 20)] public int constraintIterations = 10;
    [Tooltip("Множник довжини лески відносно прямої відстані вудка-гачок. 1 = леска завжди туго натягнута точно по прямій (як зараз). Більше 1 = леска ФІЗИЧНО ДОВША за пряму відстань - буде більше провисати (сильніший, реалістичніший \"провіс\" замість натягнутої струни).")]
    [Range(1f, 3f)] public float lineSlack = 1.15f;
    [Tooltip("Множник гравітації для провисання лески (не плутати з гравітацією гачка)")]
    public float lineGravity = 1f;
    [Range(0f, 0.5f)] public float lineDrag = 0.05f;

    /// <summary>Викликається один раз, коли гачок торкається води АБО суші (залежно від того, куди закинули).</summary>
    public event System.Action OnHookLanded;

    private LineRenderer lineRenderer;
    private FishingHook currentHook;

    private Vector3[] points;
    private Vector3[] prevPoints;
    private float segmentLength;

    private bool isFlying;
    private bool hookLanded;

    public bool HookLanded => hookLanded;
    public bool IsFlying => isFlying;

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = segmentCount;
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.enabled = false;

        EnsureLineMaterial();
    }

    /// <summary>
    /// Якщо матеріал не призначений в інспекторі (або несумісний з поточним render pipeline),
    /// підставляє простий однотонний матеріал, щоб леска не малювалась фіолетовим error-шейдером.
    /// </summary>
    private void EnsureLineMaterial()
    {
        if (lineMaterial != null)
        {
            lineRenderer.material = lineMaterial;
            return;
        }

        if (lineRenderer.sharedMaterial != null && lineRenderer.sharedMaterial.shader != null
            && lineRenderer.sharedMaterial.shader.name != "Hidden/InternalErrorShader")
        {
            // В інспекторі вже стоїть робочий матеріал - нічого не чіпаємо.
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("HDRP/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        if (shader == null)
        {
            Debug.LogWarning("[FishingLineController] Не вдалось знайти жоден сумісний шейдер для лески - признач lineMaterial вручну в інспекторі.");
            return;
        }

        Material generated = new Material(shader);
        if (generated.HasProperty("_Color"))
            generated.SetColor("_Color", fallbackLineColor);
        if (generated.HasProperty("_BaseColor"))
            generated.SetColor("_BaseColor", fallbackLineColor);

        lineRenderer.material = generated;
    }

    /// <summary>
    /// Кидає гачок з rodTip у targetPoint по параболі. castTime - скільки секунд має тривати політ.
    /// targetPoint може бути як у воді, так і на суші - фізика однакова в обох випадках.
    /// </summary>
    public void Cast(Vector3 targetPoint, float castTime)
    {
        if (rodTip == null || hookPrefab == null)
        {
            Debug.LogWarning("[FishingLineController] Не задано rodTip або hookPrefab.");
            return;
        }

        if (currentHook != null)
            Destroy(currentHook.gameObject);

        currentHook = Instantiate(hookPrefab, rodTip.position, Quaternion.identity);
        currentHook.landableLayer = waterLayer | landLayer | seaFloorLayer;
        currentHook.ResetHook();
        currentHook.OnLanded += HandleHookLanded;

        Rigidbody rb = currentHook.GetComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.linearVelocity = CalculateLaunchVelocity(rodTip.position, targetPoint, castTime);

        hookLanded = false;
        isFlying = true;
        lineRenderer.enabled = true;

        InitRope(rodTip.position, currentHook.transform.position);
    }

    /// <summary>Параболічна швидкість кидка так, щоб гачок долетів РІВНО до end (без "навісу", що заносить точку під землю).</summary>
    private Vector3 CalculateLaunchVelocity(Vector3 start, Vector3 end, float castTime)
    {
        float gravity = Mathf.Abs(Physics.gravity.y);
        castTime = Mathf.Max(0.05f, castTime);

        Vector3 displacement = end - start;
        Vector3 velocityXZ = new Vector3(displacement.x, 0f, displacement.z) / castTime;

        // Вертикальна швидкість, щоб гачок опинився рівно в end.y через castTime секунд
        // з урахуванням падіння під гравітацією за цей час (без штучного "height", який
        // раніше міг закидати ціль нижче реальної точки влучання).
        float velocityY = displacement.y / castTime + gravity * castTime * 0.5f;

        return velocityXZ + Vector3.up * velocityY;
    }

    private void HandleHookLanded()
    {
        if (hookLanded) return;
        hookLanded = true;
        isFlying = false;

        if (currentHook != null)
        {
            Rigidbody rb = currentHook.GetComponent<Rigidbody>();
            rb.linearVelocity = Vector3.zero;
            rb.useGravity = false;
            rb.isKinematic = true; // гачок спокійно лежить на місці попадання (вода чи суша)
        }

        OnHookLanded?.Invoke();
    }

    private void InitRope(Vector3 start, Vector3 end)
    {
        points = new Vector3[segmentCount];
        prevPoints = new Vector3[segmentCount];

        for (int i = 0; i < segmentCount; i++)
        {
            float t = i / (float)(segmentCount - 1);
            points[i] = Vector3.Lerp(start, end, t);
            prevPoints[i] = points[i];
        }

        segmentLength = Vector3.Distance(start, end) * lineSlack / (segmentCount - 1);
    }

    private void FixedUpdate()
    {
        if (!lineRenderer.enabled || currentHook == null) return;

        SimulateRope();
        DrawRope();
    }

    private void SimulateRope()
    {
        Vector3 hookPos = currentHook.transform.position;

        // Поки гачок летить/тягнеться - довжина сегментів підлаштовується під поточну відстань,
        // помножену на lineSlack, щоб мотузка не розтягувалась нескінченно і не "телепортувала"
        // точки, але при цьому фізично була ДОВШОЮ за пряму (lineSlack > 1), а не туго натягнутою
        // струною - завдяки цьому вона більше провисає під lineGravity.
        float currentDist = Vector3.Distance(rodTip.position, hookPos);
        segmentLength = (currentDist * lineSlack) / (segmentCount - 1);

        float dt2 = Time.fixedDeltaTime * Time.fixedDeltaTime;

        for (int i = 1; i < points.Length; i++)
        {
            Vector3 velocity = (points[i] - prevPoints[i]) * (1f - lineDrag);
            prevPoints[i] = points[i];
            points[i] += velocity;
            points[i] += Vector3.down * lineGravity * dt2;
        }

        for (int iter = 0; iter < constraintIterations; iter++)
        {
            points[0] = rodTip.position;
            points[points.Length - 1] = hookPos;

            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector3 delta = points[i + 1] - points[i];
                float dist = delta.magnitude;
                if (dist < 0.0001f) continue;

                float error = dist - segmentLength;
                Vector3 correction = delta.normalized * error * 0.5f;

                if (i != 0) points[i] += correction;
                if (i + 1 != points.Length - 1) points[i + 1] -= correction;
            }
        }
    }

    private void DrawRope()
    {
        lineRenderer.SetPositions(points);
    }

    /// <summary>
    /// Плавно підтягує гачок назад до кінчика вудки за duration секунд, потім прибирає гачок і ховає леску.
    /// </summary>
    public void ReelIn(float duration, System.Action onComplete = null)
    {
        StartCoroutine(ReelInRoutine(duration, onComplete));
    }

    private IEnumerator ReelInRoutine(float duration, System.Action onComplete)
    {
        if (currentHook == null)
        {
            lineRenderer.enabled = false;
            onComplete?.Invoke();
            yield break;
        }

        isFlying = false;
        hookLanded = false;

        Rigidbody rb = currentHook.GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        Vector3 start = currentHook.transform.position;
        float t = 0f;
        duration = Mathf.Max(0.05f, duration);

        while (t < duration)
        {
            t += Time.deltaTime;
            currentHook.transform.position = Vector3.Lerp(start, rodTip.position, t / duration);
            yield return null;
        }

        lineRenderer.enabled = false;

        if (currentHook != null)
        {
            Destroy(currentHook.gameObject);
            currentHook = null;
        }

        onComplete?.Invoke();
    }

    /// <summary>Миттєво прибирає гачок і ховає леску (напр. якщо гравець зняв вудку).</summary>
    public void CancelLine()
    {
        StopAllCoroutines();
        lineRenderer.enabled = false;
        isFlying = false;
        hookLanded = false;

        if (currentHook != null)
        {
            Destroy(currentHook.gameObject);
            currentHook = null;
        }
    }
}