using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Photon.Pun;
using System.Linq;
using System.Text;

public class JournalSystemPaged : MonoBehaviourPun
{
    [Header("3D Book Journal")]
    public GameObject bookModel;
    public Animator bookAnimator;
    public Canvas bookCanvas;
    public TMP_Text pageText;
    public TMP_Text pageNumberText;

    [Header("Controls")]
    public KeyCode openJournalKey = KeyCode.J;
    public KeyCode nextPageKey = KeyCode.RightArrow;
    public KeyCode prevPageKey = KeyCode.LeftArrow;

    [Header("3D HUD Arrow")]
    public Transform arrow3D;

    [Header("Player Movement")]
    public MonoBehaviour playerMovementScript;

    [Header("Text Typing Effect")]
    public float typeSpeed = 0.02f;

    [Header("Page System")]
    public string blankPagePlaceholder = "????";

    [Header("Pickup Popup")]
    public TMP_Text pickupPopupText;
    public Canvas popupCanvasOverride;
    public float popupShowDuration = 2.5f;
    public float popupFadeInDuration = 0.15f;
    public float popupFadeDuration = 0.75f;
    public string popupMessageFormat = "New Note {0} collected";

    [Header("Dynamic Spawn Settings")]
    public float dynamicScanInterval = 1f;

    private List<string> collectedEntryIDs = new List<string>();
    private Dictionary<string, string> allEntryTexts = new Dictionary<string, string>();
    private Dictionary<int, string> entryTextsById = new Dictionary<int, string>();
    private HashSet<int> collectedIds = new HashSet<int>();
    private HashSet<int> knownPageIds = new HashSet<int>();

    private int currentPage = 0;
    private int totalPages = 0;
    private int maxDiscoveredId = 0;

    private bool isJournalOpen = false;
    private bool isClosing = false;
    private bool isTurningPage = false;
    private Coroutine typingCoroutine;
    private bool isTyping = false;

    private Player playerScript;

    private List<JournalEntry> cachedEntries = new List<JournalEntry>();
    private bool cachedEntriesDirty = true;

    private Rigidbody rb;
    private RigidbodyConstraints rbOriginalConstraints;

    private float animCloseLength = 0.5f;
    private float animTurnRightLength = 0.5f;
    private float animTurnLeftLength = 0.5f;

    private Coroutine popupCoroutine;

    private float nextScanTime = 0f;

    private bool temporarilyActivatedBookCanvas = false;

    void Start()
    {
        if (bookCanvas != null) bookCanvas.gameObject.SetActive(false);
        if (bookModel != null) bookModel.SetActive(false);
        if (arrow3D != null) arrow3D.gameObject.SetActive(false);
        if (pickupPopupText != null) pickupPopupText.gameObject.SetActive(false);
        if (popupCanvasOverride != null) popupCanvasOverride.gameObject.SetActive(true);

        playerScript = GetComponent<Player>();

        rb = GetComponent<Rigidbody>();
        if (rb != null) rbOriginalConstraints = rb.constraints;

        CacheJournalEntries();
        CacheAnimationClipLengths();
        BuildOrRefreshEntryLookup();
        HideCollectedEntries();
        UpdatePageDisplayImmediate();
    }

    void Update()
    {
        if (!photonView.IsMine) return;

        if (Time.time >= nextScanTime)
        {
            nextScanTime = Time.time + dynamicScanInterval;
            if (ScanForNewEntries())
            {
                BuildOrRefreshEntryLookup();
                ClampCurrentPage();
                UpdatePageDisplayImmediate();
            }
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            TryPickupEntry();
        }

        if (Input.GetKeyDown(openJournalKey))
        {
            if (!isTurningPage)
                ToggleJournal();
        }

        if (isJournalOpen && totalPages > 0 && !isTurningPage)
        {
            if (Input.GetKeyDown(nextPageKey))
            {
                if (currentPage < totalPages - 1)
                    StartCoroutine(TurnPageCoroutine(true));
            }
            else if (Input.GetKeyDown(prevPageKey))
            {
                if (currentPage > 0)
                    StartCoroutine(TurnPageCoroutine(false));
            }
        }

        UpdateArrow3D();
    }

    void CacheJournalEntries()
    {
        cachedEntries.Clear();
#if UNITY_2020_1_OR_NEWER
        cachedEntries.AddRange(FindObjectsOfType<JournalEntry>(true));
#else
        cachedEntries.AddRange(FindObjectsOfType<JournalEntry>());
#endif
        cachedEntriesDirty = false;
    }

