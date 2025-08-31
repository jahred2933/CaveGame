using UnityEngine;

public class RotationWatcher : MonoBehaviour
{
    public RotatableObject[] rotatableObjects; // List of all rotatable objects
    public Animator animator; // Animator to enable

    void Update()
    {
        CheckObjects();
    }

    void CheckObjects()
    {
        // Check if any object is not solved
        bool anyNotSolved = false;

        foreach (var obj in rotatableObjects)
        {
            if (!obj.Solved)
            {
                anyNotSolved = true;
                break;
            }
        }

        if (!anyNotSolved)
        {
            if (!animator.enabled)
            {
                animator.enabled = true;
            }
        }
    }
}
