using UnityEngine;
using System.Collections;

public class RotatableObject : MonoBehaviour
{
    public Vector3 correctRotation;        // The correct rotation for this object
    public float rotationStep = 90f;       // Degrees to rotate per interaction
    public float rotationTolerance = 5f;   // Tolerance for rotation comparison
    public float spawnDelay = 2f;          // Seconds to wait for player/camera to spawn

    private bool isCorrectlyRotated = false;
    public bool Solved { get; private set; }   // Indicates if the object is solved
    public bool IsCorrectlyRotated => isCorrectlyRotated;

    private Camera cam;
    private bool isReady = false;

    IEnumerator Start()
    {
        // wait a bit for the player (and its camera) to finish spawning
        yield return new WaitForSeconds(spawnDelay);

        cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError($"RotatableObject: MainCamera not found after waiting {spawnDelay} seconds.");
        }
        else
        {
            isReady = true;
        }
    }

    void Update()
    {
        if (!isReady)
            return;

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit) && hit.transform == transform)
            {
                RotateObject();
                CheckRotation();
            }
        }
    }

    void RotateObject()
    {
        transform.Rotate(0, 0, rotationStep);
    }

    void CheckRotation()
    {
        Quaternion currentRotation = transform.rotation;
        Quaternion targetRotation = Quaternion.Euler(correctRotation);

        float angle = Quaternion.Angle(currentRotation, targetRotation);
        bool isWithinTolerance = angle <= rotationTolerance;

        if (isWithinTolerance != isCorrectlyRotated)
        {
            isCorrectlyRotated = isWithinTolerance;
            Solved = isCorrectlyRotated;
        }
    }
}
