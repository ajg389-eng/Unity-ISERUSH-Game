using UnityEngine;
using TMPro;

public class MoneyUI : MonoBehaviour
{
    public MoneyManager money;
    public TextMeshProUGUI moneyText;

    void Update()
    {
        if (!money || !moneyText) return;
        moneyText.text = "$" + money.CurrentMoney.ToString();
    }
}