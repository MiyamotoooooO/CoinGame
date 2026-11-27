using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// ショップの中身（並べるレコード・リロール・投入上限アップ）。画面は MonitorDisplay がモニターに描く。
    ///
    /// ・レコードは Resources/RecordData.csv の全レコードから、まだ持っていないものをランダムに6つ並べる
    /// ・買ったレコードは RecordInventory に入る（シーンに無ければここで作る）
    /// ・支払いはエリアのポイント（MedalPusherGame.TrySpendPoints）。値段は CSV の購入価格 × recordPriceScale
    /// ・エリアのポイント 100pt を、エリアを移っても残る特殊ポイントに交換できる
    /// </summary>
    public class RecordShop : MonoBehaviour
    {
        public const int OfferCount = 6;

        MedalPusherGame game;
        List<RecordData> allRecords = new List<RecordData>();

        /// <summary>並んでいるレコード（空の枠は null）</summary>
        public readonly RecordData[] Offers = new RecordData[OfferCount];
        /// <summary>その枠のレコードを買ったか</summary>
        public readonly bool[] Sold = new bool[OfferCount];

        /// <summary>所持しているレコード</summary>
        public RecordInventory Inventory { get; private set; }

        /// <summary>チュートリアル用：このキーのレコードを、まだ持っていなければ必ず並べる（空なら何もしない）</summary>
        [System.NonSerialized] public string pinnedRecordKey = "";
        /// <summary>チュートリアル用：このキーのレコードの値段を 0pt にする（空なら何もしない）</summary>
        [System.NonSerialized] public string freeRecordKey = "";

        /// <summary>今のリロールの値段</summary>
        public int RerollCost { get; private set; }
        /// <summary>投入上限アップを買った回数（このラン）</summary>
        public int LimitLevel { get; private set; }

        ShopSettings S => game.shop;
        public int LimitUpgradeCost => S.limitBasePoints + LimitLevel * S.limitStepPoints;
        /// <summary>レコードの値段（pt）</summary>
        public int RecordPrice(RecordData r)
        {
            if (r == null) return 0;
            if (!string.IsNullOrEmpty(freeRecordKey) && r.key == freeRecordKey) return 0;   // チュートリアルの 1 回目
            return r.buyPrice * Mathf.Max(1, S.recordPriceScale);
        }

        /// <summary>並んでいるレコードの中で、key のレコードの枠の番号。無ければ -1。</summary>
        public int IndexOfOffer(string key)
        {
            for (int i = 0; i < OfferCount; i++)
                if (Offers[i] != null && Offers[i].key == key) return i;
            return -1;
        }
        public int ExchangeCost => S.specialExchangeCost;
        public int ExchangeAmount => S.specialExchangeAmount;
        public int NextInsertLimit => Mathf.Min(game.InsertLimit + S.limitStep, S.limitMax);
        public bool CanUpgradeLimit => game.InsertLimit < S.limitMax;

        /// <summary>並び・売り切れ・値段が変わった時に呼ばれる</summary>
        public event System.Action Changed;

        public void Init(MedalPusherGame game)
        {
            this.game = game;
            allRecords = RecordDatabase.LoadFromResources();

            Inventory = FindFirstObjectByType<RecordInventory>();
            if (Inventory == null)
            {
                // シーンに無ければ作る。テスト用の「初期レコードを全部持つ」はオフにして、何も持たずに始める
                var go = new GameObject("RecordInventory");
                go.SetActive(false);
                Inventory = go.AddComponent<RecordInventory>();
                Inventory.ownAllStarterRecords = false;
                go.SetActive(true);
            }
        }

        /// <summary>ランの始め（リセット）。投入上限アップの回数を戻す。</summary>
        public void ResetRun()
        {
            LimitLevel = 0;
        }

        /// <summary>ショップを開いた時。リロールの値段を戻し、新しく並べる。</summary>
        public void OnShopOpened()
        {
            RerollCost = S.rerollBasePoints;
            Roll();
        }

        /// <summary>まだ持っていないレコードから、ランダムに並べ直す。</summary>
        void Roll()
        {
            var candidates = new List<RecordData>();
            RecordData pinned = null;
            foreach (var r in allRecords)
            {
                if (RecordData.IsNullOrEmpty(r) || Inventory.Has(r.key)) continue;
                if (!string.IsNullOrEmpty(pinnedRecordKey) && r.key == pinnedRecordKey) pinned = r;   // 必ず並べるレコードは別に取っておく
                else candidates.Add(r);
            }

            for (int i = 0; i < OfferCount; i++)
            {
                Sold[i] = false;
                if (i == 0 && pinned != null)
                {
                    Offers[i] = pinned;   // チュートリアルのレコードは、いちばん左に置く
                    continue;
                }
                if (candidates.Count == 0)
                {
                    Offers[i] = null;
                    continue;
                }
                int k = Random.Range(0, candidates.Count);
                Offers[i] = candidates[k];
                candidates.RemoveAt(k);
            }
            Changed?.Invoke();
        }

        /// <summary>リロール。ポイントを払って、並んでいるレコードを入れ替える。</summary>
        public bool Reroll(out string message)
        {
            if (!game.TrySpendPoints(RerollCost))
            {
                message = "ポイントが足りません";
                return false;
            }
            RerollCost += S.rerollStepPoints;
            Roll();
            message = "レコードを入れ替えました";
            return true;
        }

        /// <summary>index 番目のレコードを買う。</summary>
        public bool Buy(int index, out string message)
        {
            var r = index >= 0 && index < OfferCount ? Offers[index] : null;
            if (r == null || Sold[index])
            {
                message = "";
                return false;
            }
            if (Inventory.Has(r.key))
            {
                message = "もう持っています";
                return false;
            }
            if (!game.TrySpendPoints(RecordPrice(r)))
            {
                message = "ポイントが足りません";
                return false;
            }
            Inventory.Add(r);
            Sold[index] = true;
            Changed?.Invoke();
            message = $"「{r.name}」を手に入れました";
            return true;
        }

        /// <summary>投入上限アップを買う。</summary>
        public bool BuyLimitUpgrade(out string message)
        {
            if (!CanUpgradeLimit)
            {
                message = "投入上限はこれ以上増やせません";
                return false;
            }
            if (!game.TrySpendPoints(LimitUpgradeCost))
            {
                message = "ポイントが足りません";
                return false;
            }
            int before = game.InsertLimit;
            game.AddInsertLimit(NextInsertLimit - before);
            LimitLevel++;
            Changed?.Invoke();
            message = $"投入上限が {before} → {game.InsertLimit} になりました";
            return true;
        }

        /// <summary>エリアのポイントを特殊ポイントに交換する。</summary>
        public bool ExchangeSpecial(out string message)
        {
            if (!game.TrySpendPoints(ExchangeCost))
            {
                message = "ポイントが足りません";
                return false;
            }
            game.AddSpecialPoints(ExchangeAmount);
            Changed?.Invoke();
            message = $"特殊ポイントが {game.SpecialPoints} になりました";
            return true;
        }
    }
}
