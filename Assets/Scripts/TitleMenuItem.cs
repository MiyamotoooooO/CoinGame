using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace MedalPusher
{
    /// <summary>
    /// タイトル画面のメニュー項目1つ分（「初めから」など）。
    /// 見た目の動きは TitleMenu がまとめて動かす。ここはマウスの反応と、部品の置き場所だけ。
    /// </summary>
    public class TitleMenuItem : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        [Tooltip("押した時に何をするか")]
        public TitleMenu.MenuAction action;

        [Header("■ 部品（組み立てメニューが入れるので、触らなくてよい）")]
        [Tooltip("左右に動く中身の親")]
        public RectTransform content;
        [Tooltip("えらんでいる時に出る、ピンク→オレンジの帯")]
        public Image highlight;
        [Tooltip("「01」などの番号")]
        public TextMeshProUGUI indexText;
        [Tooltip("「初めから」などの文字")]
        public TextMeshProUGUI labelText;
        [Tooltip("「NEW GAME」などの小さな英語")]
        public TextMeshProUGUI subText;
        [Tooltip("えらんでいる時に回る、小さなレコード")]
        public Image miniRecord;
        [Tooltip("項目全体の濃さ（出てくる時の動きに使う）")]
        public CanvasGroup group;

        // TitleMenu が入れる値
        [NonSerialized] public TitleMenu owner;
        [NonSerialized] public int index;
        [NonSerialized] public bool usable = true;      // 押せるかどうか（セーブが無い時の「続きから」は false）
        [NonSerialized] public float selectAmount;      // えらばれ具合 0〜1（なめらかに変わる）

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (owner != null) owner.Select(index);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (owner == null || !usable) return;
            owner.Select(index);
            owner.Confirm();
        }
    }
}
