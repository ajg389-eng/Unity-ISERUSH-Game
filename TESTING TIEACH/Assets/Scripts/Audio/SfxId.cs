/// <summary>
/// Sound effect ids. Drop matching clips in Resources/Audio/SFX/ (e.g. UiClick.wav)
/// or assign them on the SfxLibrary asset.
/// </summary>
public enum SfxId
{
    UiClick,
    UiOpen,
    UiClose,
    UiError,

    Purchase,
    EarnMoney,
    SpendMoney,

    BuildPlace,
    BuildPlaceFail,
    BuildRotate,
    BuildPickup,
    BuildRemove,
    GridExpand,

    StationSelect,
    AssignWorker,
    ClearWorker,
    AssignOutput,
    ClearOutput,
    HireWorker,

    CustomerArrive,
    ItemDelivered,
    CustomerServed,

    StationWorkComplete,
    HeatLampStock,
    FoodWasted,

    MissionComplete
}
