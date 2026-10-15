using System;
using System.Collections.Generic;

[Serializable]
public sealed class ProductionStationCheckpoint
{
    [Serializable] public sealed class Count { public string item; public int units; }
    [Serializable] public sealed class Food { public string item; public float age; }
    public List<Count> inputs = new List<Count>();
    public List<Food> pickupFood = new List<Food>();
    public int output, processing;
    public long remainingTicks, creditTicks;
}

[Serializable]
public sealed class ProductionWorkerCheckpoint
{
    public string item, sourceGuid;
    public int units;
    public long readyDelayTicks;
}