    void CacheAnimationClipLengths()
    {
        if (bookAnimator == null || bookAnimator.runtimeAnimatorController == null) return;
        foreach (var clip in bookAnimator.runtimeAnimatorController.animationClips)
        {
            string lname = clip.name.ToLower();
            if (lname.Contains("close")) animCloseLength = clip.length;
            else if (lname.Contains("right")) animTurnRightLength = clip.length;
            else if (lname.Contains("left")) animTurnLeftLength = clip.length;
        }
    }

    void BuildOrRefreshEntryLookup()
    {
        foreach (var entry in cachedEntries)
        {
            if (entry == null) continue;
            if (int.TryParse(entry.entryID, out int id) && id > 0)
            {
                if (!knownPageIds.Contains(id))
                {
                    knownPageIds.Add(id);
                    if (!entryTextsById.ContainsKey(id)) entryTextsById[id] = entry.entryText;
                }
                else
                {
                    entryTextsById[id] = entry.entryText;
                }
                if (id > maxDiscoveredId) maxDiscoveredId = id;
            }
        }
        totalPages = Mathf.Max(totalPages, maxDiscoveredId);
        ClampCurrentPage();
    }

    public void RefreshEntries()
    {
        CacheJournalEntries();
        BuildOrRefreshEntryLookup();
        UpdatePageDisplayImmediate();
    }

    bool ScanForNewEntries()
    {
        int previousMax = maxDiscoveredId;
        CacheJournalEntries();
        BuildOrRefreshEntryLookup();
        return maxDiscoveredId > previousMax;
    }

    void ToggleJournal()
    {
        if (isClosing) return;

        if (!isJournalOpen)
        {
            isJournalOpen = true;
            if (bookModel != null) bookModel.SetActive(true);
            if (bookCanvas != null) bookCanvas.gameObject.SetActive(true);
            if (arrow3D != null) arrow3D.gameObject.SetActive(true);

            if (bookAnimator != null) bookAnimator.SetBool("IsOpen", true);

            if (playerScript != null && playerScript.weaponTransform != null && playerScript.weaponActivated)
                playerScript.weaponTransform.gameObject.SetActive(false);

            FreezePlayerRigidbody();
            UpdatePageDisplayImmediate();
        }
        else
        {
            isJournalOpen = false;
            if (bookAnimator != null) bookAnimator.SetBool("IsOpen", false);
            if (arrow3D != null) arrow3D.gameObject.SetActive(false);
            StartCoroutine(WaitAndDisableBook());
            UnfreezePlayerRigidbody();
        }
    }

