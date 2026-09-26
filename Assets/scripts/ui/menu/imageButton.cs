using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public class imageButton : MonoBehaviour, IPointerClickHandler
{
    [Header("Що викликати при кліку")]
    public UnityEvent onClick;

    public void OnPointerClick(PointerEventData eventData)
    {

        onClick.Invoke();

    }

}