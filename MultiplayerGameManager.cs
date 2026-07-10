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
    public Button CheckButton, CallButton, RaiseButton, FoldButton, AllInButton;
    public InputField RaiseInput;

    public GameObject cardPrefab; // Card prefab（Inspectorでセット）
    public Transform boardCardParent;  // ボードに表示するカードの親Transform
    public Transform[] playerCardParents; // プレイヤーごとの手札表示用親Transform配列（人数分）

    // 既存フィールドに加えて↓を追加
    [SerializeField]
    private Text[] playerNameTexts; // インスペクターで4人分のTextをセット

    [SerializeField]
    private int maxRounds = 10; // Inspectorで設定できるようにする

    private int currentRound = 1;

    private List<Card.Data> deck;
    private Dictionary<int, List<Card.Data>> playerHoleCards = new();
    private List<Card.Data> boardCards = new();
    private Dictionary<int, int> playerChips = new();
    private Dictionary<int, int> playerBets = new();
    private List<int> foldedPlayers = new();
    private HashSet<int> actedPlayers = new(); // アクション済みプレイヤー
    private Dictionary<int, string> playerNames = new();

    private Dictionary<int, int> playerDisplayIndexByActor = new();


    private bool waitingForNextRound = false;

    // プレイヤーID → 表示中のカードオブジェクト（2枚）
    private Dictionary<int, List<Card>> playerCardObjects = new Dictionary<int, List<Card>>();

    // ボードに表示中のカードオブジェクト
    private List<Card> boardCardObjects = new List<Card>();

    [SerializeField]
    private PokerHandEvaluator handEvaluator;
    [SerializeField] private PokerUIManager uiManager;  // ← これを追加

    private int currentTurnIndex = 0;
    private int smallBlindAmount = 10;
    private int bigBlindAmount = 20;
    private int currentMaxBet = 0;
    private bool isAllInRound = false;
    private int potAmount = 0;  // ✅ 累積ポット金額（常にこれをポット表示に使う）
    
    int smallBlindActor = -1;
    int bigBlindActor = -1;


    private enum GameStage { PreFlop, Flop, Turn, River, Showdown }
    private GameStage currentStage = GameStage.PreFlop;

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
        if (PhotonNetwork.IsMasterClient && waitingForNextRound)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                waitingForNextRound = false;
                photonView.RPC(nameof(PrepareNextRoundRPC), RpcTarget.All);
            }
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
        Debug.Log("StartGame() called from Launcher!");

        if (PhotonNetwork.IsMasterClient)
        {
            currentRound = 1; // ラウンド数を初期化
            InitializeGame();
        }
    }

    [PunRPC]
    void SyncBetInfo(int actorNumber, int chips, int bet)
    {
        playerChips[actorNumber] = chips;
        playerBets[actorNumber] = bet;

        uiManager.UpdateChipDisplay(playerChips);

        // ✅ Pot表示は potAmount を使う
        uiManager.UpdatePot(potAmount);
    }

    // 自分の現在ベット額
    public int GetMyBet()
    {
        return playerBets.GetValueOrDefault(PhotonNetwork.LocalPlayer.ActorNumber, 0);
    }

    // コールに必要な額
    public int GetCallAmount()
    {
        int myBet = GetMyBet();
        return Mathf.Max(0, currentMaxBet - myBet);
    }

    void InitializeGame()
    {
        // プレイヤー名を決定（MasterClientだけ）
        List<int> actorNumbers = new();
        List<string> names = new();

        for (int i = 0; i < PhotonNetwork.PlayerList.Length; i++)
        {
            var player = PhotonNetwork.PlayerList[i];
            string name = $"Player{i + 1}";
            playerNames[player.ActorNumber] = name;

            actorNumbers.Add(player.ActorNumber);
            names.Add(name);
        }

        // 全クライアントに同期
        photonView.RPC(nameof(SyncPlayerNamesRPC), RpcTarget.All, actorNumbers.ToArray(), names.ToArray());

        Debug.Log($"InitializeGame呼ばれたよ。uiManager is {(uiManager == null ? "null" : "NOT null")}");

        if (uiManager == null)
        {
            Debug.LogError("InitializeGame中にuiManagerがnullです！！");
            return;
        }

        Debug.Log($"uiManagerのGameObject.activeSelf = {uiManager.gameObject.activeSelf}");
        Debug.Log($"uiManagerのGameObject.activeInHierarchy = {uiManager.gameObject.activeInHierarchy}");
        // 変更後（全員のUIを非表示にする）
        photonView.RPC(nameof(HideWinnerRPC), RpcTarget.All);
        photonView.RPC(nameof(InitDeckRPC), RpcTarget.All);

        DealHoleCards();
        // 初回スタート時（StartGame内）
        photonView.RPC(nameof(AssignBlindsRPC), RpcTarget.All, true);
        StartFirstTurn();
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
        if (!PhotonNetwork.IsMasterClient) return; // ★Masterのみカード配布
        AppendGameLog("カードが配られました");
        foreach (var p in PhotonNetwork.PlayerList)
        {
            var cards = DrawCards(2);
            playerHoleCards[p.ActorNumber] = cards;

            // ★ int配列 [number1, mark1, number2, mark2] に変換して送信
            int[] serializedCards = new int[4];
            serializedCards[0] = cards[0].Number;
            serializedCards[1] = (int)cards[0].Mark;
            serializedCards[2] = cards[1].Number;
            serializedCards[3] = (int)cards[1].Mark;

            photonView.RPC(nameof(SyncPlayerHoleCards), RpcTarget.All, p.ActorNumber, serializedCards);
        }
    }

    [PunRPC]
    void SyncPlayerHoleCards(int actorNumber, int[] serializedCards)
    {
        List<Card.Data> cardList = new()
    {
        new Card.Data { Number = serializedCards[0], Mark = (Mark)serializedCards[1] },
        new Card.Data { Number = serializedCards[2], Mark = (Mark)serializedCards[3] }
    };

        playerHoleCards[actorNumber] = cardList;

        ShowPlayerCards(actorNumber);
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

    // [PunRPC]化したバージョン
    [PunRPC]
    void AssignBlindsRPC(bool isFirstRound)
    {
        var players = PhotonNetwork.PlayerList;

        foreach (var p in players)
        {
            int actor = p.ActorNumber;
            if (isFirstRound)
            {
                playerChips[actor] = 1000;
            }

            playerBets[actor] = 0;
        }

        // 💡 チップがあるプレイヤーだけでブラインド対象を選ぶ
        var activePlayers = players
            .Where(p => playerChips.GetValueOrDefault(p.ActorNumber, 0) > 0)
            .ToList();

        if (activePlayers.Count < 2)
        {
            Debug.Log("ブラインド設定：アクティブプレイヤーが1人以下。勝者確定に移行。");

            if (activePlayers.Count == 1)
            {
                int winner = activePlayers[0].ActorNumber;
                photonView.RPC(nameof(HandleFoldWin), RpcTarget.All, winner);
            }

            return;
        }

        int sb = activePlayers[0].ActorNumber;
        int bb = activePlayers[1].ActorNumber;

        smallBlindActor = sb;
        bigBlindActor = bb;

        playerChips[sb] = Mathf.Max(playerChips[sb] - smallBlindAmount, 0);
        playerChips[bb] = Mathf.Max(playerChips[bb] - bigBlindAmount, 0);

        playerBets[sb] = Mathf.Min(smallBlindAmount, playerChips[sb]);
        playerBets[bb] = Mathf.Min(bigBlindAmount, playerChips[bb]);

        currentMaxBet = playerBets[bb];

        // 同期
        photonView.RPC(nameof(SyncBetInfo), RpcTarget.All, sb, playerChips[sb], playerBets[sb]);
        photonView.RPC(nameof(SyncBetInfo), RpcTarget.All, bb, playerChips[bb], playerBets[bb]);

        uiManager.UpdateChipDisplay(playerChips);
    }



    void StartFirstTurn()
    {
        int totalPlayers = PhotonNetwork.PlayerList.Length;

        // 💡 現在のステージに応じてスタート位置を決める
        int startActor;
        if (totalPlayers == 2)
        {
            // ヘッズアップでは SB が先手
            startActor = smallBlindActor;
        }
        else if (currentStage == GameStage.PreFlop)
        {
            // プリフロップでは BB の左から
            int bbIndex = System.Array.FindIndex(PhotonNetwork.PlayerList, p => p.ActorNumber == bigBlindActor);
            startActor = PhotonNetwork.PlayerList[(bbIndex + 1) % totalPlayers].ActorNumber;
        }
        else
        {
            // フロップ以降では SB の左から
            int sbIndex = System.Array.FindIndex(PhotonNetwork.PlayerList, p => p.ActorNumber == smallBlindActor);
            startActor = PhotonNetwork.PlayerList[(sbIndex + 1) % totalPlayers].ActorNumber;
        }

        // 🔄 ターン可能なプレイヤーを startActor から探す
        int startIndex = System.Array.FindIndex(PhotonNetwork.PlayerList, p => p.ActorNumber == startActor);

        for (int i = 0; i < totalPlayers; i++)
        {
            int nextIndex = (startIndex + i) % totalPlayers;
            int actorNumber = PhotonNetwork.PlayerList[nextIndex].ActorNumber;

            bool isFolded = foldedPlayers.Contains(actorNumber);
            int chips = playerChips.GetValueOrDefault(actorNumber, 0);
            int bet = playerBets.GetValueOrDefault(actorNumber, 0);
            bool hasActed = actedPlayers.Contains(actorNumber);
            bool isAllIn = chips <= 0;

            bool canAct = !isFolded && !isAllIn && (!hasActed || bet < currentMaxBet);

            if (canAct)
            {
                currentTurnIndex = nextIndex;

                Photon.Realtime.Player targetPlayer = PhotonPlayerFromActor(actorNumber);
                if (targetPlayer != null)
                {
                    photonView.RPC(nameof(DisableAllActionButtons), RpcTarget.All);
                    photonView.RPC(nameof(SetTurnSync), targetPlayer, actorNumber, currentMaxBet, bet);
                }
                return;
            }
        }

        Debug.LogWarning("StartFirstTurn: 回せるプレイヤーがいないためステージ進行");
        AdvanceStage();
    }


    [PunRPC]
    void SetTurn(int actorNumber)
    {
        Debug.Log($"SetTurn受信：自分={PhotonNetwork.LocalPlayer.ActorNumber}, 現在ターン={actorNumber}");

        bool isMyTurn = PhotonNetwork.LocalPlayer.ActorNumber == actorNumber;
        uiManager.SetMyTurn(isMyTurn);



        // ✅ アクションボタン更新
        if (isMyTurn)
        {
            int currentBet = playerBets.GetValueOrDefault(actorNumber, 0);
            int callAmount = currentMaxBet - currentBet;

            int minRaise = bigBlindAmount;
            int minTotalBet = callAmount + minRaise;

            int playerChipsAmount = playerChips.GetValueOrDefault(actorNumber, 0);
            int maxRaise = playerChipsAmount;

            // ✅ チェック条件を正確に
            bool canCheck = currentBet == currentMaxBet;

            uiManager.UpdateActionButtons(
                canCheck: canCheck,
                callAmount: callAmount,
                minRaiseAmount: minTotalBet,
                maxRaiseAmount: maxRaise
            );

            // ✅ これ → ちゃんとベットUIも更新する
            uiManager.UpdateBetInfo(currentMaxBet, currentBet);
        }
    }
    [PunRPC]
    void SetTurnSync(int actorNumber, int syncedMaxBet, int syncedPlayerBet)
    {
        currentMaxBet = syncedMaxBet;

        bool isMyTurn = PhotonNetwork.LocalPlayer.ActorNumber == actorNumber;
        uiManager.SetMyTurn(isMyTurn);

        if (isMyTurn)
        {
            int currentBet = syncedPlayerBet;  // 🔥 これを使う
            int callAmount = Mathf.Max(0, currentMaxBet - currentBet);

            int minRaise = bigBlindAmount;
            int minTotalBet = callAmount + minRaise;
            int playerChipsAmount = playerChips.GetValueOrDefault(actorNumber, 0);

            bool canCheck = (callAmount == 0);

            uiManager.UpdateActionButtons(
                canCheck: canCheck,
                callAmount: callAmount,
                minRaiseAmount: minTotalBet,
                maxRaiseAmount: playerChipsAmount
            );

            uiManager.UpdateBetInfo(currentMaxBet, currentBet);
        }
    }

    [PunRPC]
    void DisableAllActionButtons()
    {
        uiManager.SetMyTurn(false); // ← EnableActionButtons(false) を内部で呼ぶ
    }

    [PunRPC]
    void ReceivePlayerActionRequest(string action, int amount, int senderActorNumber)
    {
        Debug.Log($"受信：{senderActorNumber}が{action}要求 amount={amount}");

        if (PhotonNetwork.IsMasterClient)
        {
            ReceivePlayerAction(action, amount, senderActorNumber); // ★直接呼ぶ
        }
    }

    [PunRPC]
    void AddLogRPC(string message)
    {
        uiManager.AddLog(message);
    }

    [PunRPC]
    void ReceivePlayerAction(string action, int amount, int actorNumber)
    {
        // 🔥 履歴ログ用
        string actorName = $"P{PlayerIndex(actorNumber) + 1}";
        string logMsg = action switch
        {
            "fold" => $"{actorName}: フォールド",
            "call" => $"{actorName}: コール",
            "check" => $"{actorName}: チェック",
            "raise" => $"{actorName}: レイズ {amount}",
            "allin" => $"{actorName}: オールイン ({playerBets[actorNumber]})",
            _ => $"{actorName}: 不明な行動"
        };
        photonView.RPC(nameof(AddLogRPC), RpcTarget.All, logMsg);
        AppendGameLog(logMsg); // ★ログファイルに書き込み

        // 🔐 辞書安全対策
        if (!playerChips.ContainsKey(actorNumber)) playerChips[actorNumber] = 0;
        if (!playerBets.ContainsKey(actorNumber)) playerBets[actorNumber] = 0;
        if (!playerChips.ContainsKey(actorNumber)) return;

        int chipsPaidThisAction = 0;

        if (action == "fold")
        {
            // 🔥 MasterClientは即時追加（確実）
            if (!foldedPlayers.Contains(actorNumber))
                foldedPlayers.Add(actorNumber);

            // 🔥 すべてのクライアントに同期
            photonView.RPC(nameof(SyncFoldedPlayer), RpcTarget.Others, actorNumber);
        }
        else if (action == "call")
        {
            int currentBet = playerBets.GetValueOrDefault(actorNumber, 0);
            int callAmount = currentMaxBet - currentBet;
            callAmount = Mathf.Min(callAmount, playerChips[actorNumber]);
            playerChips[actorNumber] -= callAmount;
            playerChips[actorNumber] = Mathf.Max(playerChips[actorNumber], 0);
            playerBets[actorNumber] = currentBet + callAmount;

            chipsPaidThisAction = callAmount;

            if (playerChips[actorNumber] == 0)
            {
                isAllInRound = true;
            }
        }
        else if (action == "raise")
        {
            int currentBet = playerBets.GetValueOrDefault(actorNumber, 0);
            int totalBet = currentMaxBet + amount;
            int raiseAmount = totalBet - currentBet;
            raiseAmount = Mathf.Min(raiseAmount, playerChips[actorNumber]);
            playerChips[actorNumber] -= raiseAmount;
            playerChips[actorNumber] = Mathf.Max(playerChips[actorNumber], 0);
            playerBets[actorNumber] = currentBet + raiseAmount;
            currentMaxBet = playerBets[actorNumber];

            chipsPaidThisAction = raiseAmount;

            if (playerChips[actorNumber] == 0)
            {
                isAllInRound = true;
            }

            // 他のプレイヤーのacted状態をリセット
            foreach (var p in PhotonNetwork.PlayerList)
            {
                int pActor = p.ActorNumber;
                if (pActor != actorNumber && !foldedPlayers.Contains(pActor) && playerChips.GetValueOrDefault(pActor, 0) > 0)
                {
                    actedPlayers.Remove(pActor);
                }
            }
        }
        else if (action == "allin")
        {
            int allInAmount = playerChips[actorNumber];
            playerBets[actorNumber] += allInAmount;
            playerChips[actorNumber] -= allInAmount;
            playerChips[actorNumber] = Mathf.Max(playerChips[actorNumber], 0);

            chipsPaidThisAction = allInAmount;

            if (playerBets[actorNumber] > currentMaxBet)
            {
                currentMaxBet = playerBets[actorNumber];
            }
            isAllInRound = true;
        }
        else if (action == "check")
        {
            // ベット無し
            Debug.Log($"Player {actorNumber} チェック");
        }

        // 🟢 ポットに加算
        if (chipsPaidThisAction > 0)
        {
            potAmount += chipsPaidThisAction;
            photonView.RPC(nameof(SyncPotAmount), RpcTarget.All, potAmount);
        }

        // アクション済みに追加
        actedPlayers.Add(actorNumber);

        // ✅ 各プレイヤーのチップ・ベット同期
        photonView.RPC(nameof(SyncBetInfo), RpcTarget.All, actorNumber,
            playerChips[actorNumber],
            playerBets[actorNumber]);

        // ✅ currentMaxBetも同期
        photonView.RPC(nameof(SyncMaxBet), RpcTarget.All, currentMaxBet);

        if (PhotonNetwork.LocalPlayer.ActorNumber == actorNumber)
        {
            uiManager.SetMyTurn(false);
        }

        if (PhotonNetwork.IsMasterClient)
        {
            // CheckNextAction() を 0.05秒後に実行
            Invoke(nameof(DeferredCheckNextAction), 0.1f);
        }

    }

    void DeferredCheckNextAction()
    {
        CheckNextAction();
    }


    [PunRPC]
    void SyncFoldedPlayer(int actorNumber)
    {
        if (!foldedPlayers.Contains(actorNumber))
        {
            foldedPlayers.Add(actorNumber);
            Debug.Log($"[SyncFoldedPlayer] 受信: {actorNumber} を foldedPlayers に追加");
        }
    }

    [PunRPC]
    void SyncPotAmount(int newPotAmount)
    {
        potAmount = newPotAmount;
        uiManager.UpdatePot(potAmount); // UI更新処理（なければ作成）
    }

    [PunRPC]
    void SyncMaxBet(int maxBet)
    {
        currentMaxBet = maxBet;
        uiManager.UpdatePot(potAmount);  // ✅ これで常に正しいポットを表示
    }

    // チェックボタン
    public void OnCheckButton()
    {
        photonView.RPC("ReceivePlayerActionRequest", RpcTarget.MasterClient, "check", 0, PhotonNetwork.LocalPlayer.ActorNumber);
    }

    // コールボタン
    public void OnCallButton()
    {
        photonView.RPC("ReceivePlayerActionRequest", RpcTarget.MasterClient, "call", 0, PhotonNetwork.LocalPlayer.ActorNumber);
    }

    public void OnRaiseButton()
    {
        int raiseAmount = 0;
        if (int.TryParse(RaiseInput.text, out raiseAmount))
        {
            var gameManager = MultiplayerGameManager.instance;
            int actorNumber = PhotonNetwork.LocalPlayer.ActorNumber;
            int currentBet = gameManager.GetPlayerBet(actorNumber);
            int callAmount = gameManager.CurrentMaxBet - currentBet;

            int minRaise = gameManager.BigBlindAmount; // 最低レイズ額はBB
            int minTotalBet = callAmount + minRaise;

            int playerChipCount = gameManager.GetPlayerChips(actorNumber);

            if (raiseAmount > playerChipCount)
            {
                Debug.LogWarning($"チップ不足！最大 {playerChipCount} までです");
                return;
            }

            if (raiseAmount < minTotalBet)
            {
                Debug.LogWarning($"レイズ額が不足！最低 {minTotalBet} 必要です");
                return;
            }

            gameManager.photonView.RPC("ReceivePlayerActionRequest", RpcTarget.MasterClient, "raise", raiseAmount, actorNumber);
        }
        else
        {
            Debug.LogWarning("レイズ額の入力が不正です");
        }
    }



    public int GetPlayerChips(int actorNumber)
    {
        return playerChips.TryGetValue(actorNumber, out var chips) ? chips : 0;
    }

    // 現在のプレイヤーのベット額を取得
    public int GetPlayerBet(int actorNumber)
    {
        return playerBets.TryGetValue(actorNumber, out var bet) ? bet : 0;
    }

    // 現在の最大ベット額を取得
    public int CurrentMaxBet => currentMaxBet;

    // BB額を取得
    public int BigBlindAmount => bigBlindAmount;

    // フォールドボタン
    public void OnFoldButton()
    {
        photonView.RPC("ReceivePlayerActionRequest", RpcTarget.MasterClient, "fold", 0, PhotonNetwork.LocalPlayer.ActorNumber);
    }

    // オールインボタン
    public void OnAllInButton()
    {
        photonView.RPC("ReceivePlayerActionRequest", RpcTarget.MasterClient, "allin", 0, PhotonNetwork.LocalPlayer.ActorNumber);
    }

    void CheckNextAction()
    {
        // 1) まだ行動残しがいるならターンを回す
        if (HasPendingActionPlayers())
        {
            ProceedToNextTurn();
            return;
        }

        // 2) 全員アクション済み or オールインラウンド終了なら次ステージ
        if (AllPlayersActed() || (isAllInRound && !HasPendingActionPlayers()))
        {
            // 2‐1) ショーダウンに入るか…
            if (currentStage == GameStage.Showdown || isAllInRound)
            {
                AdvanceStage();    // → HandleShowdown()
            }
            else
            {
                // フロップ→ターン→リバー の進行
                AdvanceStage();
            }
            return;
        }

        // 3) それ以外はまだ全員揃っていないのでフォールド勝利判定
        //    フォールドしていないプレイヤーだけカウント（チップ０は無視）
        var remained = PhotonNetwork.PlayerList
            .Where(p => !foldedPlayers.Contains(p.ActorNumber))
            .ToList();

        if (remained.Count == 1)
        {
            photonView.RPC(nameof(HandleFoldWin), RpcTarget.All, remained[0].ActorNumber);
            return;
        }

        // 4) さらに何かあればターン継続
        ProceedToNextTurn();
    }

    void ProceedToNextTurn()
    {
        int totalPlayers = PhotonNetwork.PlayerList.Length;

        for (int i = 1; i <= totalPlayers; i++)
        {
            int nextIndex = (currentTurnIndex + i) % totalPlayers;
            int actorNumber = PhotonNetwork.PlayerList[nextIndex].ActorNumber;

            bool isFolded = foldedPlayers.Contains(actorNumber);
            int chips = Mathf.Max(0, playerChips.GetValueOrDefault(actorNumber, 0));
            int bet = playerBets.GetValueOrDefault(actorNumber, 0);
            bool hasActed = actedPlayers.Contains(actorNumber);
            bool isAllIn = chips <= 0;

            // ✅ 安定した元のロジックに戻す
            bool canAct = !isFolded && !isAllIn && (!hasActed || bet < currentMaxBet);

            if (canAct)
            {
                currentTurnIndex = nextIndex;
                Photon.Realtime.Player targetPlayer = PhotonPlayerFromActor(actorNumber);
                if (targetPlayer != null)
                {
                    photonView.RPC(nameof(DisableAllActionButtons), RpcTarget.All);
                    photonView.RPC(nameof(SetTurnSync), targetPlayer, actorNumber, currentMaxBet, bet);
                }
                return;
            }
        }

        Debug.LogWarning("ProceedToNextTurn: 回せるプレイヤーがいないためステージ進行");
        AdvanceStage();
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

    [PunRPC]
    void HandleFoldWin(int winnerActorNumber)
    {
        Debug.Log($"フォールド勝利処理: {winnerActorNumber}");

        // 💰 ポット額取得と加算
        int totalPot = potAmount; // ← これで確実
        if (!playerChips.ContainsKey(winnerActorNumber))
            playerChips[winnerActorNumber] = 0;

        playerChips[winnerActorNumber] += totalPot;

        // ✅ 全員にチップを同期
        photonView.RPC(nameof(SyncChipsToAll), RpcTarget.All,
            playerChips.Keys.ToArray(),
            playerChips.Values.ToArray());

        // ✅ 勝者UI表示
        uiManager.ShowWinner(new int[] { winnerActorNumber });

        // ✅ ポットUIクリア
        potAmount = 0;
        uiManager.UpdatePot(0);

        // ✅ ベット情報も初期化
        foreach (var key in playerBets.Keys.ToList())
            playerBets[key] = 0;

        // ✅ 待機フラグ（Masterのみ）
        if (PhotonNetwork.IsMasterClient)
            waitingForNextRound = true;

        // ⏳ 次ラウンドへ
        Invoke(nameof(PrepareNextRound), 5f);
    }

    bool HasPendingActionPlayers()
    {
        return PhotonNetwork.PlayerList
            .Where(p =>
                !foldedPlayers.Contains(p.ActorNumber) &&
                !IsAllIn(p.ActorNumber) // オールイン者は除外
            )
            .Any(p =>
                !actedPlayers.Contains(p.ActorNumber) // まだ行動してない
                || playerBets.GetValueOrDefault(p.ActorNumber, 0) < currentMaxBet // ベットが不足
            );
    }

    bool IsAllIn(int actorNumber)
    {
        return playerChips.GetValueOrDefault(actorNumber, 0) == 0;
    }


    bool AllPlayersActed()
    {
        return PhotonNetwork.PlayerList
            .Where(p =>
                !foldedPlayers.Contains(p.ActorNumber) &&
                playerChips.GetValueOrDefault(p.ActorNumber, 0) > 0 // チップ残ってる
            )
            .All(p =>
                actedPlayers.Contains(p.ActorNumber) // アクション済み
                && playerBets.GetValueOrDefault(p.ActorNumber, 0) == currentMaxBet // ベットも揃ってる
            );
    }


    void AdvanceStage()
    {
        actedPlayers.Clear();

        if (currentStage == GameStage.Showdown || isAllInRound)
        {
            RevealAllRemainingBoard();

            // ⏳ 5秒後にショーダウン開始（緊張演出）
            if (PhotonNetwork.IsMasterClient)
            {
                Invoke(nameof(DelayedShowdown), 5f);
            }
            isAllInRound = false;
            return;
        }

        currentStage++;

        if (currentStage == GameStage.Showdown)
        {
            RevealAllRemainingBoard();
            if (PhotonNetwork.IsMasterClient)
            {
                Invoke(nameof(DelayedShowdown), 5f);
            }
            isAllInRound = false;
            return;
        }

        if (currentStage == GameStage.Flop)
        {
            RevealBoardCards(3);
        }
        else if (currentStage == GameStage.Turn || currentStage == GameStage.River)
        {
            RevealBoardCards(1);
        }

        ResetBets();
        StartFirstTurn();
    }

    void DelayedShowdown()
    {
        photonView.RPC(nameof(HandleShowdown), RpcTarget.All);
    }

    void RevealBoardCards(int count)
    {
        var newCards = DrawCards(count);
        boardCards.AddRange(newCards);
        ShowBoardCards();

        // 全プレイヤーに同期
        if (PhotonNetwork.IsMasterClient)
        {
            int[] serialized = boardCards
                .SelectMany(c => new int[] { c.Number, (int)c.Mark })
                .ToArray();

            photonView.RPC(nameof(SyncBoardCardsRPC), RpcTarget.Others, serialized);
        }
    }

    [PunRPC]
    void SyncBoardCardsRPC(int[] cardDataArray)
    {
        boardCards.Clear();
        for (int i = 0; i < cardDataArray.Length; i += 2)
        {
            boardCards.Add(new Card.Data
            {
                Number = cardDataArray[i],
                Mark = (Mark)cardDataArray[i + 1]
            });
        }
        ShowBoardCards();
    }

    void RevealAllRemainingBoard()
    {
        int need = 5 - boardCards.Count;
        if (need > 0)
        {
            var newCards = DrawCards(need);
            boardCards.AddRange(newCards);
            ShowBoardCards();

            if (PhotonNetwork.IsMasterClient)
            {
                int[] serialized = boardCards
                    .SelectMany(c => new int[] { c.Number, (int)c.Mark })
                    .ToArray();

                photonView.RPC(nameof(SyncBoardCardsRPC), RpcTarget.Others, serialized);
            }
        }
    }

    void ResetBets()
    {
        foreach (var p in PhotonNetwork.PlayerList)
        {
            playerBets[p.ActorNumber] = 0;
        }
        currentMaxBet = 0;
        isAllInRound = false;
    }

    [PunRPC]
    void HandleShowdown()
    {
        Debug.Log("=== ショーダウン突入 ===");

        Dictionary<int, PokerHandEvaluator.HandResult> handResults = new();

        // 各プレイヤーの役を判定（フォールド以外）
        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (foldedPlayers.Contains(player.ActorNumber)) continue;
            if (!playerHoleCards.ContainsKey(player.ActorNumber)) continue;

            List<Card.Data> hole = playerHoleCards[player.ActorNumber];
            List<Card.Data> fullHand = new(hole);
            fullHand.AddRange(boardCards);

            List<PokerHandEvaluator.Card> evalHand = fullHand
                .Select(CardUtils.ToEvalCard)
                .ToList();

            var handResult = handEvaluator.EvaluateHand(evalHand);
            handResults[player.ActorNumber] = handResult;

            Debug.Log($"Player {player.ActorNumber}：{handResult.handRank} [{string.Join(",", handResult.rankNumbers)}]");
        }

        // 有効な手札のプレイヤーがいない（全員フォールド済み）の場合
        if (handResults.Count == 0)
        {
            Debug.LogError("HandleShowdown: 有効なプレイヤーがいません（全員フォールド）");
            return;
        }

        // 最も強い手の情報を取得
        var bestStrength = handResults.Max(h => PokerHandEvaluator.GetHandStrength(h.Value.handRank));
        var bestRankNumbers = handResults
            .Where(h => PokerHandEvaluator.GetHandStrength(h.Value.handRank) == bestStrength)
            .Select(h => h.Value.rankNumbers)
            .OrderByDescending(r => r, new RankNumberComparer())
            .First();

        var bestHand = handResults
            .First(h => PokerHandEvaluator.GetHandStrength(h.Value.handRank) == bestStrength &&
                        new RankNumberComparer().Compare(h.Value.rankNumbers, bestRankNumbers) == 0)
            .Value.handRank;

        // ✅ 最終的な勝者を決定（フォールド者は含まれない）
        var finalWinners = handResults
            .Where(h => PokerHandEvaluator.GetHandStrength(h.Value.handRank) == bestStrength &&
                        new RankNumberComparer().Compare(h.Value.rankNumbers, bestRankNumbers) == 0)
            .Select(h => h.Key)
            .Where(actor => !foldedPlayers.Contains(actor)) // ← フォールド者除外
            .ToList();

        if (finalWinners.Count == 0)
        {
            Debug.LogError("HandleShowdown: 勝者がフォールド済みしかおらず分配できません");
            return;
        }

        Debug.Log($"勝者: {string.Join(",", finalWinners)}（役: {bestHand}）");

        // 💰 チップ分配・UI勝者表示
        DistributePot(finalWinners);
        photonView.RPC(nameof(AnnounceWinnersWithHand), RpcTarget.All, finalWinners.ToArray(), bestHand.ToString());

        // 🎯 MasterClientのみ待機状態へ
        if (PhotonNetwork.IsMasterClient)
        {
            waitingForNextRound = true;
        }
    }

    void DistributePot(List<int> winners)
    {
        int totalPot = potAmount;

        if (winners == null || winners.Count == 0)
        {
            Debug.LogError("DistributePot: 有効な勝者がいません（全員フォールド？）");
            return;
        }

        int share = totalPot / winners.Count;
        int remainder = totalPot % winners.Count;

        // 均等配分
        foreach (var winner in winners)
        {
            if (!playerChips.ContainsKey(winner))
                playerChips[winner] = 0;

            playerChips[winner] += share;
        }

        // 余りを順番に配分
        for (int i = 0; i < remainder; i++)
        {
            int winner = winners[i % winners.Count];
            playerChips[winner] += 1;
        }

        // 同期
        photonView.RPC(nameof(SyncChipsToAll), RpcTarget.All, playerChips.Keys.ToArray(), playerChips.Values.ToArray());

        Debug.Log($"ポット {totalPot} を {string.Join(",", winners)} が獲得（1人 {share}、余り {remainder} を順に配分）");

        // ベット情報リセット
        foreach (var actor in playerBets.Keys.ToList())
        {
            playerBets[actor] = 0;
        }

        // ポットもリセット
        potAmount = 0;
    }



    [PunRPC]
    void SyncChipsToAll(int[] actorNumbers, int[] chipAmounts)
    {
        for (int i = 0; i < actorNumbers.Length; i++)
        {
            playerChips[actorNumbers[i]] = chipAmounts[i];
        }

        // ✅ UI更新
        uiManager.UpdateChipDisplay(playerChips);
    }



    [PunRPC]
    void AnnounceWinnersWithHand(int[] winnerActorNumbers, string handName)
    {
        // フォールド済みプレイヤーを除外
        var validWinners = winnerActorNumbers
            .Where(actor => !foldedPlayers.Contains(actor))
            .ToArray();

        uiManager.ShowWinnerWithHand(validWinners, handName);
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
        // すでに表示中なら削除してクリア
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
        // playerCardParents[actorNumber] にカードを2枚生成して表示
        int parentIndex = GetDisplayIndex(actorNumber);
        if (parentIndex < 0 || parentIndex >= playerCardParents.Length) return;


        for (int i = 0; i < 2; i++)
        {
            var cardData = playerHoleCards[actorNumber][i];
            GameObject go = Instantiate(cardPrefab, playerCardParents[parentIndex]);
            Card card = go.GetComponent<Card>();
            // 自分の手札は表向き、他は裏向き
            card.SetCard(cardData.Number, cardData.Mark, !isMine);
            playerCardObjects[actorNumber].Add(card);
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

    void ShowBoardCards()
    {
        // いったん全部破棄してクリア
        foreach (var c in boardCardObjects)
            Destroy(c.gameObject);
        boardCardObjects.Clear();

        // boardCardParent に boardCards の枚数分カードを生成して表示
        for (int i = 0; i < boardCards.Count; i++)
        {
            var cardData = boardCards[i];
            GameObject go = Instantiate(cardPrefab, boardCardParent);
            Card card = go.GetComponent<Card>();
            card.SetCard(cardData.Number, cardData.Mark, false);
            boardCardObjects.Add(card);
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

    [PunRPC]
    void DealHoleCardsRPC()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        foreach (var p in PhotonNetwork.PlayerList)
        {
            var cards = DrawCards(2);
            playerHoleCards[p.ActorNumber] = cards;

            int[] serializedCards = new int[4];
            serializedCards[0] = cards[0].Number;
            serializedCards[1] = (int)cards[0].Mark;
            serializedCards[2] = cards[1].Number;
            serializedCards[3] = (int)cards[1].Mark;

            photonView.RPC(nameof(SyncPlayerHoleCards), RpcTarget.All, p.ActorNumber, serializedCards);
        }
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
        // 既にカードオブジェクトはClearしているが念のため
        foreach (var kvp in playerCardObjects)
        {
            foreach (var c in kvp.Value)
                Destroy(c.gameObject);
        }
        playerCardObjects.Clear();

        foreach (var c in boardCardObjects)
            Destroy(c.gameObject);
        boardCardObjects.Clear();
        Debug.Log("=== 次のラウンド準備中 ===");
        // 変更後（全員のUIを非表示にする）
        photonView.RPC(nameof(HideWinnerRPC), RpcTarget.All);
        uiManager.UpdateChipDisplay(playerChips); // チップを最新化する

        // --- 1. ボード・手札・ベットをリセット ---
        boardCards.Clear();
        playerBets.Clear();
        playerHoleCards.Clear();
        foldedPlayers.Clear();
        actedPlayers.Clear();
        currentMaxBet = 0;
        isAllInRound = false;
        potAmount = 0;

        // --- 2. デッキをリセット ---
        InitDeck();
        ShuffleDeck();

        // --- 3. ステージをプリフロップに戻す ---
        currentStage = GameStage.PreFlop;

        // ★ 全員にカード配布（RPC化）
        photonView.RPC(nameof(DealHoleCardsRPC), RpcTarget.All);

        // --- 6. ブラインド再設定 ---
        // PrepareNextRoundではfalse
        photonView.RPC(nameof(AssignBlindsRPC), RpcTarget.All, false);

        // --- ラウンドカウントと終了判定 ---
        if (PhotonNetwork.IsMasterClient)
        {
            currentRound++;

            if (currentRound > maxRounds)
            {
                Debug.Log("最大ラウンドに達したためゲームを終了します");
                EndGame();
                return;
            }
        }


        // --- 7. 最初のターンをスタート ---
        StartFirstTurn();
    }

    [PunRPC]
    void AnnounceFinalWinner(int[] winnerActorNumbers, int finalChipCount)
    {
        string chipInfo = $"{finalChipCount} チップ";
        uiManager.ShowWinnerWithHand(winnerActorNumbers, chipInfo);
    }


    void EndGame()
    {
        // 🔍 チップ数が最大のプレイヤーを検索
        int maxChips = playerChips.Values.Max();
        var winners = playerChips
            .Where(kv => kv.Value == maxChips)
            .Select(kv => kv.Key)
            .ToList();

        Debug.Log($"ゲーム終了！勝者: {string.Join(",", winners)} チップ: {maxChips}");

        // 🎉 UIに勝者表示（役名なしで表示）
        photonView.RPC(nameof(AnnounceFinalWinner), RpcTarget.All, winners.ToArray(), maxChips);

        // 🔚 MasterClientなら部屋を抜ける（必要なら）
        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.LeaveRoom();
        }
    }

}
