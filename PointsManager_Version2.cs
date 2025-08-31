using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public class PointsManager : MonoBehaviourPunCallbacks, IOnEventCallback
{
    public static PointsManager Instance { get; private set; }

    [Header("Base values")]
    public int enemyKillBase = 10;
    public int bossKillBase = 250;
    public int journalBase = 25;
    public int secretBase = 250;
    public int levelClearBase = 200;

    [Header("Splits")]
    [Range(0, 1f)] public float killKillerShare = 0.7f;      // 70% killer, 30% team

    [Header("Penalty")]
    public float damagePenaltyPer100 = 0.002f;               // 0.2% per 100 dmg
    public float maxPenaltyPerEncounter = 0.10f;

    [Header("Debug")]
    public bool verbose = false;

    public int TeamPoints { get; private set; } = 0;
    public int LocalSpendablePoints { get; private set; } = 0;

    // ADDED: Public method for adding points for testing/debug/UI
    public void AddPoints(int amount)
    {
        LocalSpendablePoints += amount;
    }

    // track per-actor earned and penalties
    private readonly System.Collections.Generic.Dictionary<int, int> personal = new();
    private readonly System.Collections.Generic.Dictionary<int, float> encounterPenalty = new();

    enum Ev : byte
    {
        EnemyKilled = 1,
        BossDefeated = 2,
        Journal = 3,
        Secret = 4,
        LevelClear = 5,
        DamageTaken = 6,
        Snapshot = 7
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
        PhotonNetwork.AddCallbackTarget(this);
        GlobalGameEvents.EnemyKilled += OnEnemyKilledLocal;
        GlobalGameEvents.BossDefeated += OnBossDefeatedLocal;
        GlobalGameEvents.JournalCollected += OnJournalLocal;
        GlobalGameEvents.SecretFound += OnSecretLocal;
        GlobalGameEvents.LevelCleared += OnLevelClearLocal;
        GlobalGameEvents.PlayerTookDamage += OnDamageLocal;
    }

    void OnDisable()
    {
        PhotonNetwork.RemoveCallbackTarget(this);
        GlobalGameEvents.EnemyKilled -= OnEnemyKilledLocal;
        GlobalGameEvents.BossDefeated -= OnBossDefeatedLocal;
        GlobalGameEvents.JournalCollected -= OnJournalLocal;
        GlobalGameEvents.SecretFound -= OnSecretLocal;
        GlobalGameEvents.LevelCleared -= OnLevelClearLocal;
        GlobalGameEvents.PlayerTookDamage -= OnDamageLocal;
        if (Instance == this) Instance = null;
    }

    public void OnEvent(EventData photonEvent)
    {
        byte code = photonEvent.Code;

        if (code == (byte)Ev.Snapshot)
        {
            object[] data = (object[])photonEvent.CustomData;
            TeamPoints = (int)data[0];
            LocalSpendablePoints = (int)data[1];
            return;
        }

        if (!PhotonNetwork.IsMasterClient) return;

        switch ((Ev)code)
        {
            case Ev.EnemyKilled:
            {
                var arr = (object[])photonEvent.CustomData;
                int killer = (int)arr[0];
                int basePts = (int)arr[1];

                float pm = PlayerSpawner.Instance != null ? PlayerSpawner.Instance.PointsMult : 1f;
                int teamShare = Mathf.RoundToInt(basePts * (1f - killKillerShare));
                int killerShare = Mathf.RoundToInt(basePts * killKillerShare);

                int toTeam = Mathf.RoundToInt(teamShare * pm);
                int toKiller = Mathf.RoundToInt(killerShare * pm);

                AddPersonal(killer, toKiller);
                TeamPoints += toTeam;
                BroadcastSnapshot();
                if (verbose) Debug.Log($"[Points] Enemy kill: killer {killer}+{toKiller}, team+{toTeam}");
                break;
            }
            case Ev.BossDefeated:
            {
                var arr = (object[])photonEvent.CustomData;
                int basePts = (int)arr[0];
                float pm = PlayerSpawner.Instance != null ? PlayerSpawner.Instance.PointsMult : 1f;
                int award = Mathf.RoundToInt(basePts * pm);
                TeamPoints += award;
                BroadcastSnapshot();
                break;
            }
            case Ev.Journal:
            {
                var arr = (object[])photonEvent.CustomData;
                int basePts = (int)arr[1];
                float pm = PlayerSpawner.Instance != null ? PlayerSpawner.Instance.PointsMult : 1f;
                TeamPoints += Mathf.RoundToInt(basePts * pm);
                BroadcastSnapshot();
                break;
            }
            case Ev.Secret:
            {
                var arr = (object[])photonEvent.CustomData;
                int basePts = (int)arr[1];
                float pm = PlayerSpawner.Instance != null ? PlayerSpawner.Instance.PointsMult : 1f;
                TeamPoints += Mathf.RoundToInt(basePts * pm);
                BroadcastSnapshot();
                break;
            }
            case Ev.LevelClear:
            {
                var arr = (object[])photonEvent.CustomData;
                int basePts = (int)arr[0];
                float pm = PlayerSpawner.Instance != null ? PlayerSpawner.Instance.PointsMult : 1f;
                TeamPoints += Mathf.RoundToInt(basePts * pm);
                encounterPenalty.Clear();
                BroadcastSnapshot();
                break;
            }
            case Ev.DamageTaken:
            {
                var arr = (object[])photonEvent.CustomData;
                int actor = (int)arr[0];
                int amount = (int)arr[1];
                float add = (amount / 100f) * damagePenaltyPer100;
                if (!encounterPenalty.ContainsKey(actor)) encounterPenalty[actor] = 0f;
                encounterPenalty[actor] = Mathf.Clamp(encounterPenalty[actor] + add, 0f, maxPenaltyPerEncounter);
                ApplyPenalty(actor);
                BroadcastSnapshot();
                break;
            }
        }
    }

    void AddPersonal(int actor, int delta)
    {
        if (!personal.ContainsKey(actor)) personal[actor] = 0;
        personal[actor] += delta;
        if (PhotonNetwork.LocalPlayer != null && PhotonNetwork.LocalPlayer.ActorNumber == actor)
            LocalSpendablePoints += delta;
    }

    void ApplyPenalty(int actor)
    {
        float frac = encounterPenalty.TryGetValue(actor, out var v) ? v : 0f;
        int cur = personal.TryGetValue(actor, out var p) ? p : 0;
        int penalty = Mathf.RoundToInt(cur * frac);
        if (penalty <= 0) return;
        personal[actor] = Mathf.Max(0, cur - penalty);
        if (PhotonNetwork.LocalPlayer != null && PhotonNetwork.LocalPlayer.ActorNumber == actor)
            LocalSpendablePoints = Mathf.Max(0, LocalSpendablePoints - penalty);
    }

    void BroadcastSnapshot()
    {
        var options = new RaiseEventOptions { Receivers = ReceiverGroup.All };
        PhotonNetwork.RaiseEvent((byte)Ev.Snapshot, new object[] { TeamPoints, LocalSpendablePoints }, options, SendOptions.SendReliable);
    }

    // Local -> Master
    void Raise(byte code, object[] payload)
    {
        var opt = new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient };
        PhotonNetwork.RaiseEvent(code, payload, opt, SendOptions.SendReliable);
    }

    void OnEnemyKilledLocal(int killer, int basePts) => Raise((byte)Ev.EnemyKilled, new object[] { killer, basePts });
    void OnBossDefeatedLocal() => Raise((byte)Ev.BossDefeated, new object[] { bossKillBase });
    void OnJournalLocal(string id) => Raise((byte)Ev.Journal, new object[] { id, journalBase });
    void OnSecretLocal(string id) => Raise((byte)Ev.Secret, new object[] { id, secretBase });
    void OnLevelClearLocal() => Raise((byte)Ev.LevelClear, new object[] { levelClearBase });
    void OnDamageLocal(int actor, int amount) => Raise((byte)Ev.DamageTaken, new object[] { actor, amount });

    // UI hook
    public void SetLocalSpendablePoints(int v) => LocalSpendablePoints = Mathf.Max(0, v);
}