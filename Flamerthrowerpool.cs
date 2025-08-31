using System.Collections.Generic;
using UnityEngine;

public class ProjectilePool : MonoBehaviour
{
    public static ProjectilePool Instance; // Singleton instance
    public GameObject projectilePrefab; // Prefab for the projectile
    public int initialPoolSize = 10; // Initial size of the pool

    private Queue<GameObject> projectilePool = new Queue<GameObject>();

    private void Awake()
    {
        // Singleton pattern to ensure only one instance exists
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return; // Don't continue setup if destroyed
        }

        // Pre-instantiate projectiles and add them to the pool as children
        for (int i = 0; i < initialPoolSize; i++)
        {
            GameObject projectile = Instantiate(projectilePrefab, transform);
            projectile.SetActive(false); // Set inactive initially
            projectilePool.Enqueue(projectile);
        }
    }

    // Get a projectile from the pool
    public GameObject GetPooledProjectile()
    {
        if (projectilePool.Count > 0)
        {
            GameObject projectile = projectilePool.Dequeue();
            projectile.SetActive(true); // Activate the projectile
            // Ensure it remains parented to the pool for organization
            projectile.transform.SetParent(transform);
            return projectile;
        }
        return null; // No available projectile in the pool
    }

    // Return a projectile back to the pool
    public void ReturnProjectile(GameObject projectile)
    {
        projectile.SetActive(false); // Deactivate the projectile
        projectile.transform.SetParent(transform); // Parent to pool for organization
        projectilePool.Enqueue(projectile); // Add it back to the pool
    }
}