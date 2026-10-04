using System;
using UnityEditor;
using UnityEngine;

public static class StoreCustomizationTextureInstaller
{
    const string Root = "Assets/Game/Core/Resources/StoreCustomization";

    [MenuItem("Tools/ISE Rush/Install Store Customization Textures")]
    public static void Install()
    {
        EnsureFolder("Assets/Game/Core/Resources", "StoreCustomization");
        EnsureFolder(Root, "Walls");
        EnsureFolder(Root, "Floors");

        Copy("Assets/Imported/Models/StoreModels/Fast Food Restaurant Kit/Common/Textures/FFK_Stone_01_A.png", "Walls/03 Dark Stone.png");
        Copy("Assets/Imported/Models/StoreModels/Fast Food Restaurant Kit/Common/Textures/FFK_Wood_01_A.png", "Walls/05 Light Wood.png");

        Copy("Assets/Imported/Models/StoreModels/_BPS Basic Assets/Common/Textures/Legacy/Wood Floor 01.png", "Floors/02 Warm Wood.png");
        Copy("Assets/Imported/Models/LowPolyCityProps/Textures/Road Concrete Tile.png", "Floors/05 Cream Tile.png");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Installed store customization wall and floor textures.");
    }

    static void EnsureFolder(string parent, string name)
    {
        string path = parent + "/" + name;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
    }

    static void Copy(string source, string relativeDestination)
    {
        string destination = Root + "/" + relativeDestination;
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(destination) != null) return;
        if (!AssetDatabase.CopyAsset(source, destination))
            throw new InvalidOperationException("Could not copy customization texture: " + source);
    }
}
