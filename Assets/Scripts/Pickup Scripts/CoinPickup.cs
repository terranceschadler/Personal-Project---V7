using UnityEngine;

public class CoinPickup : PickupBase
{
    [Header("Coin")]
    [Tooltip("How many coins this pickup gives.")]
    public int coinValue = 1;

    [Tooltip("Score granted in addition to coins when collected.")]
    public int scoreOnCollect = 5;

    protected override bool ApplyEffect(GameObject collector)
    {
        var gm = GameManager.Instance;
        if (gm != null)
        {
            if (coinValue > 0) gm.AddCoins(coinValue);
            if (scoreOnCollect > 0) gm.AddScore(scoreOnCollect);
        }
        return true;
    }
}
