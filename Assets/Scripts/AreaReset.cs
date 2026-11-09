using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 次のエリアに進んだ時に、持ち物をはじめの状態に戻す。
    /// 毎エリア、いろいろなレコードを試すきっかけにするため。
    ///
    /// 戻す物（Inspector でそれぞれオン・オフできる）
    ///   レコード　　　　：持っているレコードを、はじめに持っていた物（CSV の「初期」など）だけにする。流しているレコードも止める
    ///   所持メダル　　　：はじめの枚数（設定の startMedals）にする
    ///   所持ポイント　　：0（MedalPusherGame がもともと 0 にしている）
    ///   投入数の上限アップ：ショップで上げた分を元に戻し、上限アップの値段も最初に戻す
    ///   グルーヴゲージ　：0 にする
    ///   特殊ポイント　　：初めは残す（resetSpecialPoints をオンにすると 0 にする）
    ///
    /// ・クリアのあと（帯・ショップ）から次のエリアに進んだ時だけ動く。セーブの読み込みや R キーのリセットでは動かない
    /// ・MedalPusherGame は変えない。所持メダルと特殊ポイントは外から書きかえる口が無いので、リフレクションで書きかえる
    /// ・シーンに置かなくても、MedalPusherGame があれば再生時に自動で付く
    /// </summary>
    [DisallowMultipleComponent]
    public class AreaReset : MonoBehaviour
    {
        [Tooltip("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;

        [Header("次のエリアで、はじめの状態に戻す物")]
        public bool resetRecords = true;
        public bool resetMedals = true;
        public bool resetInsertLimit = true;
        public bool resetGauge = true;
        [Tooltip("特殊ポイントも 0 にする（初めはオフ：特殊ポイントはエリアをまたいで残す）")]
        public bool resetSpecialPoints = false;

        int lastArea;
        GameState prevState;

        /// <summary>シーンに無ければ、MedalPusherGame の GameObject に自動で付ける。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (FindFirstObjectByType<AreaReset>() != null) return;
            var g = FindFirstObjectByType<MedalPusherGame>();
            if (g != null) g.gameObject.AddComponent<AreaReset>();
        }

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (game == null)
            {
                enabled = false;
                return;
            }
            lastArea = game.AreaIndex;
            prevState = game.State;
            game.StateChanged += OnStateChanged;
        }

        void OnDestroy()
        {
            if (game != null) game.StateChanged -= OnStateChanged;
        }

        void OnStateChanged(GameState state)
        {
            // クリアのあと（帯・ショップ）から、次のエリアに進んだ時だけ
            bool advanced = game.AreaIndex > lastArea && (prevState == GameState.Cleared || prevState == GameState.Shop);
            lastArea = game.AreaIndex;
            prevState = state;
            if (advanced) ResetForNewArea();
        }

        /// <summary>持ち物を、はじめの状態に戻す。</summary>
        public void ResetForNewArea()
        {
            var effects = FindFirstObjectByType<RecordEffectSystem>();

            if (resetRecords)
            {
                if (effects != null) effects.Stop();   // 流しているレコードを止めて、プレーヤーからも外す
                ResetInventory();
            }
            if (resetMedals) SetPrivate("OwnedMedals", game.rules.startMedals);
            if (resetSpecialPoints) SetPrivate("SpecialPoints", 0);
            if (resetInsertLimit)
            {
                int bonus = game.InsertLimit - game.rules.insertLimit;
                if (bonus != 0) game.AddInsertLimit(-bonus);
                if (game.Shop != null) game.Shop.ResetRun();   // 上限アップの値段も最初に戻す
            }
            if (resetGauge && effects != null) effects.AddGauge(-effects.Gauge);

            Debug.Log($"AreaReset: エリア {game.AreaIndex + 1} に進んだので、持ち物をはじめの状態に戻しました。");
        }

        /// <summary>持っているレコードを、はじめに持っていた物だけにする。</summary>
        void ResetInventory()
        {
            RecordInventory inventory = game.Shop != null ? game.Shop.Inventory : null;
            if (inventory == null) inventory = FindFirstObjectByType<RecordInventory>();
            if (inventory == null) return;

            // はじめに持っていたレコード（RecordInventory の Awake と同じ決め方）
            //   ownAllStarterRecords がオンなら、CSV で「初期」のレコード全部と、startOwnedKeys の物
            var starterKeys = new HashSet<string>();
            if (inventory.ownAllStarterRecords)
                foreach (var r in RecordDatabase.LoadFromResources())
                    if (r != null && r.IsStarter && !string.IsNullOrWhiteSpace(r.key)) starterKeys.Add(r.key);
            if (inventory.startOwnedKeys != null)
                foreach (var key in inventory.startOwnedKeys)
                    if (!string.IsNullOrWhiteSpace(key)) starterKeys.Add(key.Trim());

            // はじめの物以外を手放す
            var remove = new List<string>();
            foreach (var r in inventory.Owned)
                if (r != null && !starterKeys.Contains(r.key)) remove.Add(r.key);
            foreach (var key in remove) inventory.Remove(key);

            // 手放していた「はじめの物」は戻す（売っていた時など）
            foreach (var key in starterKeys)
                if (!inventory.Has(key)) inventory.Add(key);

            // 装着の枠（使っている時だけ）から、持っていないレコードを外す
            var equip = FindFirstObjectByType<EquipManager>();
            if (equip != null)
                for (int i = 0; i < EquipManager.MaxSlots; i++)
                {
                    var r = equip.GetSlot(i);
                    if (r != null && !inventory.Has(r.key)) equip.Unequip(i);
                }
        }

        /// <summary>MedalPusherGame の、外から書きかえられない数（所持メダルなど）を書きかえる。</summary>
        void SetPrivate(string property, int value)
        {
            var p = typeof(MedalPusherGame).GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var setter = p != null ? p.GetSetMethod(true) : null;
            if (setter == null)
            {
                Debug.LogWarning($"AreaReset: MedalPusherGame の {property} を書きかえられませんでした。", this);
                return;
            }
            setter.Invoke(game, new object[] { value });
        }
    }
}
