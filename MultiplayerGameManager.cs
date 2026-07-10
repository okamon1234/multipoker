// MultiplayerGameManager 完全版（シンプル＆実用対応）

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using System.Linq;
using static PokerHandEvaluator;
using UnityEngine.Rendering;
using Unity.Collections.LowLevel.Unsafe;
using System.IO; // ファイル入出力に必要
using System;
using System.Collections;

public static class CardUtils
{
    public static PokerHandEvaluator.Card ToEvalCard(Card.Data data)
{
    string suitStr = data.Mark switch
    {
        Mark.Spade => "S",
        Mark.Heart => "H",
        Mark.Diamond => "D",
        Mark.Club => "C",
        _ => "?"
    };

    return new PokerHandEvaluator.Card(
        data.Number == 1 ? 14 : data.Number,
        suitStr
    );
}
}
public class MultiplayerGameManager : MonoBehaviourPunCallbacks
{
    public GameObject cardPrefab; // Card prefab（Inspectorでセット）
    public Transform[] playerCardParents; // プレイヤーごとの手札表示用親Transform配列（人数分）

    // 既存フィールドに加えて↓を追加
    [SerializeField]
    private Text[] playerNameTexts; // インスペクターで4人分のTextをセット

    private HashSet<int> currentParticipants = new();

    [SerializeField] private int startingPoints = 5; // Inspectorで設定可能
    private Dictionary<int, int> playerPoints = new();

    [SerializeField]
    private int maxRounds = 10; // Inspectorで設定できるようにする

    [SerializeField] private int winPointThreshold = 10;

    private Dictionary<int, List<int>> selectedCardIndices = new(); // どのカードを交換するか（0〜4）

    private int currentRound = 1;

    private HashSet<int> finishedDrawPlayers = new();

    private List<Card.Data> deck;
    private Dictionary<int, List<Card.Data>> playerHoleCards = new();
    private List<int> foldedPlayers = new();
    private HashSet<int> actedPlayers = new(); // アクション済みプレイヤー
    private Dictionary<int, string> playerNames = new();

    private bool hasShownWinnerUI = false;

    private Dictionary<int, int> playerDisplayIndexByActor = new();

    [SerializeField]
    private int maxFoldsAllowed = 3; // ← Unity Inspector で変更可能

    private Dictionary<int, int> playerFoldCounts = new(); // 各プレイヤーのフォールド回数


    public bool isGameOver = false;


    private bool waitingForNextRound = false;
    private List<string> logLines = new();

    // プレイヤーID → 表示中のカードオブジェクト（2枚）
    private Dictionary<int, List<Card>> playerCardObjects = new Dictionary<int, List<Card>>();

    // ボードに表示中のカードオブジェクト
    [SerializeField]
    private PokerHandEvaluator handEvaluator;
    [SerializeField] private PokerUIManager uiManager;  // ← これを追加

    private int currentTurnIndex = 0;

    private enum GameStage
    {
        InitialBetting, // 最初の配布後ベッティング
        Draw,           // カード交換ステージ
        FinalBetting,   // 交換後のベッティング
        Showdown        // 勝敗判定
    }
    private GameStage currentStage = GameStage.InitialBetting;

    [System.Serializable]
    public struct CardInfo
    {
        public int Number;
        public int Mark; // enum Markのint値

        public CardInfo(int number, Mark mark)
        {
            Number = number;
            Mark = (int)mark;
        }
    }

    public static MultiplayerGameManager instance;

