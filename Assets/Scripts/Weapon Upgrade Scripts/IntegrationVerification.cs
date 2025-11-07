using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Verification tool to check if the integrated upgrade system is properly setup.
/// Attach to any GameObject and run in Play mode or use the context menu.
/// </summary>
public class IntegrationVerification : MonoBehaviour
{
    [Header("Auto-Check on Start")]
    public bool checkOnStart = true;
    
    [Header("Manual Check")]
    [Tooltip("Click this in Inspector to run verification")]
    public bool runCheck = false;
    
    private void Start()
    {
        if (checkOnStart)
        {
            VerifyIntegration();
        }
    }
    
    private void OnValidate()
    {
        if (runCheck)
        {
            runCheck = false;
            VerifyIntegration();
        }
    }
    
#if UNITY_EDITOR
    [ContextMenu("Verify Integration")]
#endif
    public void VerifyIntegration()
    {
        Debug.Log("========================================");
        Debug.Log("   INTEGRATED UPGRADE SYSTEM CHECK");
        Debug.Log("========================================\n");
        
        bool allGood = true;
        
        // Check for IntegratedUpgradeSystem
        IntegratedUpgradeSystem upgradeSystem = FindObjectOfType<IntegratedUpgradeSystem>();
        if (upgradeSystem != null)
        {
            Debug.Log("✅ IntegratedUpgradeSystem found");
            
            // Check its configuration
            if (upgradeSystem.GetComponent<UpgradeGenerator>() != null)
                Debug.Log("  ✅ UpgradeGenerator component present");
            else
            {
                Debug.LogWarning("  ⚠️ UpgradeGenerator component missing!");
                allGood = false;
            }
            
            if (upgradeSystem.GetComponent<UpgradeSelectionUI>() != null)
                Debug.Log("  ✅ UpgradeSelectionUI component present");
            else
            {
                Debug.LogWarning("  ⚠️ UpgradeSelectionUI component missing!");
                allGood = false;
            }
        }
        else
        {
            Debug.LogError("❌ IntegratedUpgradeSystem NOT FOUND in scene!");
            Debug.LogError("  → Create an empty GameObject and add IntegratedUpgradeSystem component");
            allGood = false;
        }
        
        // Check for Player
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            Debug.Log("✅ Player found with 'Player' tag");
            
            PlayerWeaponController weaponController = player.GetComponent<PlayerWeaponController>();
            if (weaponController != null)
            {
                Debug.Log("  ✅ PlayerWeaponController on player");
                
                if (weaponController.weaponData != null)
                    Debug.Log("    ✅ WeaponData assigned");
                else
                    Debug.LogWarning("    ⚠️ WeaponData not assigned!");
                
                if (weaponController.firePoint != null)
                    Debug.Log("    ✅ Fire Point assigned");
                else
                    Debug.LogWarning("    ⚠️ Fire Point not assigned!");
            }
            else
            {
                Debug.LogWarning("  ⚠️ PlayerWeaponController not found on player");
                Debug.LogWarning("  → Add PlayerWeaponController component to player");
            }
        }
        else
        {
            Debug.LogError("❌ No GameObject with 'Player' tag found!");
            Debug.LogError("  → Tag your player GameObject as 'Player'");
            allGood = false;
        }
        
        // Check for UI
        UpgradeSelectionUI selectionUI = FindObjectOfType<UpgradeSelectionUI>();
        if (selectionUI != null)
        {
            Debug.Log("✅ UpgradeSelectionUI found");
            
            #if UNITY_EDITOR
            SerializedObject so = new SerializedObject(selectionUI);
            
            var selectionPanel = so.FindProperty("selectionPanel");
            if (selectionPanel.objectReferenceValue != null)
                Debug.Log("  ✅ Selection Panel assigned");
            else
            {
                Debug.LogWarning("  ⚠️ Selection Panel not assigned!");
                allGood = false;
            }
            
            var container = so.FindProperty("upgradeOptionsContainer");
            if (container.objectReferenceValue != null)
                Debug.Log("  ✅ Options Container assigned");
            else
            {
                Debug.LogWarning("  ⚠️ Options Container not assigned!");
                allGood = false;
            }
            
            var prefab = so.FindProperty("upgradeOptionPrefab");
            if (prefab.objectReferenceValue != null)
                Debug.Log("  ✅ Option Prefab assigned");
            else
            {
                Debug.LogWarning("  ⚠️ Option Prefab not assigned!");
                allGood = false;
            }
            #endif
        }
        else
        {
            Debug.LogWarning("⚠️ UpgradeSelectionUI not found");
            Debug.LogWarning("  → This is needed for the upgrade choice UI");
        }
        
