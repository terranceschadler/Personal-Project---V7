using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

/// <summary>
/// STANDALONE asset creator that works even if other scripts have errors.
/// This creates basic ScriptableObject files that you can configure later.
/// 
/// USAGE:
/// 1. Put this script in Assets/Editor/ folder (create Editor folder if needed)
/// 2. Wait for Unity to compile
/// 3. Click Window → Weapon System Creator (top menu)
/// 4. Click buttons to create assets
/// </summary>
public class StandaloneWeaponCreator
{
#if UNITY_EDITOR
    
    [MenuItem("Window/Weapon System Creator")]
    static void ShowWindow()
    {
        EditorWindow window = EditorWindow.GetWindow(typeof(StandaloneCreatorWindow));
        window.titleContent = new GUIContent("Weapon Creator");
        window.Show();
    }
    
#endif
}

#if UNITY_EDITOR
public class StandaloneCreatorWindow : EditorWindow
{
    private string outputFolder = "Assets";
    
    void OnGUI()
    {
        GUILayout.Label("Weapon Upgrade System - Asset Creator", EditorStyles.boldLabel);
        GUILayout.Space(10);
        
        GUILayout.Label("Output Folder:", EditorStyles.label);
        GUILayout.BeginHorizontal();
        outputFolder = GUILayout.TextField(outputFolder, GUILayout.Width(300));
        if (GUILayout.Button("Browse", GUILayout.Width(60)))
        {
            string path = EditorUtility.OpenFolderPanel("Select Output Folder", "Assets", "");
            if (!string.IsNullOrEmpty(path))
            {
                if (path.StartsWith(Application.dataPath))
                {
                    outputFolder = "Assets" + path.Substring(Application.dataPath.Length);
                }
            }
        }
        GUILayout.EndHorizontal();
        
        GUILayout.Space(20);
        
        if (GUILayout.Button("Create WeaponData ScriptableObject", GUILayout.Height(40)))
        {
            CreateWeaponDataManual();
        }
        
        GUILayout.Space(10);
        
        if (GUILayout.Button("Create UpgradePickup ScriptableObject", GUILayout.Height(40)))
        {
            CreateUpgradePickupManual();
        }
        
        GUILayout.Space(20);
        GUILayout.Label("Pre-configured Examples:", EditorStyles.boldLabel);
        
        if (GUILayout.Button("Create Example: Starter Rifle", GUILayout.Height(30)))
        {
            CreateExampleWeapon();
        }
        
        if (GUILayout.Button("Create Example: +5 Damage Upgrade", GUILayout.Height(30)))
        {
            CreateExampleDamage();
        }
        
        if (GUILayout.Button("Create Example: +20% Fire Rate Upgrade", GUILayout.Height(30)))
        {
            CreateExampleFireRate();
        }
        
        GUILayout.Space(20);
        
        EditorGUILayout.HelpBox(
            "These buttons create ScriptableObject assets that you can configure in the Inspector.\n\n" +
            "If you get errors about missing types (WeaponData, UpgradePickup), " +
            "make sure all weapon system scripts are imported and compiled first!",
            MessageType.Info
        );
    }
    
