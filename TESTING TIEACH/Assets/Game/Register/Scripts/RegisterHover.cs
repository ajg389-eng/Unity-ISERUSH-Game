using UnityEngine;

public class RegisterHover : MonoBehaviour
{
    public Renderer rend;
    public Color hoverColor = Color.yellow;

    Color original;

    void Start()
    {
        if (!rend) rend = GetComponentInChildren<Renderer>();
        if (rend) original = rend.material.color;
    }

    public void SetHover(bool on)
    {
        if (!rend) return;
        rend.material.color = on ? hoverColor : original;
    }
}