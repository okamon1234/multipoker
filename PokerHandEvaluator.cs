using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PokerHandEvaluator : MonoBehaviour
{
    // 役のランク（強さの順）
    public enum HandRank
    {
        HighCard,
        OnePair,
        TwoPair,
        ThreeOfAKind,
        Straight,
        Flush,
        FullHouse,
        FourOfAKind,
        StraightFlush,
        RoyalFlush
    }

    // カード情報（数字とスート）
    public class Card
    {
        public int number;  // 2～14（J=11, Q=12, K=13, A=14）
        public string suit; // "S", "H", "D", "C"

        public Card(int num, string s)
        {
            number = num;
            suit = s;
        }
    }

    // 役の判定結果
    public class HandResult
    {
        public HandRank handRank;
        public List<int> rankNumbers; // 勝敗比較用（キッカー含む）

        public HandResult(HandRank rank, List<int> ranks)
        {
            handRank = rank;
            rankNumbers = ranks;
        }
    }

    // メイン関数：7枚中から最も強い5枚を選んで役を判定
    public HandResult EvaluateHand(List<Card> cards)
    {
        if (cards == null || cards.Count < 5)
            return new HandResult(HandRank.HighCard, new List<int>());

        HandResult bestHand = null;

        // 7枚中から5枚の全ての組み合わせをチェック（21通り）
        foreach (var combo in GetCombinations(cards, 5))
        {
            var result = EvaluateFiveCardHand(combo);
            if (bestHand == null ||
                GetHandStrength(result.handRank) > GetHandStrength(bestHand.handRank) ||
                (GetHandStrength(result.handRank) == GetHandStrength(bestHand.handRank) &&
                 CompareRankNumbers(result.rankNumbers, bestHand.rankNumbers) > 0))
            {
                bestHand = result;
            }
        }

        return bestHand;
    }

    // サブ関数：5枚のみの手札で役判定
    private HandResult EvaluateFiveCardHand(List<Card> cards)
    {
        var numbers = cards.Select(c => c.number).ToList();
        var suits = cards.Select(c => c.suit).ToList();

        // フラッシュ判定
        var flushSuitGroup = cards.GroupBy(c => c.suit).FirstOrDefault(g => g.Count() >= 5);
        bool isFlush = flushSuitGroup != null;

        // ストレートフラッシュ or ロイヤルフラッシュ判定
        if (isFlush)
        {
            var flushCards = flushSuitGroup.Select(c => c.number).ToList();
            if (CheckStraight(flushCards, out List<int> sfNumbers))
            {
                if (sfNumbers[0] == 14)
                    return new HandResult(HandRank.RoyalFlush, sfNumbers);
                return new HandResult(HandRank.StraightFlush, sfNumbers);
            }
        }

        // 同じ数字ごとにまとめる
        var grouped = cards.GroupBy(c => c.number)
                           .OrderByDescending(g => g.Count())
                           .ThenByDescending(g => g.Key)
                           .ToList();

        // フォーカード
        if (grouped[0].Count() == 4)
            return new HandResult(HandRank.FourOfAKind, new List<int> { grouped[0].Key, grouped[1].Key });

        // フルハウス
        if (grouped[0].Count() == 3 && grouped.Count > 1 && grouped[1].Count() >= 2)
            return new HandResult(HandRank.FullHouse, new List<int> { grouped[0].Key, grouped[1].Key });

        // フラッシュ
        if (isFlush)
        {
            var flushTop5 = flushSuitGroup.Select(c => c.number)
                                          .OrderByDescending(n => n)
                                          .Take(5)
                                          .ToList();
            return new HandResult(HandRank.Flush, flushTop5);
        }

        // ストレート
        if (CheckStraight(numbers, out List<int> straightNumbers))
            return new HandResult(HandRank.Straight, straightNumbers);

        // スリーカード
        if (grouped[0].Count() == 3)
        {
            var kickers = grouped.Skip(1).Select(g => g.Key).Take(2).ToList();
            var result = new List<int> { grouped[0].Key };
            result.AddRange(kickers);
            return new HandResult(HandRank.ThreeOfAKind, result);
        }

        // ツーペア
        if (grouped[0].Count() == 2 && grouped.Count > 1 && grouped[1].Count() == 2)
        {
            var kickers = grouped.Skip(2).Select(g => g.Key).Take(1).ToList();
            var result = new List<int> { grouped[0].Key, grouped[1].Key };
            result.AddRange(kickers);
            return new HandResult(HandRank.TwoPair, result);
        }

        // ワンペア
        if (grouped[0].Count() == 2)
        {
            var kickers = grouped.Skip(1).Select(g => g.Key).Take(3).ToList();
            var result = new List<int> { grouped[0].Key };
            result.AddRange(kickers);
            return new HandResult(HandRank.OnePair, result);
        }

        // ハイカード
        return new HandResult(HandRank.HighCard, numbers.Distinct().OrderByDescending(n => n).Take(5).ToList());
    }

    // ストレート判定（A-2-3-4-5対応）
    private bool CheckStraight(List<int> numbers, out List<int> straightNumbers)
    {
        straightNumbers = new List<int>();
        var nums = numbers.Distinct().ToList();

        // Aを1として扱う（A-2-3-4-5）
        if (nums.Contains(14))
            nums.Add(1);

        nums = nums.Distinct().OrderByDescending(n => n).ToList();

        for (int i = 0; i <= nums.Count - 5; i++)
        {
            if (nums[i] - 1 == nums[i + 1] &&
                nums[i + 1] - 1 == nums[i + 2] &&
                nums[i + 2] - 1 == nums[i + 3] &&
                nums[i + 3] - 1 == nums[i + 4])
            {
                straightNumbers = nums.GetRange(i, 5);
                return true;
            }
        }

        return false;
    }

    // 組み合わせ生成（7枚から5枚選ぶ）
    private IEnumerable<List<Card>> GetCombinations(List<Card> list, int length)
    {
        if (length == 0) yield return new List<Card>();
        else
        {
            for (int i = 0; i <= list.Count - length; i++)
            {
                foreach (var tail in GetCombinations(list.Skip(i + 1).ToList(), length - 1))
                {
                    var comb = new List<Card> { list[i] };
                    comb.AddRange(tail);
                    yield return comb;
                }
            }
        }
    }

    // 役の強さを数値化（比較用）
    public static int GetHandStrength(HandRank rank)
    {
        return (int)rank;
    }

    // キッカーや数字による比較（同じ役だった場合）
    private int CompareRankNumbers(List<int> a, List<int> b)
    {
        for (int i = 0; i < Mathf.Min(a.Count, b.Count); i++)
        {
            if (a[i] > b[i]) return 1;
            if (a[i] < b[i]) return -1;
        }
        return 0;
    }
}