    void FreezePlayerRigidbody()
    {
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.constraints = RigidbodyConstraints.FreezePosition | RigidbodyConstraints.FreezeRotation;
        }
    }

    void UnfreezePlayerRigidbody()
    {
        if (rb != null) rb.constraints = rbOriginalConstraints;
    }

    IEnumerator WaitAndDisableBook()
    {
        isClosing = true;
        float maxWait = 2.5f;
        float waited = 0f;
        bool inCloseState = false;
        bool closeAnimFound = false;

        if (bookAnimator != null)
        {
            while (!inCloseState && waited < maxWait)
            {
                AnimatorStateInfo state = bookAnimator.GetCurrentAnimatorStateInfo(0);
                if (state.IsName("Close") || state.IsName("BookClose"))
                {
                    inCloseState = true;
                    closeAnimFound = true;
                    break;
                }
                waited += Time.deltaTime;
                yield return null;
            }

            waited = 0f;
            while (inCloseState && waited < maxWait)
            {
                AnimatorStateInfo state = bookAnimator.GetCurrentAnimatorStateInfo(0);
                if (state.normalizedTime >= 1f) break;
                waited += Time.deltaTime;
                yield return null;
            }
        }

        if (!closeAnimFound)
            yield return new WaitForSeconds(animCloseLength);

        if (bookCanvas != null) bookCanvas.gameObject.SetActive(false);
        if (bookModel != null) bookModel.SetActive(false);
        if (playerScript != null && playerScript.weaponTransform != null && playerScript.weaponActivated)
            playerScript.weaponTransform.gameObject.SetActive(true);

        isClosing = false;
    }

    IEnumerator TurnPageCoroutine(bool next)
    {
        if (isTurningPage) yield break;
        isTurningPage = true;

        if (bookAnimator != null)
        {
            bookAnimator.ResetTrigger("TurnPageRight");
            bookAnimator.ResetTrigger("TurnPageLeft");
            if (next) bookAnimator.SetTrigger("TurnPageRight");
            else bookAnimator.SetTrigger("TurnPageLeft");
        }

        float wait = next ? animTurnRightLength : animTurnLeftLength;
        yield return new WaitForSeconds(wait);

        if (!isJournalOpen) { isTurningPage = false; yield break; }

        if (next) NextPageImmediate(); else PrevPageImmediate();
        isTurningPage = false;
    }

    IEnumerator TypeCollectedPage(string fullText)
    {
        isTyping = true;
        pageText.text = "";
        StringBuilder sb = new StringBuilder(fullText.Length);
        for (int i = 0; i < fullText.Length; i++)
        {
            sb.Append(fullText[i]);
            pageText.text = sb.ToString();
            yield return new WaitForSeconds(typeSpeed);
        }
        pageText.text = fullText;
        isTyping = false;
    }

    IEnumerator PopupRoutine(string message, bool restoreBookCanvasAfter)
    {
        if (pickupPopupText == null)
            yield break;

        pickupPopupText.gameObject.SetActive(true);

        Color c = pickupPopupText.color;
        c.a = 0f;
        pickupPopupText.color = c;
        pickupPopupText.text = message;

        float fi = Mathf.Max(0.0001f, popupFadeInDuration);
        float tIn = 0f;
        while (tIn < fi)
        {
            tIn += Time.deltaTime;
            float a = Mathf.Clamp01(tIn / fi);
            c.a = a;
            pickupPopupText.color = c;
            yield return null;
        }
        c.a = 1f;
        pickupPopupText.color = c;
        yield return new WaitForSeconds(popupShowDuration);

        float fo = Mathf.Max(0f, popupFadeDuration);
        if (fo > 0f)
        {
            float tOut = 0f;
            while (tOut < fo)
            {
                tOut += Time.deltaTime;
                float a = 1f - Mathf.Clamp01(tOut / fo);
                c.a = a;
                pickupPopupText.color = c;
                yield return null;
            }
        }

        pickupPopupText.gameObject.SetActive(false);
        popupCoroutine = null;

        if (restoreBookCanvasAfter && bookCanvas != null && !isJournalOpen)
            bookCanvas.gameObject.SetActive(false);
        temporarilyActivatedBookCanvas = false;
    }

    void NextPageImmediate()
    {
        if (totalPages == 0) return;
        if (currentPage < totalPages - 1) { currentPage++; ShowPageContent(); }
    }

    void PrevPageImmediate()
    {
        if (totalPages == 0) return;
        if (currentPage > 0) { currentPage--; ShowPageContent(); }
    }

    void TryPickupEntry()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, 3f);
        foreach (var hit in hits)
        {
            var entry = hit.GetComponent<JournalEntry>();
            if (entry != null && !collectedEntryIDs.Contains(entry.entryID))
            {
                collectedEntryIDs.Add(entry.entryID);
                allEntryTexts[entry.entryID] = entry.entryText;

                if (int.TryParse(entry.entryID, out int id) && id > 0)
                {
                    collectedIds.Add(id);
                    entryTextsById[id] = entry.entryText;

                    if (!knownPageIds.Contains(id))
                    {
                        knownPageIds.Add(id);
                        if (id > maxDiscoveredId)
                        {
                            maxDiscoveredId = id;
                            totalPages = Mathf.Max(totalPages, maxDiscoveredId);
                        }
                    }

                    if (id - 1 >= 0)
                        currentPage = Mathf.Min(id - 1, totalPages - 1);
                }

                entry.gameObject.SetActive(false);

                GlobalGameEvents.EmitJournalCollected(entry.entryID);

                ShowPageContent();
                ShowPickupPopup(id: int.TryParse(entry.entryID, out int parsed) ? parsed : (int?)null);
                break;
            }
        }
    }

    void ShowPickupPopup(int? id)
    {
        if (pickupPopupText == null) return;

        string msg = id.HasValue ? string.Format(popupMessageFormat, id.Value) : "New Note collected";
        bool restoreBookCanvasAfter = false;

        if (popupCanvasOverride != null)
        {
            if (!popupCanvasOverride.gameObject.activeSelf) popupCanvasOverride.gameObject.SetActive(true);
        }
        else
        {
            if (bookCanvas != null && !bookCanvas.gameObject.activeSelf && pickupPopupText.transform.IsChildOf(bookCanvas.transform))
            {
                bookCanvas.gameObject.SetActive(true);
                temporarilyActivatedBookCanvas = true;
                restoreBookCanvasAfter = true;
            }
            else
            {
                Transform p = pickupPopupText.transform.parent;
                while (p != null && !p.gameObject.activeSelf)
                {
                    p.gameObject.SetActive(true);
                    p = p.parent;
                }
            }
        }

        if (popupCoroutine != null) StopCoroutine(popupCoroutine);
        popupCoroutine = StartCoroutine(PopupRoutine(msg, restoreBookCanvasAfter));
    }

    void HideCollectedEntries()
    {
        foreach (var entry in FindObjectsOfType<JournalEntry>())
            if (collectedEntryIDs.Contains(entry.entryID))
                entry.gameObject.SetActive(false);
    }

    void UpdatePageDisplayImmediate()
    {
        if (pageText == null) return;

        StopTypingIfRunning();

        if (totalPages == 0)
        {
            pageText.text = "<i>No journal entries in scene.</i>";
            if (pageNumberText != null) pageNumberText.text = "";
            return;
        }

        ClampCurrentPage();
        UpdatePageCounter();
        ShowPageContent();
    }

    void UpdatePageCounter()
    {
        if (pageNumberText != null)
            pageNumberText.text = $"Page {currentPage + 1}/{totalPages}";
    }

    void ShowPageContent()
    {
        StopTypingIfRunning();

        if (totalPages == 0)
        {
            pageText.text = "<i>No journal entries in scene.</i>";
            if (pageNumberText != null) pageNumberText.text = "";
            return;
        }

        ClampCurrentPage();
        UpdatePageCounter();

        int pageId = currentPage + 1;
        bool collected = collectedIds.Contains(pageId);

        if (!collected)
        {
            pageText.text = blankPagePlaceholder;
            return;
        }

        if (entryTextsById.TryGetValue(pageId, out string fullText))
            typingCoroutine = StartCoroutine(TypeCollectedPage(fullText));
        else
            pageText.text = blankPagePlaceholder;
    }

    void ClampCurrentPage()
    {
        if (currentPage < 0) currentPage = 0;
        if (currentPage > Mathf.Max(0, totalPages - 1))
            currentPage = Mathf.Max(0, totalPages - 1);
    }

    void StopTypingIfRunning()
    {
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
            isTyping = false;
        }
    }

    void UpdateArrow3D()
    {
        if (arrow3D == null || bookModel == null) return;

        if (cachedEntriesDirty)
        {
            CacheJournalEntries();
            BuildOrRefreshEntryLookup();
        }

        JournalEntry nextEntry = FindNearestUncollectedCached();
        if (nextEntry == null)
        {
            arrow3D.gameObject.SetActive(false);
            return;
        }

        arrow3D.gameObject.SetActive(true);

        Vector3 from = bookModel.transform.position;
        Vector3 to = nextEntry.transform.position;
        Vector3 direction = (to - from).normalized;
        if (direction.sqrMagnitude < 0.0001f) return;

        Vector3 bookUp = bookModel.transform.up;

        Vector3 flattenedDir = Vector3.ProjectOnPlane(direction, bookUp);
        if (flattenedDir.sqrMagnitude < 0.000001f)
        {
            flattenedDir = Vector3.ProjectOnPlane(bookModel.transform.forward, bookUp);
            if (flattenedDir.sqrMagnitude < 0.000001f) flattenedDir = Vector3.right;
        }
        flattenedDir.Normalize();

        float verticalAngle;
        Vector3 crossAxis = Vector3.Cross(flattenedDir, bookUp);
        if (crossAxis.sqrMagnitude < 0.000001f) { verticalAngle = 0f; crossAxis = bookModel.transform.right; }
        else { verticalAngle = Vector3.SignedAngle(flattenedDir, direction, crossAxis); }
        verticalAngle = Mathf.Clamp(verticalAngle, -60f, 60f);

        Quaternion yaw = Quaternion.LookRotation(flattenedDir, bookUp);
        Quaternion pitch = Quaternion.AngleAxis(verticalAngle, bookModel.transform.right);
        arrow3D.rotation = yaw * pitch;
    }

    JournalEntry FindNearestUncollectedCached()
    {
        float minDist = float.MaxValue;
        JournalEntry nearest = null;
        foreach (var entry in cachedEntries)
        {
            if (entry == null) continue;
            if (!collectedEntryIDs.Contains(entry.entryID) && entry.gameObject.activeSelf)
            {
                float dist = Vector3.Distance(transform.position, entry.transform.position);
                if (dist < minDist) { minDist = dist; nearest = entry; }
            }
        }
        return nearest;
    }

}