using System;

public static class GlobalGameEvents
{
    public static event Action<int, int> EnemyKilled;     // (killerActorNumber, basePoints)
    public static event Action BossDefeated;
    public static event Action<string> JournalCollected;  // entryID
    public static event Action<string> SecretFound;       // secretID
    public static event Action LevelCleared;

    public static event Action<int> PlayerDied;           // actorNumber
    public static event Action<int, int> PlayerTookDamage;// (actorNumber, amount)

    public static void EmitEnemyKilled(int killerActor, int basePoints) => EnemyKilled?.Invoke(killerActor, basePoints);
    public static void EmitBossDefeated() => BossDefeated?.Invoke();
    public static void EmitJournalCollected(string id) => JournalCollected?.Invoke(id);
    public static void EmitSecretFound(string id) => SecretFound?.Invoke(id);
    public static void EmitLevelCleared() => LevelCleared?.Invoke();
    public static void EmitPlayerDied(int actor) => PlayerDied?.Invoke(actor);
    public static void EmitPlayerTookDamage(int actor, int amount) => PlayerTookDamage?.Invoke(actor, amount);
}