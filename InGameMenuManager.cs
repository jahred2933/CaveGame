using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.SceneManagement;
using UnityEngine.Audio;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(PhotonView))]
public class InGameMenuManager : MonoBehaviourPunCallbacks
{
    [Header("Menu & Navigation")]
    public GameObject menuUI;
    public GameObject upgradeMenuUI; // Assign in Inspector if you use an upgrade menu
    public Button quitToMenuButton;
    public Button quitGameButton;

    [Header("Lobby Info")]
    public TMP_Text lobbyCodeText;
    public TMP_Dropdown playerDropdown;
    public Button kickButton;

    [Header("Audio Settings")]
    public AudioMixer audioMixer;
    public Slider volumeSlider;

    [Header("Graphics Settings")]
    public TMP_Dropdown resolutionDropdown;
    public TMP_Dropdown qualityDropdown;
    public Toggle fullscreenToggle;

    [Header("Upgrades (Optional)")]
    public Button btnMaxHealth;
    public Button btnDamage;
    public Button btnStamina;
    public Button btnStaminaRegen;
    public Button btnHealthRegen;
    public Button btnMoveSpeed;
    public Button btnExtraLife;
    public TMP_Text pointsText;

    [Header("Testing")]
    public int testPoints = 0; // Set in Inspector for easy test points

    private List<Photon.Realtime.Player> playersList = new List<Photon.Realtime.Player>();
    private Resolution[] allResolutions;
    private PhotonView pv;

    void Start()
    {
        pv = PhotonView.Get(this);
        PhotonNetwork.AutomaticallySyncScene = true;

        menuUI.SetActive(false);
        if (upgradeMenuUI != null)
            upgradeMenuUI.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        quitToMenuButton.onClick.AddListener(OnQuitToMenuPressed);
        quitGameButton.onClick.AddListener(OnQuitGamePressed);
        kickButton.onClick.AddListener(KickSelectedPlayer);
        playerDropdown.onValueChanged.AddListener(_ => UpdateKickButton());

        Hook(btnMaxHealth, "MaxHealth");
        Hook(btnDamage, "Damage");
        Hook(btnStamina, "Stamina");
        Hook(btnStaminaRegen, "StaminaRegen");
        Hook(btnHealthRegen, "HealthRegen");
        Hook(btnMoveSpeed, "MoveSpeed");
        Hook(btnExtraLife, "ExtraLife");

        float savedVol = PlayerPrefs.GetFloat("Volume", 0.75f);
        volumeSlider.value = savedVol;
        volumeSlider.onValueChanged.AddListener(SetVolume);
        SetVolume(savedVol);

        allResolutions = Screen.resolutions;
        PopulateResolutionDropdown();
        ApplySavedResolution();

        qualityDropdown.ClearOptions();
        qualityDropdown.AddOptions(QualitySettings.names.ToList());
        int savedQuality = PlayerPrefs.GetInt("QualityLevel", QualitySettings.GetQualityLevel());
        qualityDropdown.value = savedQuality;
        qualityDropdown.onValueChanged.AddListener(SetQuality);
        QualitySettings.SetQualityLevel(savedQuality);

        bool isFullscreen = PlayerPrefs.GetInt("Fullscreen", Screen.fullScreen ? 1 : 0) == 1;
        fullscreenToggle.isOn = isFullscreen;
        fullscreenToggle.onValueChanged.AddListener(SetFullscreen);
        Screen.fullScreen = isFullscreen;

        UpdateUpgradeButtonTexts();

        // Give test points for easy testing
        if (testPoints > 0 && PointsManager.Instance != null)
        {
            PointsManager.Instance.AddPoints(testPoints);
            Debug.Log($"[Menu] Test points added: +{testPoints}, NewPoints={PointsManager.Instance.LocalSpendablePoints}");
        }

        Debug.Log($"[Menu] Start. InRoom={PhotonNetwork.InRoom}, IsMaster={PhotonNetwork.IsMasterClient}, " +
                  $"PlayerSpawner={(PlayerSpawner.Instance ? "OK" : "NULL")}, PointsManager={(PointsManager.Instance ? "OK" : "NULL")}, " +
                  $"EventSystem={(FindObjectOfType<UnityEngine.EventSystems.EventSystem>() ? "OK" : "NULL")}");

        Debug.Log($"[Menu] Buttons assigned: " +
                  $"MaxHealth={(btnMaxHealth ? btnMaxHealth.name : "NULL")}, " +
                  $"Damage={(btnDamage ? btnDamage.name : "NULL")}, " +
                  $"Stamina={(btnStamina ? btnStamina.name : "NULL")}, " +
                  $"StaminaRegen={(btnStaminaRegen ? btnStaminaRegen.name : "NULL")}, " +
                  $"HealthRegen={(btnHealthRegen ? btnHealthRegen.name : "NULL")}, " +
                  $"MoveSpeed={(btnMoveSpeed ? btnMoveSpeed.name : "NULL")}, " +
                  $"ExtraLife={(btnExtraLife ? btnExtraLife.name : "NULL")}");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // If upgrade menu is open, close it
            if (upgradeMenuUI != null && upgradeMenuUI.activeSelf)
            {
                Debug.Log("[Menu] ESC pressed: closing Upgrade Menu");
                upgradeMenuUI.SetActive(false);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                SetLookEnabled(false);
                return;
            }
            // If options menu is open, close it
            if (menuUI.activeSelf)
            {
                Debug.Log("[Menu] ESC pressed: closing Options Menu");
                menuUI.SetActive(false);
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                SetLookEnabled(true);
                return;
            }
            // Open options menu if not already open
            Debug.Log("[Menu] ESC pressed: opening Options Menu");
            menuUI.SetActive(true);
            var menuCanvas = menuUI.GetComponent<Canvas>();
            foreach (var canvas in FindObjectsOfType<Canvas>())
            {
                var gr = canvas.GetComponent<GraphicRaycaster>();
                if (gr != null)
                    gr.enabled = (canvas == menuCanvas) || !menuUI.activeSelf;
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SetLookEnabled(false);
            RefreshMenu();
            UpdateUpgradeButtonTexts();
        }

        if (pointsText != null && PointsManager.Instance != null)
            pointsText.text = $"Points: {PointsManager.Instance.LocalSpendablePoints}";
    }

