using System;
using UnityEngine;

public class FishProgressBar : MonoBehaviour
{
    [Tooltip("Скільки риби треба закинути, щоб бар заповнився повністю")]
    public int fishNeeded = 10;

    /// <summary>
    /// Викликається один раз, коли бар щойно заповнився повністю
    /// (акула з'їла достатньо риби і "ситa"). На цю подію підписується
    /// PlayerDeathHandler, щоб показати екран перемоги.
    /// </summary>
    public event Action OnFishBarFull;

    [Tooltip("Швидкість руху смужок")]
    public float scrollSpeedX = 1f;
    public float scrollSpeedY = 0f;

    private SpriteRenderer spriteRend;
    private Material matInstance;
    private int currentFish = 0;
    private bool isFull = false;

    private float fullScaleX;
    private float leftEdgeX; // фіксована позиція лівого краю бару

    void Start()
    {
        spriteRend = GetComponent<SpriteRenderer>();

        fullScaleX = transform.localScale.x;

        // Обчислюємо позицію лівого краю (з урахуванням pivot по центру)
        leftEdgeX = transform.localPosition.x - (fullScaleX / 2f);

        matInstance = new Material(spriteRend.sharedMaterial);
        spriteRend.material = matInstance;

        UpdateVisual();
    }

    void Update()
    {
        float offsetX = Time.time * scrollSpeedX;
        float offsetY = Time.time * scrollSpeedY;
        matInstance.mainTextureOffset = new Vector2(offsetX, offsetY);
    }

    public void AddFish(int amount = 1)
    {
        currentFish += amount;
        currentFish = Mathf.Clamp(currentFish, 0, fishNeeded);

        UpdateVisual();

        if (currentFish >= fishNeeded && !isFull)
        {
            isFull = true;
            OnBarFull();
        }
    }

    private void UpdateVisual()
    {
        float progress = (float)currentFish / fishNeeded;
        float newScaleX = progress * fullScaleX;

        // Центр = лівий край + половина нового розміру
        // (це тримає лівий край на місці незалежно від знаку scale)
        Vector3 pos = transform.localPosition;
        pos.x = leftEdgeX + (newScaleX / 2f);
        transform.localPosition = pos;

        Vector3 scale = transform.localScale;
        scale.x = newScaleX;
        transform.localScale = scale;
    }

    private void OnBarFull()
    {
        OnFishBarFull?.Invoke();
    }

    void OnDestroy()
    {
        if (matInstance != null)
            Destroy(matInstance);
    }
}