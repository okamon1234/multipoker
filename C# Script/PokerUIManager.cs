using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.EventSystems;
using System.Linq;
using System.Collections; 

public class PokerUIManager : MonoBehaviour
{
    [Header("チップ表示テキスト（プレイヤーごと）")]
    public Text[] playerChipTexts; // インデックス＝プレイヤー順


    [Header("勝者表示")]
    public Text winnerText;

    [Header("アクションボタン群")]
    public Button checkButton;
    public Button callButton;
    public Button raiseButton;
    public Button foldButton;
    public Button allInButton;
    public InputField raiseInputField;

    private Dictionary<int, int> playerChips = new();

    [Header("追加UI")]
    public Text potText;
    public Text maxBetText;
    public Text myBetText;


    [Header("履歴ログ")]
    public Text logText;
    public ScrollRect logScrollRect; // ScrollViewの本体

    void Start()
    {
        SetupButtonListeners();
    }

    public void SetupButtonListeners()
    {
        checkButton.onClick.RemoveAllListeners();
        callButton.onClick.RemoveAllListeners();
        raiseButton.onClick.RemoveAllListeners();
        foldButton.onClick.RemoveAllListeners();
        allInButton.onClick.RemoveAllListeners();

        checkButton.onClick.AddListener(MultiplayerGameManager.instance.OnCheckButton);
        callButton.onClick.AddListener(MultiplayerGameManager.instance.OnCallButton);
        raiseButton.onClick.AddListener(MultiplayerGameManager.instance.OnRaiseButton);
        foldButton.onClick.AddListener(MultiplayerGameManager.instance.OnFoldButton);
        allInButton.onClick.AddListener(MultiplayerGameManager.instance.OnAllInButton);
    }

    public void ShowWinnerWithHand(int[] winnerActorNumbers, string handName)
    {
        if (winnerActorNumbers.Length == 0)
        {
            winnerText.text = "No Winner";
        }
        else
        {
            string winners = string.Join(", ", winnerActorNumbers);
            winnerText.text = $"Winner: Player {winners}\nHand: {handName}";
        }
        winnerText.gameObject.SetActive(true);
    }

    public void UpdateChipDisplay(Dictionary<int, int> chips)
    {
        var players = PhotonNetwork.PlayerList;

        foreach (var p in players)
        {
            int actorNumber = p.ActorNumber;

            // 自分から見た表示位置（0=自分, 1=左, 2=右, 3=前）を取得
            int displayIndex = MultiplayerGameManager.instance.GetDisplayIndex(actorNumber);

            if (displayIndex >= 0 && displayIndex < playerChipTexts.Length)
            {
                if (chips.TryGetValue(actorNumber, out int chipAmount))
                {
                    playerChipTexts[displayIndex].text = $"{chipAmount} chips";
                }
                else
                {
                    playerChipTexts[displayIndex].text = "-";
                }
            }
        }
    }

    public void ShowWinner(int[] winnerActorNumbers)
    {
        winnerText.text = winnerActorNumbers.Length == 0 ? "No Winner" : "Winner: Player " + string.Join(", ", winnerActorNumbers);
        winnerText.gameObject.SetActive(true);
    }

    public void HideWinner()
    {
        winnerText.gameObject.SetActive(false);
    }
    public void AddLog(string message)
    {
        logText.text = message + "\n" + logText.text; // 上に追加

        // 行数制限（100行以上になったら古い方を削除）
        var lines = logText.text.Split('\n');
        if (lines.Length > 100)
            logText.text = string.Join("\n", lines.Take(100));

        // スクロールを一番上に（新しいログが上にあるため）
        StartCoroutine(ScrollToTopNextFrame());
    }



    private IEnumerator ScrollToTopNextFrame()
    {
        yield return null;
        logScrollRect.verticalNormalizedPosition = 1f; // ← 一番上にスクロール
    }


    public void EnableActionButtons(bool enable)
    {
        checkButton.interactable = enable;
        callButton.interactable = enable;
        raiseButton.interactable = enable;
        foldButton.interactable = enable;
        allInButton.interactable = enable;
        raiseInputField.interactable = enable;
    }

    public void UpdateActionButtons(bool canCheck, int callAmount, int minRaiseAmount = 0, int maxRaiseAmount = 0)
    {
        checkButton.gameObject.SetActive(canCheck);
        callButton.gameObject.SetActive(!canCheck && callAmount > 0);
        allInButton.gameObject.SetActive(true);
        foldButton.gameObject.SetActive(true);
        raiseButton.gameObject.SetActive(true);

        callButton.GetComponentInChildren<Text>().text = $"コール ({callAmount})";

        // ✅ レイズ欄に最低額をプリセット
        if (minRaiseAmount > 0)
            raiseInputField.text = minRaiseAmount.ToString();
        else
            raiseInputField.text = callAmount.ToString();
    }

    public void SetMyTurn(bool isMyTurn)
    {
        EnableActionButtons(isMyTurn);
    }

    public void UpdatePot(int totalPot)
    {
        potText.text = "POT: " + totalPot + " chips";
    }

    public void UpdateBetInfo(int maxBet, int myBet)
    {
        maxBetText.text = "現在最大ベット: " + maxBet;
        myBetText.text = "あなたのベット: " + myBet;
    }



}