    void SetLookEnabled(bool value)
    {
        // Find local player (spawned) and enable/disable their FirstPersonLook
        var localPlayer = GameObject.FindGameObjectsWithTag("Player")
            .FirstOrDefault(go => go.GetComponent<PhotonView>()?.IsMine == true);
        if (localPlayer)
        {
            var look = localPlayer.GetComponent<FirstPersonLook>();
            if (look) look.enabled = value;
            Debug.Log($"[Menu] SetLookEnabled({value}) on '{localPlayer.name}' (hasLook={(look!=null)})");
        }
        else
        {
            Debug.LogWarning("[Menu] SetLookEnabled: local player not found.");
        }
    }

    void RefreshMenu()
    {
        lobbyCodeText.text = PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.Name : "N/A";
        playersList = PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.Players.Values.ToList() : new List<Photon.Realtime.Player>();

        var names = playersList.Select(p => p.NickName).ToList();
        playerDropdown.ClearOptions();
        playerDropdown.AddOptions(names);
        if (names.Count > 0) playerDropdown.value = 0;

        Debug.Log($"[Menu] RefreshMenu: InRoom={PhotonNetwork.InRoom}, Players={names.Count}, Room='{lobbyCodeText.text}'");

        UpdateKickButton();
        UpdateUpgradeButtonTexts();
    }

    void UpdateKickButton()
    {
        if (!PhotonNetwork.IsMasterClient || playersList.Count == 0)
        {
            kickButton.interactable = false;
            return;
        }
        var selected = playersList[playerDropdown.value];
        kickButton.interactable = selected != PhotonNetwork.LocalPlayer;
    }

    void KickSelectedPlayer()
    {
        if (!PhotonNetwork.IsMasterClient || playersList.Count == 0) return;
        var target = playersList[playerDropdown.value];
        Debug.Log($"[Menu] KickSelectedPlayer: Attempting to kick '{target.NickName}' ({target.ActorNumber})");
        pv.RPC(nameof(RPC_KickMe), target);
        RefreshMenu();
    }

