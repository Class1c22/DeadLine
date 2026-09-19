using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Розставляє пальми по поверхні острова один раз на старті гри.
/// Чекає подію HeightmapIsland.OnIslandGenerated (щоб не спрацювати
/// до того, як меш і колайдер острова реально готові), потім рейкастить
/// зверху вниз у випадкових точках кола і перевіряє кожну точку на:
/// - висоту (вище рівня моря),
/// - крутизну схилу (не саджаємо пальму на стрімкому боці гори),
/// - відступ від краю острова (акула кусає край першою - пальми біля
///   самого берега надто швидко зникнуть),
/// - мінімальну відстань до вже поставлених пальм (щоб не тулились купою).
///
/// Повісити на той самий об'єкт, де HeightmapIsland, або окремий порожній GameObject.
/// </summary>
public class PalmSpawner : MonoBehaviour
{
    [Header("Посилання")]
    public HeightmapIsland island;
    [Tooltip("Шар острова (MeshCollider) - для рейкасту зверху вниз")]
    public LayerMask islandLayer;

    [Header("Префаби пальм")]
    [Tooltip("Один або кілька варіантів моделі пальми - для візуального різноманіття")]
    public GameObject[] palmPrefabs;

    [Header("Кількість і розкидання")]
    public int palmCount = 12;
    [Tooltip("Скільки разів пробувати знайти валідну точку для ОДНІЄЇ пальми, перш ніж пропустити її")]
    public int maxAttemptsPerPalm = 30;
    [Tooltip("Мінімальна відстань між сусідніми пальмами")]
    public float minSpacing = 3f;

    [Header("Обмеження позиції")]
    [Tooltip("Не саджати пальми ближче ніж це до самого краю острова (акула кусає край першою)")]
    public float edgeMargin = 4f;
    [Tooltip("Не саджати пальми надто близько до вершини/центру гори (необов'язково, 0 = без обмеження)")]
    public float centerMargin = 0f;
    [Tooltip("Максимальний кут нахилу поверхні (градуси), на якому ще можна поставити пальму")]
    public float maxSlopeAngle = 25f;

    [Header("Висота розміщення")]
    [Tooltip("Зсув пальми по світовій осі Y відносно точки на поверхні. Мінус = пальма сидить глибше в землі (напр. -0.3). Плюс = вище.")]
    public float verticalOffset = -0.3f;
    [Tooltip("Саджати пальми лише не вище ніж стільки метрів над рівнем моря (щоб росли ближче до берега, а не на горі). 0 = без обмеження.")]
    public float maxHeightAboveSea = 0f;

    [Header("Варіативність вигляду")]
    public Vector2 scaleRange = new Vector2(0.9f, 1.15f);
    [Tooltip("Чи нахиляти пальму під кут поверхні (природніше на схилах) чи завжди ставити рівно вгору")]
    public bool alignToSurfaceNormal = true;

    [Header("Перевірка на затоплення")]
    [Tooltip("Як часто (сек) перевіряти, чи не опинилась якась пальма нижче рівня моря (напр. рельєф під нею плавно просів після укусу поруч). 0 = перевіряти щокадру.")]
    public float floodCheckInterval = 0.5f;

    private float floodCheckTimer;
    private readonly List<Vector3> spawnedPositions = new List<Vector3>();
    private readonly List<GameObject> spawnedPalms = new List<GameObject>();

    /// <summary>Готові позиції всіх успішно поставлених пальм - знадобиться майбутній логіці кокосів.</summary>
    public IReadOnlyList<GameObject> SpawnedPalms => spawnedPalms;

    void Awake()
    {
        if (island == null)
            island = GetComponent<HeightmapIsland>();

        if (island != null)
        {
            island.OnIslandGenerated += SpawnPalms;
            island.OnBite += HandleBite;
        }
        else
        {
            Debug.LogWarning("[PalmSpawner] Не задано HeightmapIsland - нема з чиєю поверхнею працювати.");
        }
    }

    void Update()
    {
        if (spawnedPalms.Count == 0 || island == null) return;

        floodCheckTimer += Time.deltaTime;
        if (floodCheckTimer < floodCheckInterval) return;

        floodCheckTimer = 0f;
        RemoveFloodedPalms();
    }

