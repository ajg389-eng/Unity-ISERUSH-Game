using UnityEngine;
using TMPro;

public class MoneyUI : MonoBehaviour
{
    public MoneyManager money;
    public TextMeshProUGUI moneyText;

    TextMeshProUGUI displayedMoneyText;
    int displayedMoney;

    void Update()
    {
        if (!money || !moneyText) return;
        int amount = money.CurrentMoney;
        if (displayedMoneyText == moneyText && displayedMoney == amount) return;
        moneyText.text = "$" + amount;
        displayedMoneyText = moneyText;
        displayedMoney = amount;
    }
}