    [PunRPC]
    void RPC_KickMe()
    {
        Debug.Log("[Menu] RPC_KickMe received: leaving room and loading Menu scene.");
        PhotonNetwork.LeaveRoom();
        SceneManager.LoadScene("Menu");
    }

    void OnQuitToMenuPressed()
    {
        Debug.Log("[Menu] Quit to Menu pressed.");
        PhotonNetwork.LeaveRoom();
        SceneManager.LoadScene("Menu");
    }

    void OnQuitGamePressed()
    {
        Debug.Log("[Menu] Quit Game pressed.");
        PhotonNetwork.Disconnect();
        Application.Quit();
    }

    public void SetVolume(float v)
    {
        float vol = Mathf.Max(v, 0.0001f);
        audioMixer.SetFloat("Volume", Mathf.Log10(vol) * 20f);
        PlayerPrefs.SetFloat("Volume", vol);
        Debug.Log($"[Menu] Volume set to {vol:0.00}");
    }

    public void SetResolution(int idx)
    {
        string[] parts = resolutionDropdown.options[idx].text.Split('x');
        int w = int.Parse(parts[0].Trim());
        int h = int.Parse(parts[1].Trim());
        Screen.SetResolution(w, h, Screen.fullScreen);
        PlayerPrefs.SetInt("ResolutionWidth", w);
        PlayerPrefs.SetInt("ResolutionHeight", h);
        PlayerPrefs.SetInt("ResolutionDropdownIndex", idx);
        Debug.Log($"[Menu] Resolution set to {w}x{h}");
    }

    private void ApplySavedResolution()
    {
        int savedWidth = PlayerPrefs.GetInt("ResolutionWidth", Screen.currentResolution.width);
        int savedHeight = PlayerPrefs.GetInt("ResolutionHeight", Screen.currentResolution.height);
        int dropdownIndex = PlayerPrefs.GetInt("ResolutionDropdownIndex", 0);

        Screen.SetResolution(savedWidth, savedHeight, Screen.fullScreen);
        if (dropdownIndex >= 0 && dropdownIndex < resolutionDropdown.options.Count)
        {
            resolutionDropdown.value = dropdownIndex;
            resolutionDropdown.RefreshShownValue();
        }
        Debug.Log($"[Menu] ApplySavedResolution -> {savedWidth}x{savedHeight} (idx {dropdownIndex})");
    }

    private void PopulateResolutionDropdown()
    {
        var resOptions = new List<string>();
        var seen = new HashSet<string>();
        int dropdownIndex = 0;
        int matchWidth = PlayerPrefs.GetInt("ResolutionWidth", Screen.currentResolution.width);
        int matchHeight = PlayerPrefs.GetInt("ResolutionHeight", Screen.currentResolution.height);

        for (int i = 0; i < allResolutions.Length; i++)
        {
            var res = allResolutions[i];
            string opt = $"{res.width} x {res.height}";
            if (seen.Add(opt))
            {
                resOptions.Add(opt);
                if (res.width == matchWidth && res.height == matchHeight)
                    dropdownIndex = resOptions.Count - 1;
            }
        }

        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(resOptions);
        resolutionDropdown.value = dropdownIndex;
        resolutionDropdown.RefreshShownValue();
        resolutionDropdown.onValueChanged.AddListener(SetResolution);

        Debug.Log($"[Menu] PopulateResolutionDropdown: {resOptions.Count} unique resolutions. Selected idx={dropdownIndex}");
    }

    public void SetQuality(int q)
    {
        QualitySettings.SetQualityLevel(q);
        PlayerPrefs.SetInt("QualityLevel", q);
        Debug.Log($"[Menu] Quality set to index {q} ('{QualitySettings.names[q]}').");
    }

