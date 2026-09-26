using Photon.Pun;
using UnityEngine;

public class PlayerPickup : MonoBehaviourPun
{
    public Transform handAttachPoint;
    [Tooltip("Окрема точка кріплення для риби (замість handAttachPoint) - призначити тут, напряму на гравці, а НЕ на префабі риби. Prefab-асет не може надійно зберігати посилання на об'єкт сцени (як pickFish), тому таке посилання після PhotonNetwork.Instantiate обнулялось і риба падала назад на handAttachPoint.")]
    public Transform fishAttachPoint;
    public float pickupRadius = 2f;
    public LayerMask pickupableLayer;
    public HandAnimatorController handAnimatorController;
    public InventoryManager inventoryManager;
    public string fishingRodTriggerName = "EquipRod";
    public float throwForwardForce = 6f;
    public float throwUpwardForce = 2f;

    private Pickupable currentlyHeld = null;
    public bool IsHoldingFishingRod =>
        currentlyHeld != null && currentlyHeld.equipAnimTrigger == fishingRodTriggerName;

    void Awake()
    {
        if (inventoryManager != null)
        {
            inventoryManager.OnEquip += HandleEquip;
            inventoryManager.OnUnequip += HandleUnequip;
        }
    }

    void OnDestroy()
    {
        if (inventoryManager != null)
        {
            inventoryManager.OnEquip -= HandleEquip;
            inventoryManager.OnUnequip -= HandleUnequip;
        }
    }

    void Start()
    {
        // Підстраховка: якщо PlayerRig з якоїсь причини не вимкнув цей скрипт
        // на чужій копії - робимо це тут теж.
        if (!photonView.IsMine)
        {
            enabled = false;
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
            TryPickUpNearby();

        if (Input.GetKeyDown(KeyCode.R) && currentlyHeld != null)
            ThrowCurrent();
    }

    void TryPickUpNearby()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, pickupRadius, pickupableLayer);

        Pickupable closest = null;
        float closestDist = float.MaxValue;

        foreach (Collider hit in hits)
        {
            Pickupable p = hit.GetComponent<Pickupable>();
            if (p != null && !p.isHeld)
            {
                float dist = Vector3.Distance(transform.position, hit.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = p;
                }
            }
        }

        if (closest == null) return;

        if (inventoryManager != null && !inventoryManager.HasFreeSlot())
        {
            return;
        }

        PhotonView itemView = closest.GetComponent<PhotonView>();
        if (itemView == null)
        {
            Debug.LogError($"[PlayerPickup] На предметі '{closest.name}' немає PhotonView - додай його, інакше підбір не синхронізується по мережі.");
            return;
        }

        // Гонка: якщо два гравці натиснули E в один момент по одному предмету,
        // RPC_RequestPickup сам вирішить (на власнику предмета/MasterClient),
        // хто саме забрав, і розішле фактичний результат всім через RPC_Store.
        itemView.RPC(nameof(Pickupable.RPC_RequestPickup), RpcTarget.All, photonView.ViewID);
    }

    /// <summary>
    /// Викликається з Pickupable.RPC_Store, коли мережа підтвердила, що САМЕ
    /// цей гравець (photonView.IsMine == true) отримав предмет у руки/інвентар.
    /// </summary>
    public void ConfirmPickup(Pickupable item)
    {
        if (inventoryManager != null)
        {
            item.Store();
            inventoryManager.AddItem(item);
        }
        else
        {
            EquipItem(item);
        }
    }

    private void HandleEquip(Pickupable item)
    {
        if (currentlyHeld == item) return;

        if (currentlyHeld != null)
            currentlyHeld.Store();

        EquipItem(item);
    }

    private void HandleUnequip()
    {
        if (currentlyHeld != null)
            currentlyHeld.Store();

        currentlyHeld = null;

        if (handAnimatorController != null)
        {
            handAnimatorController.SetHolding(false);
            handAnimatorController.SetFishingEquipped(false);
        }
    }

    private void EquipItem(Pickupable item)
    {
        Transform attachTarget = ResolveAttachPoint(item);

        item.PickUp(attachTarget);
        currentlyHeld = item;

        if (handAnimatorController != null)
        {
            handAnimatorController.SetHolding(true);
            handAnimatorController.PlayEquipAnimation(item.equipAnimTrigger);

            bool isFishingRod = item.equipAnimTrigger == fishingRodTriggerName;
            handAnimatorController.SetFishingEquipped(isFishingRod);
        }
    }

    /// <summary>
    /// Обирає, куди прикріпити предмет у руці. Риба (тег "Fish") завжди йде в
    /// fishAttachPoint (pickFish), якщо він призначений на гравці - це надійніше,
    /// ніж item.customAttachPoint на самому префабі риби, бо той не переживає
    /// PhotonNetwork.Instantiate (посилання на об'єкт сцени на prefab-асеті
    /// обнуляється). Для решти предметів - як і раніше: customAttachPoint,
    /// якщо заданий, інакше загальний handAttachPoint.
    /// </summary>
    private Transform ResolveAttachPoint(Pickupable item)
    {
        if (item.CompareTag("Fish") && fishAttachPoint != null)
            return fishAttachPoint;

        if (item.customAttachPoint != null)
            return item.customAttachPoint;

        return handAttachPoint;
    }

    void ThrowCurrent()
    {
        if (currentlyHeld == null) return;

        Vector3 throwStartPos = transform.position - transform.forward * 1f + Vector3.up * 1f;
        Vector3 throwVelocity = -transform.forward * throwForwardForce + Vector3.up * throwUpwardForce;

        Pickupable thrown = currentlyHeld;

        if (inventoryManager != null)
            inventoryManager.RemoveItem(thrown);

        PhotonView itemView = thrown.GetComponent<PhotonView>();
        itemView.RPC(nameof(Pickupable.RPC_Drop), RpcTarget.All, throwStartPos, throwVelocity);

        currentlyHeld = null;

        if (handAnimatorController != null)
        {
            handAnimatorController.SetHolding(false);
            handAnimatorController.SetFishingEquipped(false);
        }
    }
}