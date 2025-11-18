using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Troubleshooting script to verify the weapon upgrade system is properly imported.
/// Attach this to any GameObject and check the console for status.
/// </summary>
public class WeaponSystemVerification : MonoBehaviour
{
    [Header("Click button below to verify system")]
    [Tooltip("Checks if all required scripts are present and properly configured")]
    public bool runVerification = false;

    void Start()
    {
        if (runVerification)
        {
            VerifySystem();
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Verify Weapon System")]
#endif
    public void VerifySystem()
    {
        Debug.Log("=== WEAPON UPGRADE SYSTEM VERIFICATION ===");
        
        bool allGood = true;
        
        // Check for core classes
        allGood &= CheckType("WeaponStats");
        allGood &= CheckType("UpgradeEffect");
        allGood &= CheckType("UpgradePickup");
        allGood &= CheckType("WeaponData");
        allGood &= CheckType("PlayerWeaponController");
        allGood &= CheckType("EnhancedBullet");
        allGood &= CheckType("UpgradePickupBehavior");
        allGood &= CheckType("ElementalEffects");
        
        Debug.Log("===========================================");
        
        if (allGood)
        {
            Debug.Log("✅ <color=green><b>ALL SYSTEMS OPERATIONAL!</b></color>");
            Debug.Log("You should now see these menu items:");
            Debug.Log("  • Right-click → Create → Weapons → Weapon Data");
            Debug.Log("  • Right-click → Create → Weapon System → Upgrade Pickup");
            Debug.Log("\nIf you DON'T see them:");
            Debug.Log("  1. Check Unity console for compilation errors");
            Debug.Log("  2. Try reimporting the scripts (right-click → Reimport)");
            Debug.Log("  3. Restart Unity Editor");
        }
        else
        {
            Debug.LogError("❌ <color=red><b>MISSING SCRIPTS!</b></color>");
            Debug.LogError("Please ensure all .cs files are imported to your project.");
        }
    }
    
    bool CheckType(string typeName)
    {
        System.Type type = System.Type.GetType(typeName);
        if (type != null)
        {
            Debug.Log($"✅ {typeName} found");
            return true;
        }
        else
        {
            Debug.LogError($"❌ {typeName} NOT FOUND - missing script!");
            return false;
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Force Refresh Asset Database")]
    public void ForceRefresh()
    {
        Debug.Log("Refreshing Unity Asset Database...");
        AssetDatabase.Refresh();
        Debug.Log("Done! Check for menu items now.");
    }
    
    [ContextMenu("Show Create Menu Locations")]
    public void ShowMenuLocations()
    {
        Debug.Log("=== WHERE TO FIND CREATE MENUS ===");
        Debug.Log("1. In Project window, right-click on any folder");
        Debug.Log("2. Navigate to: Create → Weapons → Weapon Data");
        Debug.Log("3. Navigate to: Create → Weapon System → Upgrade Pickup");
        Debug.Log("");
        Debug.Log("If these don't appear:");
        Debug.Log("• Check for compilation errors in Console");
        Debug.Log("• Scripts must be in Assets folder (not Packages)");
        Debug.Log("• Try: Assets → Reimport All");
        Debug.Log("• Try: Restarting Unity");
    }
#endif
}
