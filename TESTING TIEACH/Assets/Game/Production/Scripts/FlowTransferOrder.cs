using UnityEngine;

/// <summary>Why a worker is moving one buffered item batch between flow nodes.</summary>
public enum FlowTransferPurpose
{
    FeedStation,
    SupplyIngredient,
    DeliverFinishedProduct
}

/// <summary>
/// One exact, reserved handoff in a production flow. It captures physical station
/// instances and capacity once so the worker can execute the same route the
/// scheduler reserved, even when several identical stations exist in the flow.
/// </summary>
public sealed class FlowTransferOrder
{
    static long nextId;

    public readonly long id;
    public readonly long demandTaskId;
    public readonly FlowTransferPurpose purpose;
    public GameObject source { get; private set; }
    public GameObject destination { get; private set; }
    public ItemDefinition item { get; private set; }
    public int reservedOutputUnits { get; private set; }
    public int reservedInputUnits { get; private set; }

    public FlowTransferOrder(long demandTaskId, FlowTransferPurpose purpose,
        GameObject source, GameObject destination, ItemDefinition item,
        int outputUnits, int inputUnits)
    {
        id = ++nextId;
        this.demandTaskId = demandTaskId;
        this.purpose = purpose;
        Configure(source, destination, item, outputUnits, inputUnits);
    }

    internal void Configure(GameObject source, GameObject destination, ItemDefinition item,
        int outputUnits, int inputUnits)
    {
        this.source = source;
        this.destination = destination;
        this.item = item;
        reservedOutputUnits = Mathf.Max(0, outputUnits);
        reservedInputUnits = Mathf.Max(0, inputUnits);
    }

    internal void ConsumeOutput(int amount)
    {
        if (amount <= 0) return;
        reservedOutputUnits = Mathf.Max(0, reservedOutputUnits - amount);
        if (reservedOutputUnits == 0) source = null;
    }

    internal void ConsumeInput(int amount)
    {
        if (amount <= 0) return;
        reservedInputUnits = Mathf.Max(0, reservedInputUnits - amount);
        if (reservedInputUnits == 0) destination = null;
    }
}
