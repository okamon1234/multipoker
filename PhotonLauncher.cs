using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.UI;
using Photon.Pun.Demo.PunBasics;

public class PhotonLauncher : MonoBehaviourPunCallbacks
{
    [SerializeField] private byte maxPlayersPerRoom = 4;
    [SerializeField] private string gameVersion = "1.0";

    [Header("UI")]
    public GameObject connectPanel; 
    public GameObject roomPanel;  
    public Text statusText;
    
    public static PhotonLauncher instance; // Singleton

    private void Awake()
    {
        if (instance == null) instance = this;
        else Destroy(gameObject);
    }


    private void Start()
    {
        PhotonNetwork.AutomaticallySyncScene = true;
        connectPanel.SetActive(true);
        roomPanel.SetActive(false);
    }

    public void Connect()
    {
        statusText.text = "接続中...";
        if (PhotonNetwork.IsConnected)
        {
            PhotonNetwork.JoinRandomRoom();
        }
        else
        {
            PhotonNetwork.GameVersion = gameVersion;
            PhotonNetwork.ConnectUsingSettings();
        }
    }


    public override void OnConnectedToMaster()
    {
        statusText.text = "マスターサーバーに接続成功。ルームを検索中...";
        PhotonNetwork.JoinRandomRoom();
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        statusText.text = "空きルームがないため新規作成します...";
        PhotonNetwork.CreateRoom(null, new RoomOptions { MaxPlayers = maxPlayersPerRoom });
    }


    // ルーム作成失敗時の処理
    public override void OnJoinedRoom()
    {
        statusText.text = $"ルームに参加しました ({PhotonNetwork.CurrentRoom.PlayerCount}/{maxPlayersPerRoom})";

        connectPanel.SetActive(false);
        roomPanel.SetActive(true);

        if (PhotonNetwork.CurrentRoom.PlayerCount == maxPlayersPerRoom)
        {
            StartGame();
        }
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        statusText.text = $"プレイヤー参加: {newPlayer.NickName} ({PhotonNetwork.CurrentRoom.PlayerCount}/{maxPlayersPerRoom})";

        if (PhotonNetwork.CurrentRoom.PlayerCount == maxPlayersPerRoom)
        {
            StartGame();
        }
    }

    private void StartGame()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            statusText.text = "全員揃ったのでゲーム開始！";
            MultiplayerGameManager.instance.StartGame();
        }
    }
}
