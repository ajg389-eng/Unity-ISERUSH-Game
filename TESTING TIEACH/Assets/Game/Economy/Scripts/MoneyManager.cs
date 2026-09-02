using UnityEngine;

public class MoneyManager : MonoBehaviour
{
    public int startingMoney = 1000;
    public int CurrentMoney { get; private set; }

    void Awake()
    {
        CurrentMoney = startingMoney;
    }

    public bool CanAfford(int amount) => amount >= 0 && CurrentMoney >= amount;

    public bool TrySpend(int amount)
    {
        if (!CanAfford(amount)) return false;
        CurrentMoney -= amount;
        return true;
    }

    public void AddMoney(int amount)
    {
        if (amount <= 0) return;
        CurrentMoney += amount;
    }
}