using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum Mark
{
    Heart,   // ハート
    Diamond, // ダイヤ
    Spade,   // スペード
    Club,    // クラブ (誤字修正済み)
}

public class Card : MonoBehaviour, IPointerClickHandler
{
    // カードのマークを表す列挙型

    // カードの表裏を判定するフラグ (true = 裏向き, false = 表向き)
    public bool IsReverse = false;

    public bool IsLarge = false;

    // カードの数値 (1〜13の範囲)
    [Range(1, 13)]
    public int Number = 1;
    public string m = "a";

    // 現在のマーク (デフォルトはハート)
    public Mark CurrentMark = Mark.Heart;

    public int cardIndex = -1; // 自分の手札の中でのインデックス（0〜4）
    private bool isSelected = false;


    // UI要素（Inspector でアサインする）
    [SerializeField] private Image cardImage; // カードの背景画像
    [SerializeField] private Text markText;   // マークのテキスト
    [SerializeField] private Text numberText; // 数字のテキスト

    public Sprite CardBackSprite; // 裏面の画像
    public Sprite CardFrontSprite; // 表面の画像

    public bool IsSelectable = true; // 交換フェーズ中のみ true

    // スクリプトが有効化されたとき（オブジェクトが生成されたとき）に実行
    private void Awake()
    {
        UpdateCard(); // カードの見た目を更新
    }

    /// <summary>
    /// カードの情報を設定する
    /// </summary>
    /// <param name="number">カードの数値 (1〜13)</param>
    /// <param name="mark">カードのマーク</param>
    /// <param name="isReverse">カードが裏向きかどうか</param>
    public void SetCard(int number, Mark mark, bool isReverse)
    {
        // 数値を 1〜13 の範囲に制限
        Number = Mathf.Clamp(number, 1, 13);

        // マークと裏表の状態を設定
        CurrentMark = mark;
        IsReverse = isReverse;

        // UIを更新
        UpdateCard();

    }



    /// <summary>
    /// カードの UI を更新する
    /// </summary>
    private void UpdateCard()
    {
        // カードの表裏の色を設定
        var image = GetComponent<Image>();

        // ✅ 裏向きなら裏面画像、表向きなら表面画像を設定
        if (IsReverse)
        {
            image.sprite = CardBackSprite;
        }
        else
        {
            image.sprite = CardFrontSprite;
        }

        // 数字やマークを非表示（裏向きのとき）
        foreach (Transform child in transform)
        {
            child.gameObject.SetActive(!IsReverse);
        }
        // カードのマークを設定
        if (markText != null)
        {
            switch (CurrentMark)
            {
                case Mark.Heart:
                    markText.text = "❤️"; // ハート
                    markText.color = Color.red;
                    m = "H";
                    break;
                case Mark.Diamond:
                    markText.text = "♦️"; // ダイヤ
                    markText.color = Color.red;
                    m = "D";
                    break;
                case Mark.Spade:
                    markText.text = "♠️"; // スペード
                    markText.color = Color.black;
                    m = "S";
                    break;
                case Mark.Club:
                    markText.text = "♣️"; // クラブ
                    markText.color = Color.black;
                    m = "C";
                    break;
            }
        }

        // カードの数値を設定
        if (numberText != null)
        {
            switch (Number)
            {
                case 1:
                    numberText.text = "A"; // エース
                    break;
                case 11:
                    numberText.text = "J"; // ジャック
                    break;
                case 12:
                    numberText.text = "Q"; // クイーン
                    break;
                case 13:
                    numberText.text = "K"; // キング
                    break;
                default:
                    numberText.text = Number.ToString(); // 2〜10の数字
                    break;
            }
        }


    }


    /// <summary>
    /// Unityエディタで値を変更したときに呼ばれる
    /// </summary>
    private void OnValidate()
    {
        UpdateCard(); // エディタ上で即座に変更を反映
    }

    public class Data
    {
        public Mark Mark;
        public int Number;
        public string m;

        public Data() { }

        public Data(Mark mark, int number)
        {
            this.Mark = mark;
            this.Number = number;
            this.m = mark.ToString().Substring(0, 1);
        }

        public override string ToString()
        {
            return $"{m}_{Number}";
        }

        public static Data FromString(string str)
        {
            // 例: "H_10"
            string[] parts = str.Split('_');
            string markStr = parts[0];
            int number = int.Parse(parts[1]);

            Mark mark = markStr switch
            {
                "H" => Mark.Heart,
                "D" => Mark.Diamond,
                "S" => Mark.Spade,
                "C" => Mark.Club,
                _ => throw new System.Exception("Invalid mark")
            };

            return new Data(mark, number);
        }
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        if (IsReverse || !IsSelectable) return; // ← 追加！

        isSelected = !isSelected;
        UpdateVisual();

        MultiplayerGameManager.instance?.OnCardClicked(cardIndex);
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        UpdateVisual();
    }


    public void SetSelectable(bool selectable)
    {
        IsSelectable = selectable;

        // 無効化時は選択も解除して白色に
        if (!selectable)
        {
            isSelected = false;
            UpdateVisual();
        }
    }


    public void UpdateVisual()
    {
        // 背景色変更（選択中は水色）
        if (!IsReverse)
        {
            var image = GetComponent<Image>();
            image.color = isSelected ? Color.cyan : Color.white;
        }
    }

}