    void Update()
    {
        if (isGameOver && PhotonNetwork.IsMasterClient)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                Debug.Log("スペースが押されたのでゲームをリセットします");
                InitializeGame(); // またはタイトルに戻る処理でもOK
            }
        }
    }


    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            Destroy(gameObject);
        }

        if (PhotonLauncher.instance != null && PhotonLauncher.instance.statusText != null)
        {
            PhotonLauncher.instance.statusText.gameObject.SetActive(false);
        }

        // PokerUIManagerを見つける
        if (uiManager == null)
        {
            uiManager = FindObjectOfType<PokerUIManager>();
            if (uiManager == null)
            {
                Debug.LogError("[MultiplayerGameManager] PokerUIManagerがシーンに存在しません！");
            }
            else
            {
                Debug.Log("[MultiplayerGameManager] PokerUIManagerを見つけました。");
            }
        }
        // ★ここでボタンを毎回登録しなおす
        if (uiManager != null)
        {
            uiManager.SetupButtonListeners();
        }
    }


    public void StartGame()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            currentRound = 1;
            InitializeGame();
        }
    }


    void InitializeGame()
    {
        // プレイヤー名を決定（MasterClientだけ）
        List<int> actorNumberList = new();  // 名前変更
        List<string> names = new();

        int[] actorNumbers = PhotonNetwork.PlayerList.Select(p => p.ActorNumber).ToArray();
        int[] initialPoints = actorNumbers.Select(_ => startingPoints).ToArray();
        photonView.RPC(nameof(SyncPlayerPointsRPC), RpcTarget.All, actorNumbers, initialPoints);

        for (int i = 0; i < PhotonNetwork.PlayerList.Length; i++)
        {
            var player = PhotonNetwork.PlayerList[i];
            string name = $"Player{i + 1}";
            playerNames[player.ActorNumber] = name;

            actorNumberList.Add(player.ActorNumber);  // 配列ではなくリストに追加
            names.Add(name);
        }


        // 全クライアントに名前を同期
        photonView.RPC(nameof(SyncPlayerNamesRPC), RpcTarget.All, actorNumbers.ToArray(), names.ToArray());

        // UIログ出力
        Debug.Log($"InitializeGame呼ばれたよ。uiManager is {(uiManager == null ? "null" : "NOT null")}");
        if (uiManager == null)
        {
            Debug.LogError("InitializeGame中にuiManagerがnullです！！");
            return;
        }

        // デッキ初期化
        photonView.RPC(nameof(InitDeckRPC), RpcTarget.All);

        // 初回スコア設定
        foreach (var p in PhotonNetwork.PlayerList)
        {
            int actor = p.ActorNumber;
            if (!playerPoints.ContainsKey(actor))
            {
                playerPoints[actor] = startingPoints;
            }
            if (!playerFoldCounts.ContainsKey(actor))
            {
                playerFoldCounts[actor] = 0;
            }
        }

        if (PhotonNetwork.IsMasterClient)
        {
            int[] foldActors = playerFoldCounts.Keys.ToArray();
            int[] foldValues = foldActors.Select(a => playerFoldCounts[a]).ToArray();
            photonView.RPC(nameof(SyncFoldCountsRPC), RpcTarget.All, foldActors, foldValues);
        }
        
        currentRound = 1;
        currentStage = GameStage.InitialBetting;
        waitingForNextRound = false;

        if (PhotonNetwork.IsMasterClient)
        {
            photonView.RPC(nameof(SyncRoundInfoRPC), RpcTarget.All, currentRound, maxRounds, winPointThreshold);
        }

        AppendGameLog($"=== 第{currentRound}ラウンド開始 ===");
        DealHoleCards();      // まずカードを配る
        currentStage = GameStage.InitialBetting;
        currentStage = GameStage.Draw;
        if (PhotonNetwork.IsMasterClient)
        {
            photonView.RPC(nameof(RunStartDrawPhase), RpcTarget.All);
        }
    }

    [PunRPC]
    void SyncRoundInfoRPC(int round, int maxRound, int winPoint)
    {
        uiManager.UpdateRoundInfo(round, maxRound, winPoint);
    }

    [PunRPC]
    void SetGazePhaseRPC(string phaseName)
    {
        if (uiManager == null) return;

        if (phaseName == "Draw")
            uiManager.StartGazePhase("Draw");
        else if (phaseName == "Join")
            uiManager.StartGazePhase("Join");
        else if (phaseName == "Reset")
            uiManager.ResetGazeColor();
    }

    [PunRPC]
    void SyncFoldCountsRPC(int[] actorNumbers, int[] foldCounts)
    {
        for (int i = 0; i < actorNumbers.Length; i++)
        {
            playerFoldCounts[actorNumbers[i]] = foldCounts[i];
        }

        uiManager.UpdatePlayerFoldCounts(playerFoldCounts, maxFoldsAllowed);

        // 🔁 フォールド制限に応じて UI を制御（ローカルプレイヤーだけ）
        int actor = PhotonNetwork.LocalPlayer.ActorNumber;
        int remainingFolds = maxFoldsAllowed - (playerFoldCounts.ContainsKey(actor) ? playerFoldCounts[actor] : 0);

        // もし UI が表示中なら状態を反映させる
        if (uiManager.joinButton.gameObject.activeSelf || uiManager.foldButton.gameObject.activeSelf)
        {
            uiManager.SetJoinPhaseUI(true, remainingFolds); // 再反映
        }
    }

    public void AppendGameLog(string message)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string fullMessage = $"[{timestamp}] {message}";
        string path = Path.Combine(Application.persistentDataPath, "game_log.txt");

        try
        {
            File.AppendAllText(path, fullMessage + Environment.NewLine);
        }
        catch (Exception e)
        {
            Debug.LogError($"ログ書き込みエラー: {e.Message}");
        }
    }


    [PunRPC]
    void EnterDrawPhaseRPC()
    {
        uiManager.SetDrawPhaseUI();
    }

    void BeginJoinPhase()
    {
        uiManager.SetJoinPhaseUI(true); // ボタンを表示

        int myActor = PhotonNetwork.LocalPlayer.ActorNumber;
        if (playerPoints.TryGetValue(myActor, out int points))
        {
            if (points <= 0)
            {
                uiManager.SetJoinPhaseUI(false);
                AppendGameLog("あなたはポイントが0のためこのラウンドに参加できません。");
            }
        }
        if (PhotonNetwork.IsMasterClient)
        {
            AppendGameLog("Joinラウンド開始");
        }
        uiManager.SetDrawButtonVisible(false);
        uiManager.SetSkipDrawButtonVisible(false);
        photonView.RPC(nameof(SetGazePhaseRPC), RpcTarget.All, "Join");
    }

    public void OnJoinRound()
    {
        int actor = PhotonNetwork.LocalPlayer.ActorNumber;

        if (currentParticipants.Contains(actor) || foldedPlayers.Contains(actor)) return;

        // 参加リクエストをMasterClientに送る
        photonView.RPC(nameof(RequestJoinRPC), RpcTarget.MasterClient, actor);

        uiManager.SetJoinPhaseUI(false);
    }

    [PunRPC]
    void RequestJoinRPC(int actor)
    {
        if (!playerPoints.ContainsKey(actor)) return;
        if (playerPoints[actor] <= 0) return;

        playerPoints[actor] -= 1;
        currentParticipants.Add(actor);

        // 全員に同期
        int[] actors = playerPoints.Keys.ToArray();
        int[] values = actors.Select(a => playerPoints[a]).ToArray();
        photonView.RPC(nameof(SyncPlayerPointsRPC), RpcTarget.All, actors, values);

        // ログ出力
        string name = GetPlayerName(actor);
        string hand = string.Join(" ", playerHoleCards[actor].Select(c => $"{c.Number}{c.Mark}"));
        AppendGameLog($"{name} はこのラウンドに参加しました。");

        CheckJoinFoldCompletion();
    }

    [PunRPC]
    void SyncPlayerPointsRPC(int[] actorNumbers, int[] points)
    {
        for (int i = 0; i < actorNumbers.Length; i++)
        {
            playerPoints[actorNumbers[i]] = points[i];
        }

        uiManager.UpdatePlayerPoints(playerPoints); 
        uiManager.UpdatePlayerFoldCounts(playerFoldCounts, maxFoldsAllowed);
    }

    public void OnFoldRound()
    {
        int actor = PhotonNetwork.LocalPlayer.ActorNumber;

        // ✅ すでにフォールド済 or 参加済みなら無視
        if (foldedPlayers.Contains(actor) || currentParticipants.Contains(actor))
            return;

        // ✅ 自分のフォールド回数を確認（ローカルチェック）
        int foldCount = playerFoldCounts.ContainsKey(actor) ? playerFoldCounts[actor] : 0;
        if (foldCount >= maxFoldsAllowed)
        {
            Debug.LogWarning("フォールド回数の上限に達しています。フォールドできません。");
            return;
        }

        // UIだけ先に閉じる
        uiManager.SetJoinPhaseUI(false);

        // サーバーに送信
        photonView.RPC(nameof(RequestFoldRPC), RpcTarget.MasterClient, actor);
    }

    [PunRPC]
    void RequestFoldRPC(int actor)
    {
        if (!playerFoldCounts.ContainsKey(actor))
            playerFoldCounts[actor] = 0;

        if (playerFoldCounts[actor] >= maxFoldsAllowed)
            return;

        playerFoldCounts[actor]++;
        foldedPlayers.Add(actor);

        // Foldカウント同期
        int[] actors = playerFoldCounts.Keys.ToArray();
        int[] foldCounts = actors.Select(a => playerFoldCounts[a]).ToArray();
        photonView.RPC(nameof(SyncFoldCountsRPC), RpcTarget.All, actors, foldCounts);

        string name = GetPlayerName(actor);
        string hand = string.Join(" ", playerHoleCards[actor].Select(c => $"{c.Number}{c.Mark}"));
        AppendGameLog($"{name} はこのラウンドをフォールドしました。");

        CheckJoinFoldCompletion();
    }

    void CheckJoinFoldCompletion()
    {
        int total = currentParticipants.Count + foldedPlayers.Count;
        int expected = PhotonNetwork.CurrentRoom.PlayerCount;

        Debug.Log($"参加: {currentParticipants.Count}, フォールド: {foldedPlayers.Count}, 合計: {total}, 期待: {expected}");

        if (total >= expected && PhotonNetwork.IsMasterClient)
        {
            photonView.RPC(nameof(HandleShowdown), RpcTarget.All);
        }
    }

    [PunRPC]
    void AdvanceStageRPC()
    {
        AdvanceStage();
    }

    [PunRPC]
    void SyncPlayerNamesRPC(int[] actorNumbers, string[] names)
    {
        for (int i = 0; i < actorNumbers.Length; i++)
        {
            playerNames[actorNumbers[i]] = names[i];

            int displayIndex = GetDisplayIndex(actorNumbers[i]);
            if (displayIndex >= 0 && displayIndex < playerNameTexts.Length)
            {
                playerNameTexts[displayIndex].text = names[i];
            }
        }
    }

    [PunRPC]
    void PrepareNextRoundRPC()
    {
        PrepareNextRound(); // 全員で実行される
    }



    [PunRPC]
    void InitDeckRPC()
    {
        InitDeck();
        ShuffleDeck();
    }

    void InitDeck()
    {
        deck = new List<Card.Data>();
        foreach (Mark mark in System.Enum.GetValues(typeof(Mark)))
            for (int num = 1; num <= 13; num++)
                deck.Add(new Card.Data { Number = num, Mark = mark });
    }

    void ShuffleDeck()
    {
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
    }

    void DealHoleCards()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        AppendGameLog("カードが配られました");

        foreach (var p in PhotonNetwork.PlayerList)
        {
            var cards = DrawCards(5); // 🔄 5枚配る
            playerHoleCards[p.ActorNumber] = cards;

            // 🔄 int配列 [num1, mark1, ..., num5, mark5] に変換（10個）
            int[] serializedCards = new int[10];
            for (int i = 0; i < 5; i++)
            {
                serializedCards[i * 2] = cards[i].Number;
                serializedCards[i * 2 + 1] = (int)cards[i].Mark;
            }

            photonView.RPC(nameof(SyncPlayerHoleCards), RpcTarget.All, p.ActorNumber, serializedCards);
            string cardStr = string.Join(" ", cards.Select(c => $"{c.Number}{c.Mark}"));
            AppendGameLog($"{GetPlayerName(p.ActorNumber)} の初期手札: {cardStr}");
        }
        
    }

    [PunRPC]
    void SyncPlayerHoleCards(int actorNumber, int[] serializedCards)
    {
        List<Card.Data> cardList = new();
        for (int i = 0; i < 5; i++)
        {
            cardList.Add(new Card.Data
            {
                Number = serializedCards[i * 2],
                Mark = (Mark)serializedCards[i * 2 + 1]
            });
        }

        playerHoleCards[actorNumber] = cardList;

        ShowPlayerCards(actorNumber); // 手札表示
    }


    List<Card.Data> DrawCards(int count)
    {
        List<Card.Data> drawn = new();
        for (int i = 0; i < count; i++)
        {
            drawn.Add(deck[0]);
            deck.RemoveAt(0);
        }
        return drawn;
    }


    [PunRPC]
    void AddLogRPC(string message)
    {
        uiManager.AddLog(message);
    }

    public void OnCardClicked(int index)
    {
        int actor = PhotonNetwork.LocalPlayer.ActorNumber;

        if (!selectedCardIndices.TryGetValue(actor, out var list))
        {
            list = new List<int>();
            selectedCardIndices[actor] = list;
        }

        if (list.Contains(index))
            list.Remove(index);
        else
            list.Add(index);

        uiManager.SetDrawButtonInteractable(list.Count > 0);
    }


    public void OnRequestCardDraw()
    {
        int actor = PhotonNetwork.LocalPlayer.ActorNumber;

        if (!selectedCardIndices.TryGetValue(actor, out var selected)) return;
        // UIロック
        if (playerCardObjects.TryGetValue(actor, out var cards))
        {
            foreach (var c in cards)
                c.SetSelectable(false);
        }

        uiManager.SetDrawButtonInteractable(false);
        uiManager.SetSkipDrawButtonVisible(false);

        photonView.RPC(nameof(RequestCardExchange), RpcTarget.MasterClient, actor, selected.ToArray());
    }

    [PunRPC]
    void RequestCardExchange(int actorNumber, int[] indicesToReplace)
    {

        if (indicesToReplace == null || indicesToReplace.Length == 0)
        {
            Debug.Log($"[サーバー] {actorNumber} は交換せずスキップ");
            finishedDrawPlayers.Add(actorNumber);
            if (finishedDrawPlayers.Count == PhotonNetwork.CurrentRoom.PlayerCount)
            {
                if (PhotonNetwork.IsMasterClient)
                {
                    // ✅ 全員が交換を終えたら次のステージへ進む
                    AppendGameLog("全員がカード交換を完了しました。次のステージへ進みます。");
                    photonView.RPC(nameof(AdvanceStageRPC), RpcTarget.All);
                }
            }
            return;
        }

        if (!playerHoleCards.ContainsKey(actorNumber)) return;

        var hand = playerHoleCards[actorNumber];
        var before = hand.Select(c => $"{c.Number}{c.Mark}").ToList(); // 交換前を記録
        for (int i = 0; i < indicesToReplace.Length; i++)
        {
            int idx = indicesToReplace[i];
            if (idx >= 0 && idx < hand.Count)
            {
                hand[idx] = DrawCards(1)[0];
            }
        }

        var after = hand.Select(c => $"{c.Number}{c.Mark}").ToList(); // 交換後を記録

        int numExchanged = indicesToReplace.Length;
        string name = GetPlayerName(actorNumber);

        // ✅ ログ出力
        AppendGameLog($"{name} はカードを {numExchanged} 枚交換しました");
        AppendGameLog($"交換後: {string.Join(" ", after)}");

        // 同期送信
        int[] serialized = new int[10];
        for (int i = 0; i < 5; i++)
        {
            serialized[i * 2] = hand[i].Number;
            serialized[i * 2 + 1] = (int)hand[i].Mark;
        }

        photonView.RPC(nameof(SyncPlayerHoleCards), RpcTarget.All, actorNumber, serialized);

        // 完了チェック
        finishedDrawPlayers.Add(actorNumber);
        photonView.RPC(nameof(AddLogRPC), RpcTarget.All, $"{name} はカードを {numExchanged} 枚交換しました。");


        if (finishedDrawPlayers.Count == PhotonNetwork.CurrentRoom.PlayerCount)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                photonView.RPC(nameof(AdvanceStageRPC), RpcTarget.All); // ✅ 全員で AdvanceStage を実行
            }
        }
    }


    Photon.Realtime.Player PhotonPlayerFromActor(int actorNumber)
    {
        if (PhotonNetwork.CurrentRoom.Players.TryGetValue(actorNumber, out var player))
        {
            return player;
        }
        Debug.LogError($"[PhotonPlayerFromActor] ActorNumber {actorNumber} のプレイヤーが見つかりません。");
        return null;
    }

    void StartDrawPhase()
    {
        finishedDrawPlayers.Clear();
        selectedCardIndices.Clear();
        int actor = PhotonNetwork.LocalPlayer.ActorNumber;
        selectedCardIndices[actor] = new List<int>(); // ✅ 毎ラウンド最初に初期化

        uiManager.SetSkipDrawButtonVisible(true);


        if (PhotonNetwork.LocalPlayer.IsMasterClient)
        {
            photonView.RPC(nameof(AddLogRPC), RpcTarget.All, "カード交換フェーズに入りました。");
            StartCoroutine(DelayedSendDrawPhase());
        }
        if (PhotonNetwork.IsMasterClient)
        {
            AppendGameLog("カード交換フェーズに入りました。");
        }
        photonView.RPC(nameof(SetGazePhaseRPC), RpcTarget.All, "Draw");

    }

    [PunRPC]
    void RunStartDrawPhase()
    {
        StartDrawPhase();
    }

    IEnumerator DelayedSendDrawPhase()
    {
        yield return new WaitForSeconds(0.1f);
        photonView.RPC(nameof(EnterDrawPhaseRPC), RpcTarget.All);
    }

    public void OnSkipDraw()
    {
        int actor = PhotonNetwork.LocalPlayer.ActorNumber;

        // 手札カードの選択を無効化
        if (playerCardObjects.TryGetValue(actor, out var cards))
        {
            foreach (var c in cards)
                c.SetSelectable(false);
        }

        // ボタン類も無効化・非表示
        uiManager.SetDrawButtonInteractable(false);
        uiManager.SetSkipDrawButtonVisible(false);

        photonView.RPC(nameof(RequestCardExchange), RpcTarget.MasterClient, actor, new int[0]);
    }


    void AdvanceStage()
    {
        actedPlayers.Clear();

        switch (currentStage)
        {
            case GameStage.InitialBetting:
                // ❌ 今回は使わない（配布直後には参加しない）
                break;

            case GameStage.Draw:
                currentStage = GameStage.InitialBetting; // ← 次に「参加 or 降りる」フェーズへ
                photonView.RPC(nameof(EnterJoinPhaseRPC), RpcTarget.All);
                break;

            case GameStage.FinalBetting:
            case GameStage.Showdown:
                // 通常はここに来ない
                break;
        }
    }

    [PunRPC]
    void EnterJoinPhaseRPC()
    {
        BeginJoinPhase(); // 全員がJoin/Foldボタンを表示する
    }

    [PunRPC]
    void HandleShowdown()
    {
        Debug.Log("=== ショーダウン突入（ドローポーカー） ===");
        photonView.RPC(nameof(SetGazeVisibilityRPC), RpcTarget.All, false);

        uiManager.ShowTemporaryWinnerMessage("ショーダウン...", 3f); // 3秒表示

        if (PhotonNetwork.IsMasterClient)
        {
            StartCoroutine(DelayedShowdownLogic());
        }
    }

    IEnumerator DelayedShowdownLogic()
    {
        Debug.Log("=== ショーダウンフェーズ開始 ===");
        AppendGameLog("ショーダウン開始");

        uiManager.ShowTemporaryWinnerMessage("ショーダウン...", 3f);
        yield return new WaitForSeconds(3f);

        Dictionary<int, PokerHandEvaluator.HandResult> handResults = new();

        foreach (var player in PhotonNetwork.PlayerList)
        {
            int actor = player.ActorNumber;
            if (!playerHoleCards.ContainsKey(actor)) continue;
            if (foldedPlayers.Contains(actor)) continue;

            var cards = playerHoleCards[actor].Select(CardUtils.ToEvalCard).ToList();
            var result = handEvaluator.EvaluateHand(cards);
            handResults[actor] = result;

            string hand = string.Join(" ", playerHoleCards[actor].Select(c => $"{c.Number}{c.Mark}"));
            AppendGameLog($"{GetPlayerName(actor)} の役: {result.handRank}");
        }

        if (handResults.Count == 0)
        {
            Debug.LogWarning("全員フォールド済み。引き分けとして次のラウンドへ");
            AppendGameLog("全員フォールド：引き分けで次のラウンドへ");

            // 🟡 勝者表示UIにも「勝者なし」と出す
            photonView.RPC(nameof(AnnounceNoWinner), RpcTarget.All);

            yield return new WaitForSeconds(5f);

            if (PhotonNetwork.IsMasterClient)
                PrepareNextRound();

            yield break;
        }

        var bestStrength = handResults.Max(h => PokerHandEvaluator.GetHandStrength(h.Value.handRank));
        var bestRankNumbers = handResults
            .Where(h => PokerHandEvaluator.GetHandStrength(h.Value.handRank) == bestStrength)
            .Select(h => h.Value.rankNumbers)
            .OrderByDescending(r => r, new RankNumberComparer())
            .First();

        var winners = handResults
            .Where(h => PokerHandEvaluator.GetHandStrength(h.Value.handRank) == bestStrength &&
                        new RankNumberComparer().Compare(h.Value.rankNumbers, bestRankNumbers) == 0)
            .Select(h => h.Key)
            .ToList();

        var bestHand = handResults[winners[0]].handRank;
        string winnerNames = string.Join(", ", winners.Select(GetPlayerName));
        AppendGameLog($"勝者: {winnerNames}（役: {bestHand}）");

        photonView.RPC(nameof(AnnounceWinnersWithHand), RpcTarget.All, winners.ToArray(), bestHand.ToString());

        // ポイント加算 & 勝利判定
        int winnerGain = currentParticipants.Count;
        bool gameEnded = false;

        foreach (int winner in winners)
        {
            if (!playerPoints.ContainsKey(winner)) continue;
            playerPoints[winner] += winnerGain;

            if (playerPoints[winner] >= winPointThreshold)
            {
                gameEnded = true;
            }
        }

        yield return new WaitForSeconds(5f);

        // ラウンド数上限 or スコア勝利 → Game Over モードへ
        if (PhotonNetwork.IsMasterClient)
        {
            if (currentRound >= maxRounds || gameEnded)
            {
                EndGame(); // ← 勝者表示＆isGameOver = true
            }
            else
            {
                PrepareNextRound(); // 通常進行
            }
        }
    }

    [PunRPC]
    void AnnounceWinnersWithHand(int[] winnerActorNumbers, string handName)
    {
        if (!isGameOver && currentRound < maxRounds)
        {
            uiManager.ShowWinnerWithHandTemporarily(winnerActorNumbers, handName, 5f);
        }
        else
        {
            uiManager.ShowWinnerWithHand(winnerActorNumbers, handName);
        }
    }

    class RankNumberComparer : IComparer<List<int>>
    {
        public int Compare(List<int> a, List<int> b)
        {
            for (int i = 0; i < Mathf.Min(a.Count, b.Count); i++)
            {
                if (a[i] > b[i]) return 1;
                if (a[i] < b[i]) return -1;
            }
            return 0;
        }
    }

    void ShowPlayerCards(int actorNumber)
    {
        if (playerCardObjects.ContainsKey(actorNumber))
        {
            foreach (var c in playerCardObjects[actorNumber])
            {
                Destroy(c.gameObject);
            }
            playerCardObjects[actorNumber].Clear();
        }
        else
        {
            playerCardObjects[actorNumber] = new List<Card>();
        }

        bool isMine = (actorNumber == PhotonNetwork.LocalPlayer.ActorNumber);
        int parentIndex = GetDisplayIndex(actorNumber);
        if (parentIndex < 0 || parentIndex >= playerCardParents.Length) return;

        for (int i = 0; i < 5; i++) // 🔄 5枚に変更
        {
            var cardData = playerHoleCards[actorNumber][i];
            GameObject go = Instantiate(cardPrefab, playerCardParents[parentIndex]);
            Card card = go.GetComponent<Card>();
            card.SetCard(cardData.Number, cardData.Mark, !isMine);
            playerCardObjects[actorNumber].Add(card);
            card.cardIndex = i;
            card.SetSelected(false);
        }
    }

    string GetPlayerName(int actorNumber)
    {
        return playerNames.TryGetValue(actorNumber, out string name) ? name : $"Player?";
    }

    int PlayerIndex(int actorNumber)
    {
        var players = PhotonNetwork.PlayerList;
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i].ActorNumber == actorNumber)
                return i;
        }
        return -1; // 見つからなかった場合
    }

    [PunRPC]
    void SetGazeVisibilityRPC(bool visible)
    {
        if (uiManager != null)
        {
            uiManager.ShowGazePoint(visible);
        }
    }

    [PunRPC]
    void HideWinnerRPC()
    {
        uiManager.HideWinner();
    }


    [PunRPC]
    void AnnounceWinners(int[] winnerActorNumbers)
    {
        uiManager.ShowWinner(winnerActorNumbers); // 勝者を表示
    }


    public int GetDisplayIndex(int actorNumber)
    {
        var players = PhotonNetwork.PlayerList;
        int myIndex = -1;
        int targetIndex = -1;

        for (int i = 0; i < players.Length; i++)
        {
            if (players[i].ActorNumber == PhotonNetwork.LocalPlayer.ActorNumber)
                myIndex = i;
            if (players[i].ActorNumber == actorNumber)
                targetIndex = i;
        }

        if (myIndex == -1 || targetIndex == -1) return -1;

        int relativeIndex = (targetIndex - myIndex + players.Length) % players.Length;
        return relativeIndex;
    }

    void PrepareNextRound()
    {
        if (isGameOver)
        {
            Debug.Log("ゲーム終了状態なので次のラウンドには進みません。");
            return;
        }

        Debug.Log("=== 次のラウンド準備中 ===");

        photonView.RPC(nameof(SetGazePhaseRPC), RpcTarget.All, "Reset");
        photonView.RPC(nameof(SetGazeVisibilityRPC), RpcTarget.All, true);

        // カードオブジェクトをすべて削除
        foreach (var kvp in playerCardObjects)
        {
            foreach (var c in kvp.Value)
                Destroy(c.gameObject);
        }
        playerCardObjects.Clear();

        // 状態の初期化
        playerHoleCards.Clear();
        foldedPlayers.Clear();
        actedPlayers.Clear();
        finishedDrawPlayers.Clear();
        currentParticipants.Clear();
        selectedCardIndices.Clear();

        int actor = PhotonNetwork.LocalPlayer.ActorNumber;
        selectedCardIndices.Remove(actor); // ← 念のため、ローカルプレイヤー分も削除

        currentStage = GameStage.InitialBetting;

        // デッキをリセット
        InitDeck();
        ShuffleDeck();

        // ラウンドカウントと終了判定
        if (PhotonNetwork.IsMasterClient)
        {
            currentRound++;
            if (currentRound > maxRounds)
            {
                Debug.Log("最大ラウンドに達したためゲームを終了します");
                EndGame();
                return;
            }
            photonView.RPC(nameof(SyncRoundInfoRPC), RpcTarget.All, currentRound, maxRounds, winPointThreshold);
        }

        AppendGameLog($"=== 第{currentRound}ラウンド開始 ===");
         if (PhotonNetwork.IsMasterClient)
        {
            int[] actors = playerPoints.Keys.ToArray();
            int[] values = actors.Select(a => playerPoints[a]).ToArray();
            photonView.RPC(nameof(SyncPlayerPointsRPC), RpcTarget.All, actors, values);
        }

        // ステージ開始（参加or降りる）
        DealHoleCards();
        currentStage = GameStage.Draw;
        if (PhotonNetwork.IsMasterClient)
        {
            photonView.RPC(nameof(RunStartDrawPhase), RpcTarget.All);
        }
    }

    [PunRPC]
    void AnnounceFinalWinner(int[] winnerActors, int maxPoint)
    {
        string names = string.Join(", ", winnerActors.Select(GetPlayerName));
        AppendGameLog($"{names} が規定ポイント {maxPoint} に到達し、勝利しました！");

        uiManager.ShowWinner(winnerActors);
        isGameOver = true;
    }

    [PunRPC]
    void AnnounceNoWinner()
    {
        if (!isGameOver && currentRound < maxRounds)
        {
            uiManager.ShowNoWinnerMessageTemporarily("全員フォールド：勝者なし", 5f);
        }
        else
        {
            uiManager.ShowNoWinnerMessage("全員フォールド：勝者なし");
        }
    }

    void EndGame()
    {
        int maxPoints = playerPoints.Values.Max();
        var winners = playerPoints
            .Where(kv => kv.Value == maxPoints)
            .Select(kv => kv.Key)
            .ToList();

        photonView.RPC(nameof(AnnounceFinalWinner), RpcTarget.All, winners.ToArray(), maxPoints);
        isGameOver = true;

        Debug.Log($"ゲーム終了！勝者: {string.Join(",", winners)} ポイント: {maxPoints}");
    }

}