    void CreateWeaponDataManual()
    {
        try
        {
            var asset = ScriptableObject.CreateInstance("WeaponData");
            if (asset == null)
            {
                EditorUtility.DisplayDialog("Error", 
                    "WeaponData script not found!\n\n" +
                    "Make sure WeaponData.cs is imported and compiled.", 
                    "OK");
                return;
            }
            
            string path = AssetDatabase.GenerateUniqueAssetPath(outputFolder + "/NewWeaponData.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            
            Debug.Log($"✅ Created WeaponData at: {path}");
            EditorUtility.DisplayDialog("Success!", $"Created WeaponData at:\n{path}", "OK");
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("Error", 
                $"Failed to create WeaponData:\n{e.Message}\n\n" +
                "Make sure WeaponData.cs is imported correctly!", 
                "OK");
        }
    }
    
    void CreateUpgradePickupManual()
    {
        try
        {
            var asset = ScriptableObject.CreateInstance("UpgradePickup");
            if (asset == null)
            {
                EditorUtility.DisplayDialog("Error", 
                    "UpgradePickup script not found!\n\n" +
                    "Make sure UpgradePickup.cs is imported and compiled.", 
                    "OK");
                return;
            }
            
            string path = AssetDatabase.GenerateUniqueAssetPath(outputFolder + "/NewUpgradePickup.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            
            Debug.Log($"✅ Created UpgradePickup at: {path}");
            EditorUtility.DisplayDialog("Success!", $"Created UpgradePickup at:\n{path}", "OK");
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("Error", 
                $"Failed to create UpgradePickup:\n{e.Message}\n\n" +
                "Make sure UpgradePickup.cs is imported correctly!", 
                "OK");
        }
    }
    
    void CreateExampleWeapon()
    {
        try
        {
            var asset = ScriptableObject.CreateInstance("WeaponData");
            if (asset == null)
            {
                EditorUtility.DisplayDialog("Error", "WeaponData script not found!", "OK");
                return;
            }
            
            // Try to set properties via reflection
            var type = asset.GetType();
            type.GetField("weaponName")?.SetValue(asset, "Starter Rifle");
            
            var baseStats = type.GetField("baseStats")?.GetValue(asset);
            if (baseStats != null)
            {
                var statsType = baseStats.GetType();
                statsType.GetField("baseDamage")?.SetValue(baseStats, 10f);
                statsType.GetField("baseFireRate")?.SetValue(baseStats, 5f);
                statsType.GetField("baseMagazineSize")?.SetValue(baseStats, 30);
                statsType.GetField("baseReloadSpeed")?.SetValue(baseStats, 2f);
                statsType.GetField("bulletVelocity")?.SetValue(baseStats, 20f);
                statsType.GetField("projectileLifetime")?.SetValue(baseStats, 5f);
            }
            
            string path = AssetDatabase.GenerateUniqueAssetPath(outputFolder + "/StarterRifle_WeaponData.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            
            Debug.Log($"✅ Created example Starter Rifle at: {path}");
            EditorUtility.DisplayDialog("Success!", 
                $"Created Starter Rifle at:\n{path}\n\n" +
                "Remember to assign your bullet prefab!", 
                "OK");
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("Error", $"Failed:\n{e.Message}", "OK");
        }
    }
    
    void CreateExampleDamage()
    {
        try
        {
            var asset = ScriptableObject.CreateInstance("UpgradePickup");
            if (asset == null)
            {
                EditorUtility.DisplayDialog("Error", "UpgradePickup script not found!", "OK");
                return;
            }
            
            string path = AssetDatabase.GenerateUniqueAssetPath(outputFolder + "/DamageBoost_Common.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            
            Debug.Log($"✅ Created Damage Boost upgrade at: {path}");
            Debug.Log("Configure it in Inspector: UpgradeType=DamageFlat, Value=5");
            EditorUtility.DisplayDialog("Success!", 
                $"Created Damage Boost at:\n{path}\n\n" +
                "Configure in Inspector:\n" +
                "- Pickup Name: Damage Boost\n" +
                "- Effects → Size: 1\n" +
                "- Element 0 → Type: DamageFlat\n" +
                "- Element 0 → Value: 5", 
                "OK");
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("Error", $"Failed:\n{e.Message}", "OK");
        }
    }
    
    void CreateExampleFireRate()
    {
        try
        {
            var asset = ScriptableObject.CreateInstance("UpgradePickup");
            if (asset == null)
            {
                EditorUtility.DisplayDialog("Error", "UpgradePickup script not found!", "OK");
                return;
            }
            
            string path = AssetDatabase.GenerateUniqueAssetPath(outputFolder + "/RapidFire_Uncommon.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            
            Debug.Log($"✅ Created Rapid Fire upgrade at: {path}");
            Debug.Log("Configure it in Inspector: UpgradeType=FireRatePercent, Value=20");
            EditorUtility.DisplayDialog("Success!", 
                $"Created Rapid Fire at:\n{path}\n\n" +
                "Configure in Inspector:\n" +
                "- Pickup Name: Rapid Fire\n" +
                "- Effects → Size: 1\n" +
                "- Element 0 → Type: FireRatePercent\n" +
                "- Element 0 → Value: 20", 
                "OK");
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("Error", $"Failed:\n{e.Message}", "OK");
        }
    }
}
#endif
