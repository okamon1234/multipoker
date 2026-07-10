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
    public GameObject connectPanel; // 最初の接続パネル
    public GameObject roomPanel;    // ルームに入った後のパネル
    public Text statusText;

    private void Start()
    {
        PhotonNetwork.AutomaticallySyncScene = true; // シーン自動同期
        connectPanel.SetActive(true);
        roomPanel.SetActive(false);
    }

    public void Connect()
    {
        statusText.text = "接続中...";
        if (PhotonNetwork.IsConnected)
        {
            // 既に接続済みならルーム参加
            PhotonNetwork.JoinRandomRoom();
        }
        else
        {
            // PhotonServerへ接続
            PhotonNetwork.GameVersion = gameVersion;
            PhotonNetwork.ConnectUsingSettings();
        }
    }

    // コールバック：PhotonServer接続成功
    public override void OnConnectedToMaster()
    {
        statusText.text = "マスターサーバーに接続完了。ルーム参加中...";
        PhotonNetwork.JoinRandomRoom();
    }

    // コールバック：JoinRandomRoom失敗（空き部屋なし）
    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        statusText.text = "空きルームがないので新しく作成中...";
        PhotonNetwork.CreateRoom(null, new RoomOptions { MaxPlayers = maxPlayersPerRoom });
    }

    // コールバック：ルーム入室成功
    public override void OnJoinedRoom()
    {
        statusText.text = $"ルーム入室成功！ ({PhotonNetwork.CurrentRoom.PlayerCount}/{maxPlayersPerRoom})";

        connectPanel.SetActive(false);
        roomPanel.SetActive(true);

        if (PhotonNetwork.CurrentRoom.PlayerCount == maxPlayersPerRoom)
        {
            StartGame();
        }
    }

    // コールバック：他のプレイヤーがルームに入ったとき
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
