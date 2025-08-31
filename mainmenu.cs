using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;      // for Photon.Realtime.Player
using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class MainMenuManager : MonoBehaviourPunCallbacks
{
    [Header("Inputs")]
    public TMP_InputField nameInput;
    public TMP_InputField joinCodeInput;

    [Header("Displays")]
    public TextMeshProUGUI roomCodeText;
    public TextMeshProUGUI playerListText;
    public TextMeshProUGUI statusText;  // shows "Joining..."

    [Header("Buttons")]
    public Button createButton;
    public Button joinButton;
    public Button playButton;
    public Button leaveButton;
    public Button quitButton;

    [Header("Settings")]
    public string gameSceneName = "YourGameSceneName";
    const int ROOM_CODE_LENGTH = 6;
    const float CODE_DISPLAY_DELAY = 1f;

    // keep track of active room codes from lobby (optional now)
    private HashSet<string> availableRooms = new HashSet<string>();

    void Start()
    {
        PhotonNetwork.AutomaticallySyncScene = true;

        createButton.interactable = false;
        joinButton.interactable   = false;
        playButton.interactable   = false;
        leaveButton.interactable  = false;
        statusText.text = "";

        nameInput.onValueChanged.AddListener(_ => UpdateButtons());
        joinCodeInput.onValueChanged.AddListener(_ => UpdateButtons());

        PhotonNetwork.ConnectUsingSettings();
    }

    void UpdateButtons()
    {
        bool hasName  = !string.IsNullOrWhiteSpace(nameInput.text);
        string code   = joinCodeInput.text.Trim().ToUpperInvariant();

        // Enable joinButton if name entered AND joinCode looks valid (length + digits)
        bool isCodeValid = code.Length == ROOM_CODE_LENGTH && IsCodeNumeric(code);

        bool canMatch = PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InRoom;

        createButton.interactable = hasName && canMatch;
        joinButton.interactable = hasName && isCodeValid && canMatch;

        // Note: No longer requiring availableRooms.Contains(code) here
    }

    bool IsCodeNumeric(string code)
    {
        foreach (char c in code)
            if (!char.IsDigit(c))
                return false;
        return true;
    }

    public override void OnConnectedToMaster()
    {
        // Guard: don't call JoinLobby in OfflineMode or when not fully ready
        if (PhotonNetwork.OfflineMode)
        {
            UpdateButtons();
            return;
        }
        if (PhotonNetwork.IsConnectedAndReady)
            PhotonNetwork.JoinLobby(); // Join lobby to keep room list updated if you want
    }

    public override void OnJoinedLobby()
    {
        UpdateButtons();
    }

    public override void OnRoomListUpdate(List<RoomInfo> roomList)
    {
        // Keep room list updated (optional - used only for display or info)
        foreach (var info in roomList)
        {
            string roomName = info.Name.ToUpperInvariant();

            if (info.RemovedFromList)
                availableRooms.Remove(roomName);
            else
                availableRooms.Add(roomName);
        }
        UpdateButtons();
    }

    // ─── Create / Join ──────────────────────────────────────────────

    public void OnCreatePressed()
    {
        SetNickname();
        string code = GenerateRoomCode();
        PhotonNetwork.CreateRoom(code, new RoomOptions { MaxPlayers = 4 });
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"CreateRoom failed: {message}");
        createButton.interactable = true;
        statusText.text = $"Create room failed: {message}";
    }

    public void OnJoinPressed()
    {
        SetNickname();
        string code = joinCodeInput.text.Trim().ToUpperInvariant();

        // Try joining the room directly regardless of lobby or room list availability
        PhotonNetwork.JoinRoom(code);

        // Disable join button to prevent spamming until callback
        joinButton.interactable = false;
        statusText.text = $"Joining room {code}...";
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"JoinRoom failed: {message}");
        joinButton.interactable = true;
        statusText.text = $"Failed to join room: {message}";
    }

    public override void OnJoinedRoom()
    {
        leaveButton.interactable = true;
        playButton.interactable  = PhotonNetwork.IsMasterClient;

        RefreshPlayerList();
        StartCoroutine(DisplayRoomCodeDelayed());

        statusText.text = "";
    }

    // ─── Play / Leave / Quit ───────────────────────────────────────

    public void OnPlayPressed()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        playButton.interactable = false;
        statusText.text        = "Starting game...";

        PhotonNetwork.LoadLevel(gameSceneName);
    }

    public void OnLeavePressed()
    {
        if (PhotonNetwork.InRoom)
            PhotonNetwork.LeaveRoom();
    }

    public override void OnLeftRoom()
    {
        roomCodeText.text   = "";
        playerListText.text = "";
        statusText.text     = "";

        leaveButton.interactable = false;
        playButton.interactable  = false;
        UpdateButtons();

        // Guard: avoid JoinLobby when offline or not connected
        if (!PhotonNetwork.OfflineMode && PhotonNetwork.IsConnected)
            PhotonNetwork.JoinLobby();
    }

    public void OnQuitPressed()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ─── Player list callbacks ──────────────────────────────────────

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        RefreshPlayerList();
    }

    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        RefreshPlayerList();
    }

    void RefreshPlayerList()
    {
        if (PhotonNetwork.CurrentRoom == null) return;
        var names = new List<string>();
        foreach (var p in PhotonNetwork.CurrentRoom.Players.Values)
            names.Add(p.NickName);
        playerListText.text = string.Join("\n", names);
    }

    // ─── Helpers ────────────────────────────────────────────────────

    void SetNickname()
    {
        string nick = nameInput.text.Trim();
        if (!string.IsNullOrEmpty(nick))
            PhotonNetwork.NickName = nick;
    }

    string GenerateRoomCode()
    {
        const string chars = "0123456789";  // numeric only
        var buffer = new char[ROOM_CODE_LENGTH];
        for (int i = 0; i < ROOM_CODE_LENGTH; i++)
            buffer[i] = chars[Random.Range(0, chars.Length)];
        return new string(buffer);
    }

    IEnumerator DisplayRoomCodeDelayed()
    {
        yield return new WaitForSeconds(CODE_DISPLAY_DELAY);
        roomCodeText.text = PhotonNetwork.CurrentRoom?.Name ?? "";
    }
}