        // Check for Canvas
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas != null)
        {
            Debug.Log("✅ Canvas found");
        }
        else
        {
            Debug.LogWarning("⚠️ No Canvas found in scene");
            Debug.LogWarning("  → Create a Canvas for the upgrade UI");
        }
        
        // Check for pickup prefab reference
        if (upgradeSystem != null)
        {
            #if UNITY_EDITOR
            SerializedObject so = new SerializedObject(upgradeSystem);
            var pickupPrefab = so.FindProperty("upgradePickupPrefab");
            if (pickupPrefab.objectReferenceValue != null)
                Debug.Log("✅ Upgrade Pickup Prefab assigned to IntegratedUpgradeSystem");
            else
            {
                Debug.LogWarning("⚠️ Upgrade Pickup Prefab not assigned!");
                Debug.LogWarning("  → Assign your IntegratedUpgradePickup prefab");
                allGood = false;
            }
            #endif
        }
        
        // Check for enemies with droppers
        EnemyUpgradeDropper[] droppers = FindObjectsOfType<EnemyUpgradeDropper>();
        if (droppers.Length > 0)
        {
            Debug.Log($"✅ Found {droppers.Length} enemies with EnemyUpgradeDropper");
        }
        else
        {
            Debug.LogWarning("⚠️ No EnemyUpgradeDropper components found");
            Debug.LogWarning("  → Add EnemyUpgradeDropper to enemy prefabs");
        }
        
        Debug.Log("\n========================================");
        
        if (allGood)
        {
            Debug.Log("✅✅✅ ALL SYSTEMS READY! ✅✅✅");
            Debug.Log("\nYou can now:");
            Debug.Log("1. Kill enemies to spawn upgrade pickups");
            Debug.Log("2. Collect pickups to see upgrade choices");
            Debug.Log("3. Click an upgrade to apply it");
            Debug.Log("4. See your weapon stats improve!");
        }
        else
        {
            Debug.LogWarning("⚠️ SETUP INCOMPLETE");
            Debug.LogWarning("Please fix the issues above.");
            Debug.LogWarning("See INTEGRATION_GUIDE.md for detailed instructions.");
        }
        
        Debug.Log("========================================\n");
    }
    
#if UNITY_EDITOR
    [MenuItem("Tools/Upgrade System/Verify Integration")]
    static void VerifyIntegrationMenu()
    {
        IntegrationVerification verification = FindObjectOfType<IntegrationVerification>();
        
        if (verification == null)
        {
            GameObject go = new GameObject("IntegrationVerification");
            verification = go.AddComponent<IntegrationVerification>();
        }
        
        verification.VerifyIntegration();
    }
    
    [MenuItem("Tools/Upgrade System/Open Integration Guide")]
    static void OpenGuide()
    {
        string guidePath = "Assets/INTEGRATION_GUIDE.md";
        
        if (System.IO.File.Exists(guidePath))
        {
            System.Diagnostics.Process.Start(guidePath);
        }
        else
        {
            EditorUtility.DisplayDialog(
                "Integration Guide",
                "INTEGRATION_GUIDE.md not found in Assets folder.\n\n" +
                "Please import the INTEGRATION_GUIDE.md file to your Assets folder.",
                "OK"
            );
        }
    }
    
    [MenuItem("Tools/Upgrade System/Quick Setup Wizard")]
    static void ShowQuickSetup()
    {
        EditorUtility.DisplayDialog(
            "Quick Setup Wizard",
            "QUICK SETUP STEPS:\n\n" +
            "1. Create empty GameObject → Add IntegratedUpgradeSystem\n" +
            "2. Add UpgradeGenerator + UpgradeSelectionUI to same GameObject\n" +
            "3. Create Canvas with upgrade selection panel\n" +
            "4. Create upgrade card prefab with UpgradeOptionUI\n" +
            "5. Create pickup prefab with IntegratedUpgradePickup\n" +
            "6. Add EnemyUpgradeDropper to enemy prefabs\n" +
            "7. Run verification: Tools → Upgrade System → Verify Integration\n\n" +
            "See INTEGRATION_GUIDE.md for detailed instructions!",
            "Got It!"
        );
    }
#endif
}
