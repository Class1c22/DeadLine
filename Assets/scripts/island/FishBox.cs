using UnityEngine;

public class WaterFishZone : MonoBehaviour
{
    [Tooltip("Якщо не задано - шукається автоматично через FindObjectOfType, бо акула - об'єкт сцени.")]
    public SharkController shark;

    void Awake()
    {
        if (shark == null)
            shark = FindObjectOfType<SharkController>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Fish")) return;

        Pickupable pickupable = other.GetComponentInParent<Pickupable>();
        if (pickupable != null && pickupable.isHeld)
        {
            return;
        }

        Transform fishRoot = pickupable != null ? pickupable.transform : other.transform;

        if (shark != null)
            shark.RequestEatFish(fishRoot);
        else
            Debug.LogWarning("[WaterFishZone] Shark не призначено і не знайдено на сцені!");
    }
}