    public void SetFullscreen(bool fs)
    {
        Screen.fullScreen = fs;
        PlayerPrefs.SetInt("Fullscreen", fs ? 1 : 0);
        Debug.Log($"[Menu] Fullscreen set to {fs}");
    }

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        Debug.Log($"[Menu] OnPlayerEnteredRoom: '{newPlayer.NickName}' ({newPlayer.ActorNumber})");
        if (menuUI.activeSelf) RefreshMenu();
    }

    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        Debug.Log($"[Menu] OnPlayerLeftRoom: '{otherPlayer.NickName}' ({otherPlayer.ActorNumber})");
        if (menuUI.activeSelf) RefreshMenu();
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        Debug.Log($"[Menu] OnMasterClientSwitched: NewMaster='{newMasterClient.NickName}' ({newMasterClient.ActorNumber})");
        if (menuUI.activeSelf) RefreshMenu();
    }

    // NEW: make upgrade UI update the moment we join a room
    public override void OnJoinedRoom()
    {
        RefreshMenu();
        UpdateUpgradeButtonTexts();
    }

    // ---------- Upgrades UI wiring ----------
    void Hook(Button b, string key)
    {
        if (b == null)
        {
            Debug.LogWarning($"[Menu] Hook: Button for '{key}' is NULL (not assigned in Inspector).");
            return;
        }
        b.onClick.AddListener(() => TryBuy(key));
        Debug.Log($"[Menu] Hooked button '{b.name}' to upgrade key '{key}'.");
    }

    void TryBuy(string key)
    {
        int ptsNow = PointsManager.Instance != null ? PointsManager.Instance.LocalSpendablePoints : -1;
        Debug.Log($"[Menu] TryBuy pressed for key='{key}'. InRoom={PhotonNetwork.InRoom}. PlayerSpawner={(PlayerSpawner.Instance ? "OK" : "NULL")}. Points={ptsNow}");

        if (PlayerSpawner.Instance == null)
        {
            Debug.LogWarning("[Menu] TryBuy aborted: PlayerSpawner.Instance is NULL.");
            return;
        }

        bool ok = PlayerSpawner.Instance.TryPurchaseUpgrade(key, out var msg);
        Debug.Log($"[Menu] TryPurchaseUpgrade('{key}') -> {ok} msg='{msg}'");

        UpdateUpgradeButtonTexts();
        Debug.Log("[Menu] UI refreshed after purchase attempt.");
    }

    void UpdateUpgradeButtonTexts()
    {
        if (PlayerSpawner.Instance == null)
        {
            Debug.LogWarning("[Menu] UpdateUpgradeButtonTexts aborted: PlayerSpawner.Instance is NULL.");
            return;
        }
        var prof = PlayerSpawner.Instance.GetLocalProfile();

        if (prof == null)
        {
            Debug.LogWarning("[Menu] UpdateUpgradeButtonTexts aborted: local PlayerProfile is NULL (likely not in room yet).");
            return;
        }

        SetUpgradeButtonText(btnMaxHealth, "MaxHealth", prof);
        SetUpgradeButtonText(btnDamage, "Damage", prof);
        SetUpgradeButtonText(btnStamina, "Stamina", prof);
        SetUpgradeButtonText(btnStaminaRegen, "StaminaRegen", prof);
        SetUpgradeButtonText(btnHealthRegen, "HealthRegen", prof);
        SetUpgradeButtonText(btnMoveSpeed, "MoveSpeed", prof);
        SetUpgradeButtonText(btnExtraLife, "ExtraLife", prof);

        int pts = PointsManager.Instance != null ? PointsManager.Instance.LocalSpendablePoints : -1;
        Debug.Log($"[Menu] UpdateUpgradeButtonTexts complete. Points={pts}");
    }

    void SetUpgradeButtonText(Button btn, string key, PlayerProfile prof)
    {
        if (btn == null || prof == null)
        {
            if (btn == null) Debug.LogWarning($"[Menu] SetUpgradeButtonText: Button NULL for key '{key}'.");
            return;
        }
        int owned = prof.Get(key);
        int cost = PlayerSpawner.Instance.CalcCost(owned);
        var label = btn.GetComponentInChildren<TMP_Text>();
        if (label == null)
        {
            Debug.LogWarning($"[Menu] SetUpgradeButtonText: No TMP_Text found under button '{btn.name}' for key '{key}'.");
        }
        btn.GetComponentInChildren<TMP_Text>().text = $"{key} ({owned}) - Cost: {cost}";
        Debug.Log($"[Menu] Button text set for '{key}': owned={owned}, cost={cost}, button='{btn.name}'");
    }
}