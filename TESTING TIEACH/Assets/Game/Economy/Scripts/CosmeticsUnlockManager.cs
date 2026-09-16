using System.Collections.Generic;
using System.Text;
using UnityEngine;

public enum CosmeticKind
{
    Body,
    Face,
    Hat
}

/// <summary>
/// Tracks which worker cosmetics are unlocked. Defaults: red/green/blue colors,
/// one expression, no accessories. Everything else is purchased with MoneyManager.
/// </summary>
public class CosmeticsUnlockManager : MonoBehaviour
{
    const string PrefsBody = "cosmetics_body_owned";
    const string PrefsFace = "cosmetics_face_owned";
    const string PrefsHat = "cosmetics_hat_owned";

    public int colorPrice = 50;
    public int expressionPrice = 75;
    public int accessoryPrice = 125;

    readonly HashSet<int> ownedBody = new HashSet<int>();
    readonly HashSet<int> ownedFace = new HashSet<int>();
    readonly HashSet<int> ownedHat = new HashSet<int>();

    static CosmeticsUnlockManager instance;

    public static CosmeticsUnlockManager Ensure()
    {
        if (instance != null) return instance;
        instance = Object.FindFirstObjectByType<CosmeticsUnlockManager>();
        if (instance != null) return instance;

        var go = new GameObject("CosmeticsUnlockManager");
        instance = go.AddComponent<CosmeticsUnlockManager>();
        return instance;
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        Load();
        EnsureDefaults();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    public bool IsOwned(CosmeticKind kind, int index)
    {
        if (kind == CosmeticKind.Hat && index < 0)
            return true; // "None" is always free

        EnsureDefaults();
        switch (kind)
        {
            case CosmeticKind.Body: return ownedBody.Contains(index);
            case CosmeticKind.Face: return ownedFace.Contains(index);
            case CosmeticKind.Hat: return ownedHat.Contains(index);
            default: return false;
        }
    }

    public int GetPrice(CosmeticKind kind)
    {
        switch (kind)
        {
            case CosmeticKind.Body: return Mathf.Max(0, colorPrice);
            case CosmeticKind.Face: return Mathf.Max(0, expressionPrice);
            case CosmeticKind.Hat: return Mathf.Max(0, accessoryPrice);
            default: return 0;
        }
    }

    public bool TryPurchase(CosmeticKind kind, int index)
    {
        if (IsOwned(kind, index)) return true;
        if (kind == CosmeticKind.Hat && index < 0) return true;

        int price = GetPrice(kind);
        var money = Object.FindFirstObjectByType<MoneyManager>();
        if (money == null || !money.TrySpend(price))
        {
            Sfx.Play(SfxId.UiError);
            return false;
        }

        Unlock(kind, index);
        Sfx.Play(SfxId.Purchase);
        return true;
    }

    public void Unlock(CosmeticKind kind, int index)
    {
        if (kind == CosmeticKind.Hat && index < 0) return;

        switch (kind)
        {
            case CosmeticKind.Body: ownedBody.Add(index); break;
            case CosmeticKind.Face: ownedFace.Add(index); break;
            case CosmeticKind.Hat: ownedHat.Add(index); break;
        }
        Save();
    }

    /// <summary>Apply a random owned look to a worker (no paid cosmetics).</summary>
    public void ApplyRandomOwned(PartyCharacterRandomizer appearance)
    {
        if (appearance == null) return;
        EnsureDefaults();

        appearance.bodyIndex = PickRandom(ownedBody, 0);
        appearance.faceIndex = PickRandom(ownedFace, 0);
        appearance.hatIndex = -1; // default: no accessory
        appearance.ApplyCurrent();
    }

    static int PickRandom(HashSet<int> set, int fallback)
    {
        if (set == null || set.Count == 0) return fallback;
        int pick = Random.Range(0, set.Count);
        int i = 0;
        foreach (int value in set)
        {
            if (i == pick) return value;
            i++;
        }
        return fallback;
    }

    void EnsureDefaults()
    {
        // One free expression
        if (PartyCharacterRandomizer.FaceCount > 0)
            ownedFace.Add(0);

        // Free colors: red, green, blue (prefer Base variants)
        int red = FindBodyIndex("red");
        int green = FindBodyIndex("green");
        int blue = FindBodyIndex("blue");
        if (red >= 0) ownedBody.Add(red);
        if (green >= 0) ownedBody.Add(green);
        if (blue >= 0) ownedBody.Add(blue);

        // Fallback if names didn't resolve
        if (ownedBody.Count == 0 && PartyCharacterRandomizer.BodyCount > 0)
        {
            ownedBody.Add(0);
            if (PartyCharacterRandomizer.BodyCount > 1) ownedBody.Add(1);
            if (PartyCharacterRandomizer.BodyCount > 2) ownedBody.Add(2);
        }
    }

    static int FindBodyIndex(string colorToken)
    {
        int count = PartyCharacterRandomizer.BodyCount;
        int fallback = -1;
        for (int i = 0; i < count; i++)
        {
            string name = PartyCharacterRandomizer.GetBodyName(i);
            if (string.IsNullOrEmpty(name)) continue;
            string n = name.ToLowerInvariant();
            if (!n.Contains(colorToken)) continue;
            if (n.Contains("base")) return i;
            if (fallback < 0) fallback = i;
        }
        return fallback;
    }

    void Load()
    {
        ParseSet(PlayerPrefs.GetString(PrefsBody, ""), ownedBody);
        ParseSet(PlayerPrefs.GetString(PrefsFace, ""), ownedFace);
        ParseSet(PlayerPrefs.GetString(PrefsHat, ""), ownedHat);
    }

    void Save()
    {
        PlayerPrefs.SetString(PrefsBody, JoinSet(ownedBody));
        PlayerPrefs.SetString(PrefsFace, JoinSet(ownedFace));
        PlayerPrefs.SetString(PrefsHat, JoinSet(ownedHat));
        PlayerPrefs.Save();
    }

    static void ParseSet(string raw, HashSet<int> set)
    {
        set.Clear();
        if (string.IsNullOrEmpty(raw)) return;
        string[] parts = raw.Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            if (int.TryParse(parts[i], out int value))
                set.Add(value);
        }
    }

    static string JoinSet(HashSet<int> set)
    {
        if (set == null || set.Count == 0) return "";
        var sb = new StringBuilder();
        bool first = true;
        foreach (int value in set)
        {
            if (!first) sb.Append(',');
            sb.Append(value);
            first = false;
        }
        return sb.ToString();
    }
}
