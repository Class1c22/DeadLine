using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Невидима клікабельна зона поверх одного квадрата в ряду інвентаря.
/// Просто ловить клік і повідомляє InventoryManager, який слот вибрано.
/// Сам вигляд (підсвітка, іконка) НЕ малюється тут — все це вже "вшито"
/// в готовий спрайт всього ряду, який показує InventoryManager.
/// </summary>
public class InventoryClickZone : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private int slotIndex; // 0..3
    [SerializeField] private InventoryManager manager;

    public void OnPointerClick(PointerEventData eventData)
    {
        manager?.SelectSlot(slotIndex);
    }
}