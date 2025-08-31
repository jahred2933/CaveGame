using UnityEngine;
using Photon.Pun;

public class RockHealth : MonoBehaviourPunCallbacks, IPunObservable
{
    public int maxHealth = 100;
    private int currentHealth;

    public GameObject[] objectsToSpawn;
    public int spawnChancePercentage = 15;
    public float descentSpeed = 2.0f;
    public float rotationSpeed = 100f;

    public GameObject particleSystem1;
    public GameObject particleSystem2;

    private bool isDescending = false;
    private float descentThreshold;

    [SerializeField] private Renderer renderer1;
    [SerializeField] private Renderer renderer2;
    private MaterialPropertyBlock propBlock;

    void Start()
    {
        currentHealth = maxHealth;
        descentThreshold = transform.position.y - 11f;
        propBlock = new MaterialPropertyBlock();
        UpdateDissolve(0f);
    }

    void UpdateDissolve(float value)
    {
        if (renderer1)
        {
            renderer1.GetPropertyBlock(propBlock);
            propBlock.SetFloat("_Dissolve", value);
            renderer1.SetPropertyBlock(propBlock);
        }
        if (renderer2)
        {
            renderer2.GetPropertyBlock(propBlock);
            propBlock.SetFloat("_Dissolve", value);
            renderer2.SetPropertyBlock(propBlock);
        }
    }

    [PunRPC]
    public void RequestDamage(int damageAmount, PhotonMessageInfo info)
    {
        if (!photonView.IsMine)
            return;

        currentHealth -= damageAmount;
        float dissolveValue = 1f - Mathf.Clamp01((float)currentHealth / maxHealth);
        UpdateDissolve(dissolveValue);

        if (currentHealth <= 0 && !isDescending)
        {
            isDescending = true;

            if (PhotonNetwork.IsMasterClient)
            {
                TrySpawnObjects();
                photonView.RPC("DisappearImmediately", RpcTarget.All); // CHANGED
            }
        }
    }

    private void TrySpawnObjects()
    {
        bool shouldSpawnObjects = Random.Range(0, 100) < spawnChancePercentage;
        if (shouldSpawnObjects && objectsToSpawn.Length > 0)
        {
            int randomIndex = Random.Range(0, objectsToSpawn.Length);
            PhotonNetwork.Instantiate(objectsToSpawn[randomIndex].name, transform.position, Quaternion.identity);
        }
    }

    private void DetachParticleSystems()
    {
        if (particleSystem1 != null)
            particleSystem1.transform.SetParent(null, true);
        if (particleSystem2 != null)
            particleSystem2.transform.SetParent(null, true);
    }

    [PunRPC]
    private void DisappearImmediately() // CHANGED
    {
        DetachParticleSystems();
        if (PhotonNetwork.IsMasterClient)
            PhotonNetwork.Destroy(gameObject);
    }

    // REMOVED: DescendAndDestroy coroutine and BeginDescent method

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
            stream.SendNext(currentHealth);
        else
            currentHealth = (int)stream.ReceiveNext();
    }
}