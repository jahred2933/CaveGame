// Assuming other parts of the class remain unchanged.

private bool hasSpawnedEnemies; // Track if spawning routines have run

public void ResetSpawnFlags() {
    // Other reset logic...
    hasSpawnedEnemies = false;
}

public void TryStartSpawning() {
    if (hasSpawnedEnemies) return; // Check if enemies have already spawned
    // Existing spawning logic...
    hasSpawnedEnemies = true; // Set to true after running
}

protected override void OnMasterClientSwitched() {
    // Existing logic...
    if (newMaster && !hasSpawnedEnemies) {
        DelayedTryStartSpawning(); // Call if conditions are met
    }
}