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

    [Header("勝者表示")]
    public Text winnerText;

    [Header("フォールド回数表示")]
    public Text[] playerFoldTexts; // インスペクターで設定（4人分）

    public Button drawButton; // ← 交換ボタン

    [Header("アクションボタン群")]
    public Button foldButton;
    public Button joinButton;

    public Button skipDrawButton;

    [Header("履歴ログ")]
    public Text logText;
    public ScrollRect logScrollRect; // ScrollViewの本体

    [Header("ポイント表示")]
    public Text[] playerPointTexts; // 名前の下に表示（インスペクターで設定）

    [Header("ラウンド情報")]
    public Text roundText;
    public Text maxRoundText;
    public Text winPointText;

    [Header("注視点")]
    public Image[] gazeImages; // 2つのImageをInspectorで設定

    private Coroutine gazePhaseCoroutine;
    private string currentGazePhase = "";

    public void UpdatePlayerPoints(Dictionary<int, int> playerPoints)
    {
        foreach (var kvp in playerPoints)
        {
            int displayIndex = MultiplayerGameManager.instance.GetDisplayIndex(kvp.Key);
            if (displayIndex >= 0 && displayIndex < playerPointTexts.Length)
            {
                playerPointTexts[displayIndex].text = $"Pt: {kvp.Value}";
            }
        }
    }

    public void SetGazeColor(Color color)
    {
        foreach (var image in gazeImages)
        {
            if (image != null) image.color = color;
        }
    }

    public void ShowGazePoint(bool visible)
    {
        foreach (var image in gazeImages)
        {
            if (image != null)
            {
                image.gameObject.SetActive(visible);
            }
        }
    }

    public void StartGazePhase(string phaseName)
    {
        currentGazePhase = phaseName;

        if (gazePhaseCoroutine != null)
            StopCoroutine(gazePhaseCoroutine);

        ShowGazePoint(true); // 常に表示ONにする

        if (phaseName == "Draw")
        {
            SetGazeColor(Color.red);
            gazePhaseCoroutine = StartCoroutine(DrawGazeColorSequence());
        }
        else if (phaseName == "Join")
        {
            SetGazeColor(Color.blue);
            gazePhaseCoroutine = StartCoroutine(JoinGazeColorSequence());
        }
        else if (phaseName == "Reset")
        {
            SetGazeColor(Color.white);
        }
    }

    IEnumerator JoinGazeColorSequence()
    {
        yield return new WaitForSeconds(5f);

        if (currentGazePhase != "Join") yield break;

        SetGazeColor(new Color32(0xFF, 0xD7, 0x00, 0xFF));

        yield return new WaitForSeconds(5f);

        if (currentGazePhase == "Join")
        {
            SetGazeColor(Color.white);
        }
    }

    IEnumerator DrawGazeColorSequence()
    {
        yield return new WaitForSeconds(5f);

        if (currentGazePhase != "Draw") yield break;

        // ゴールデンイエロー（#FFD700）
        SetGazeColor(new Color32(0xFF, 0xD7, 0x00, 0xFF)); // RGBA

        yield return new WaitForSeconds(5f);

        if (currentGazePhase == "Draw")
        {
            SetGazeColor(Color.white);
        }
    }


    IEnumerator DelayedSetGazeColorWhite(string phaseName, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (currentGazePhase == phaseName)
        {
            SetGazeColor(Color.white);
        }
    }


    public void ResetGazeColor()
    {
        currentGazePhase = "";
        if (gazePhaseCoroutine != null) StopCoroutine(gazePhaseCoroutine);
        SetGazeColor(Color.white);
    }


    public void ShowTemporaryWinnerMessage(string message, float duration)
    {
        winnerText.text = message;
        winnerText.gameObject.SetActive(true);
        StartCoroutine(HideWinnerAfterDelay(duration));
    }

    IEnumerator HideWinnerAfterDelay(float delay)
    {
        string displayedTextAtStart = winnerText.text;

        yield return new WaitForSeconds(delay);

        // 表示中のテキストが変化していなければ、つまり「表示が上書きされていなければ」だけ消す
        if (!MultiplayerGameManager.instance.isGameOver && winnerText.text == displayedTextAtStart)
        {
            winnerText.gameObject.SetActive(false);
        }
    }

    public void UpdateRoundInfo(int round, int maxRound, int winPoint)
    {
        roundText.text = $"Round: {round}";
        maxRoundText.text = $"Max: {maxRound}";
        winPointText.text = $"Goal: {winPoint}";
    }

    void Start()
    {
        SetupButtonListeners();
    }

    public void UpdatePlayerFoldCounts(Dictionary<int, int> playerFoldCounts, int maxFoldsAllowed)
    {
        foreach (var kvp in playerFoldCounts)
        {
            int displayIndex = MultiplayerGameManager.instance.GetDisplayIndex(kvp.Key);
            if (displayIndex >= 0 && displayIndex < playerFoldTexts.Length)
            {
                playerFoldTexts[displayIndex].text = $"Fold: {kvp.Value}/{maxFoldsAllowed}";
            }
        }
    }

    public void SetupButtonListeners()
    {
        foldButton.onClick.RemoveAllListeners();
        drawButton.onClick.RemoveAllListeners();
        joinButton.onClick.RemoveAllListeners();
        drawButton.onClick.AddListener(MultiplayerGameManager.instance.OnRequestCardDraw);
        skipDrawButton.onClick.RemoveAllListeners();
        joinButton.onClick.AddListener(MultiplayerGameManager.instance.OnJoinRound);
        skipDrawButton.onClick.AddListener(MultiplayerGameManager.instance.OnSkipDraw);
        foldButton.onClick.AddListener(MultiplayerGameManager.instance.OnFoldRound);
    }

    public void SetDrawButtonVisible(bool visible)
    {
        drawButton.gameObject.SetActive(visible);
    }

    public void SetJoinPhaseUI(bool visible, int remainingFolds = 99)
    {
        joinButton.gameObject.SetActive(visible);
        foldButton.gameObject.SetActive(visible);

        joinButton.interactable = visible;
        foldButton.interactable = visible && remainingFolds > 0;
    }

    public void SetSkipDrawButtonVisible(bool visible)
    {
        if (skipDrawButton != null)
            skipDrawButton.gameObject.SetActive(visible);
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

    public void ShowNoWinnerMessage(string message)
    {
        winnerText.text = message;
        winnerText.gameObject.SetActive(true);
    }

    public void ShowNoWinnerMessageTemporarily(string message, float duration)
    {
        ShowNoWinnerMessage(message);
        StartCoroutine(HideWinnerAfterDelay(duration));
    }

    public void ShowWinnerWithHandTemporarily(int[] winnerActorNumbers, string handName, float duration = 5f)
    {
        ShowWinnerWithHand(winnerActorNumbers, handName);
        StartCoroutine(HideWinnerAfterDelay(duration));
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

    public void SetDrawPhaseUI()
    {
        // Draw/Skip 表示
        SetDrawButtonVisible(true);
        SetDrawButtonInteractable(false); // 最初は選択なしで非活性
        SetSkipDrawButtonVisible(true);

        // Join/Fold 非表示
        SetJoinPhaseUI(false);
    }


    private IEnumerator ScrollToTopNextFrame()
    {
        yield return null;
        logScrollRect.verticalNormalizedPosition = 1f; // ← 一番上にスクロール
    }

    public void SetDrawButtonInteractable(bool interactable)
    {
        if (drawButton != null)
        {
            drawButton.gameObject.SetActive(true); // ← これを追加！見えるようにする
            drawButton.interactable = interactable;
        }
    }

}