    /// <summary>
    /// Прибирає будь-яку пальму, чия позиція вже опинилась нижче рівня моря.
    /// Потрібно окремо від HandleBite, бо ямка після укусу провалюється ПОСТУПОВО
    /// (лерп протягом biteDuration) - пальма могла стояти на самій межі радіуса
    /// укусу і не потрапити під HandleBite, але рельєф під нею все одно осяде
    /// нижче seaLevel трохи пізніше.
    /// </summary>
    private void RemoveFloodedPalms()
    {
        float worldSeaLevel = island.transform.position.y + island.seaLevel;

        for (int i = spawnedPalms.Count - 1; i >= 0; i--)
        {
            GameObject palm = spawnedPalms[i];
            if (palm == null)
            {
                spawnedPalms.RemoveAt(i);
                spawnedPositions.RemoveAt(i);
                continue;
            }

            // Порівнюємо висоту ПОВЕРХНІ під пальмою (без verticalOffset), інакше
            // занурена в землю пальма біля берега вважалась би затопленою і зникала.
            float surfaceY = palm.transform.position.y - verticalOffset;
            if (surfaceY < worldSeaLevel)
            {
                Destroy(palm);
                spawnedPalms.RemoveAt(i);
                spawnedPositions.RemoveAt(i);
            }
        }
    }

    void OnDestroy()
    {
        if (island != null)
        {
            island.OnIslandGenerated -= SpawnPalms;
            island.OnBite -= HandleBite;
        }
    }

    /// <summary>
    /// Викликається HeightmapIsland.OnBite в момент укусу. Прибирає (знищує) будь-яку
    /// пальму, чия позиція в площині XZ потрапила в радіус укусу — так пальма
    /// "провалюється" разом зі шматком острова, а не лишається висіти в повітрі
    /// над ямою чи під водою.
    /// </summary>
    private void HandleBite(Vector3 worldBitePos, float biteRadius)
    {
        for (int i = spawnedPalms.Count - 1; i >= 0; i--)
        {
            GameObject palm = spawnedPalms[i];
            if (palm == null)
            {
                spawnedPalms.RemoveAt(i);
                spawnedPositions.RemoveAt(i);
                continue;
            }

            float distXZ = Vector2.Distance(
                new Vector2(palm.transform.position.x, palm.transform.position.z),
                new Vector2(worldBitePos.x, worldBitePos.z));

            if (distXZ <= biteRadius)
            {
                Destroy(palm);
                spawnedPalms.RemoveAt(i);
                spawnedPositions.RemoveAt(i);
            }
        }
    }

    private void SpawnPalms()
    {
        if (palmPrefabs == null || palmPrefabs.Length == 0)
        {
            Debug.LogWarning("[PalmSpawner] Не задано жодного префаба пальми.");
            return;
        }

        float radius = island.EffectiveRadius;
        float usableRadius = Mathf.Max(0f, radius - edgeMargin);

        int placed = 0;
        for (int i = 0; i < palmCount; i++)
        {
            if (TryFindValidPoint(usableRadius, out Vector3 pos, out Vector3 normal))
            {
                SpawnOnePalm(pos, normal);
                spawnedPositions.Add(pos);
                placed++;
            }
        }

        Debug.Log($"[PalmSpawner] Розставлено {placed}/{palmCount} пальм.");
    }

    private bool TryFindValidPoint(float usableRadius, out Vector3 point, out Vector3 normal)
    {
        point = Vector3.zero;
        normal = Vector3.up;

        for (int attempt = 0; attempt < maxAttemptsPerPalm; attempt++)
        {
            // Рівномірна випадкова точка всередині кола (не тільки по краях)
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float dist = Mathf.Sqrt(Random.value) * usableRadius;

            if (dist < centerMargin) continue;

            Vector3 candidateXZ = island.transform.position + new Vector3(
                Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);

            // Рейкаст зверху вниз, щоб знайти реальну висоту поверхні в цій точці
            Vector3 rayOrigin = candidateXZ + Vector3.up * 100f;
            if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 200f, islandLayer))
                continue;

            if (hit.point.y < island.seaLevel) continue; // потрапили під воду - точка не годиться

            // Обмеження по висоті: лише нижні схили/узбережжя.
            if (maxHeightAboveSea > 0f && hit.point.y - island.seaLevel > maxHeightAboveSea) continue;

            float slope = Vector3.Angle(hit.normal, Vector3.up);
            if (slope > maxSlopeAngle) continue;

            if (IsTooCloseToOthers(hit.point)) continue;

            point = hit.point;
            normal = hit.normal;
            return true;
        }

        return false;
    }

    private bool IsTooCloseToOthers(Vector3 point)
    {
        foreach (Vector3 existing in spawnedPositions)
        {
            if (Vector3.Distance(existing, point) < minSpacing)
                return true;
        }
        return false;
    }

    private void SpawnOnePalm(Vector3 position, Vector3 normal)
    {
        GameObject prefab = palmPrefabs[Random.Range(0, palmPrefabs.Length)];

        Quaternion rotation = alignToSurfaceNormal
            ? Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
            : Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        Vector3 finalPosition = position + Vector3.up * verticalOffset;
        GameObject palm = Instantiate(prefab, finalPosition, rotation, island.transform);

        float scale = Random.Range(scaleRange.x, scaleRange.y);
        palm.transform.localScale = Vector3.one * scale;

        spawnedPalms.Add(palm);
    }
}