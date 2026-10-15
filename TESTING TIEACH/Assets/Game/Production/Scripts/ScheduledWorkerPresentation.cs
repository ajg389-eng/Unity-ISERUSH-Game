using KitchenProduction;
using UnityEngine;

public partial class KitchenEmployee
{
    bool scheduledPresentation, scheduledBusy;
    string scheduledDescription;
    WorkerActivityState scheduledActivity;
    CapsuleCollider scheduledCollider;
    Rigidbody scheduledBody;
    bool previousTrigger, previousKinematic;

    public void ApplyScheduledPresentation(Vector3 position, ItemDefinition cargo, int quantity,
        string description, ProductionSimulation.TripPhase phase, float progress, GameObject facing)
    {
        if (!scheduledPresentation)
        {
            AbortCurrentWork();
            scheduledCollider = GetComponent<CapsuleCollider>();
            scheduledBody = GetComponent<Rigidbody>();
            if (scheduledCollider != null) { previousTrigger = scheduledCollider.isTrigger; scheduledCollider.isTrigger = true; }
            if (scheduledBody != null) { previousKinematic = scheduledBody.isKinematic; scheduledBody.isKinematic = true; }
            scheduledPresentation = true;
        }
        Vector3 oldPosition = transform.position;
        transform.position = position;
        scheduledDescription = description;
        scheduledBusy = phase != ProductionSimulation.TripPhase.Idle;
        bool moving = phase == ProductionSimulation.TripPhase.Approach || phase == ProductionSimulation.TripPhase.Travel;
        scheduledActivity = moving ? WorkerActivityState.Traveling
            : scheduledBusy ? WorkerActivityState.Working : WorkerActivityState.Idle;
        heldUnits = quantity;
        heldDeliveryItem = quantity > 0 ? cargo : null;
        hasPatty = false;
        awaitingOutputDelivery = quantity > 0;
        deliverTarget = quantity > 0 ? facing : null;
        ShowTaskBar = phase == ProductionSimulation.TripPhase.Pickup || phase == ProductionSimulation.TripPhase.Placement;
        TaskProgress = ShowTaskBar ? progress : 0f;
        SetAttentionWave(false);
        SetStationWorkAnimation(ShowTaskBar ? PartyCharacterAnimator.StationWorkKind.Plating
            : PartyCharacterAnimator.StationWorkKind.None);
        if (characterAnimator == null) characterAnimator = PartyCharacterAnimator.EnsureOn(gameObject);
        if (moving && (position - oldPosition).sqrMagnitude > 0.000001f)
            characterAnimator?.FaceMovementToward(position + position - oldPosition);
        else if (facing != null) characterAnimator?.FaceTowardAdjacentObject(facing);
    }

    public void ClearScheduledPresentation()
    {
        if (!scheduledPresentation) return;
        scheduledPresentation = scheduledBusy = false;
        if (scheduledCollider != null) scheduledCollider.isTrigger = previousTrigger;
        if (scheduledBody != null) scheduledBody.isKinematic = previousKinematic;
        ClearHeldInventory();
        ShowTaskBar = false;
        SetStationWorkAnimation(PartyCharacterAnimator.StationWorkKind.None);
    }
}
