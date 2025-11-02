using UnityEngine;

[DisallowMultipleComponent]
public class BonePlate : MonoBehaviour
{
    [Header("Owner / Identity")]
    public BoneforgeTitanBoss owner;
    [HideInInspector] public int index;

    [Header("Plate Stats")]
    public float plateHealth = 80f;

    [Header("Break FX (optional)")]
    public ParticleSystem breakVfx;
    public AudioClip breakSfx;

    // --- Exposed state used by BoneforgeTitanBoss ---
    public bool IsBroken => _broken;
    public Vector3 WorldPosition => transform.position;

    // --- Internals ---
    private float _hp;
    private bool _broken;
    private Collider[] _cols;
    private Renderer[] _rends;
    private AudioSource _sfx;

    private void Awake()
    {
        _cols = GetComponentsInChildren<Collider>(true);
        _rends = GetComponentsInChildren<Renderer>(true);
        _sfx = GetComponent<AudioSource>();
    }

    private void OnEnable()
    {
        _hp = plateHealth;
        _broken = false;
        SetColliders(true);
        SetVisuals(true);
    }

    private void Break()
    {
        if (_broken) return;
        _broken = true;

        if (breakVfx) Instantiate(breakVfx, transform.position, Quaternion.identity);
        if (_sfx && breakSfx) _sfx.PlayOneShot(breakSfx, 0.9f);

        SetColliders(false);
        SetVisuals(false);

        if (owner) owner.NotifyPlateBroken(this);
        else Debug.LogWarning("[BonePlate] Owner not set; cannot notify.", this);
    }

    private void SetColliders(bool en)
    {
        if (_cols == null) return;
        for (int i = 0; i < _cols.Length; i++) if (_cols[i]) _cols[i].enabled = en;
    }

    private void SetVisuals(bool en)
    {
        if (_rends == null) return;
        for (int i = 0; i < _rends.Length; i++) if (_rends[i]) _rends[i].enabled = en;
    }

    // --- Damage entry points ---
    public void ApplyDamageFrom(object payload)
    {
        if (_broken) return;

        float amt = 0f;
        switch (payload)
        {
            case BossEnemy.DamageEnvelope env: amt = env.amount; break;
            case System.ValueTuple<float, GameObject> tup: amt = tup.Item1; break;
            case float f: amt = f; break;
            default: return;
        }

        TakeDamage(amt);
    }

    public void TakeDamage(float amount)
    {
        if (_broken || amount <= 0f) return;

        _hp -= amount;
        if (_hp <= 0f) Break();
    }
}
