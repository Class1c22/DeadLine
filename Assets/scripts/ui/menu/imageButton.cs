using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public class imageButton : MonoBehaviour, IPointerClickHandler
{
    [Header("Що викликати при кліку")]
    public UnityEvent onClick;

    public void OnPointerClick(PointerEventData eventData)
    {

        Debug.Log($"[imageButton] Клік! Кількість підписників onClick: {onClick.GetPersistentEventCount()}");
        onClick.Invoke();
        Debug.Log("[imageButton] onClick.Invoke() виконано");

    }

    
}