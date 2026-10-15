using UnityEngine;

// These adapters only expose/project buffers. Processing is owned by the shared
// simulation for connected production stations, including their saved progress.
public partial class GrillStation
{
    public int ScheduledProcessingUnits => pattyUnits;
    public void ApplyScheduledBuffers(int input, int secondary, int output, int processing, float progress)
    {
        waitingInputUnits = input;
        secondaryInputUnits = secondary;
        cookedOutputUnits = output;
        pattyUnits = processing;
        cookTimer = progress;
        RefreshItemDisplay(false);
    }
}

public partial class FryerStation
{
    public int ScheduledProcessingUnits => basketUnits;
    public float ScheduledProcessingProgress => cookTimer;
    public void ApplyScheduledBuffers(int input, int output, int processing, float progress)
    {
        waitingInputUnits = input;
        cookedOutputUnits = output;
        basketUnits = processing;
        cookTimer = progress;
        RefreshItemDisplay(false);
    }
}

public partial class CuttingStation
{
    public int ScheduledProcessingUnits => processingUnits;
    public float ScheduledProcessingProgress => processingTimer;
    public void ApplyScheduledBuffers(int input, int output, int processing, float progress)
    {
        bool changed = inputUnits != input || outputUnits != output || processingUnits != processing;
        inputUnits = input;
        outputUnits = output;
        processingUnits = processing;
        processingTimer = progress;
        if (changed) RefreshItemDisplay(true);
    }
}

public partial class AssemblyStation
{
    public void ApplyScheduledBuffers(int processed, int pantry, int third, int output, int processing, float progress)
    {
        bool changed = bufferedProcessedInputs != processed || bufferedPantryInputs != pantry
            || bufferedThirdInputs != third || bufferedOutputs != output || processingUnits != processing;
        bufferedProcessedInputs = processed;
        bufferedPantryInputs = pantry;
        bufferedThirdInputs = third;
        bufferedOutputs = output;
        processingUnits = processing;
        processingTimer = progress;
        if (changed) RefreshTableDisplay();
    }
}

public partial class FreezerStation
{
    public float CaptureScheduledCredits()
    {
        StationOutputRateLimiter.GetAvailableUnits(EffectiveMaxOutputPerMinute,
            ref outputTokens, ref outputTokensUpdatedAt, ref outputTokensInitialized);
        return outputTokens;
    }
    public void ApplyScheduledCredits(float credits)
    {
        outputTokens = credits;
        outputTokensUpdatedAt = Time.time;
        outputTokensInitialized = true;
    }
}

public partial class PantryStation
{
    public float CaptureScheduledCredits()
    {
        StationOutputRateLimiter.GetAvailableUnits(EffectiveMaxOutputPerMinute,
            ref outputTokens, ref outputTokensUpdatedAt, ref outputTokensInitialized);
        return outputTokens;
    }
    public void ApplyScheduledCredits(float credits)
    {
        outputTokens = credits;
        outputTokensUpdatedAt = Time.time;
        outputTokensInitialized = true;
    }
}
