using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    // =====================================================================
    // オープニングムービーで使う「データの入れ物」をまとめたファイル。
    // 動きの処理は OpeningMoviePlayer.cs、
    // シーンへの組み立ては Editor/OpeningMovieBuilder.cs にある。
    // =====================================================================

    // ---------- opening_cuts.json をそのまま読み込むための入れ物 ----------

    [Serializable]
    public class OpeningJson
    {
        public int width;
        public int height;
        public float totalSeconds;
        public OpeningJsonCut[] cuts;
    }

    [Serializable]
    public class OpeningJsonCut
    {
        public int cut;
        public float start;
        public float end;
        public string description;
        public OpeningJsonLayer[] layers;
        public OpeningJsonCaption[] captions;
        public string[] sounds;
    }

    [Serializable]
    public class OpeningJsonLayer
    {
        public int order;
        public string name;
        public string file;
        public bool gray;
    }

    [Serializable]
    public class OpeningJsonCaption
    {
        public string speaker;
        public string text;
    }

    // ---------- シーンに並べた物を覚えておく入れ物 ----------

    /// <summary>
    /// 1枚のレイヤー（画像）の情報。
    /// 画像は 1920×1080 で、絵が描いてある部分の「中心」と「大きさ」を
    /// 組み立てる時に測って覚えておく。回す時や跳ねさせる時の中心に使う。
    /// </summary>
    [Serializable]
    public class OpeningLayerView
    {
        [Tooltip("レイヤーの名前（opening_cuts.json の name）")]
        public string name;
        public RectTransform rect;
        public Image image;
        [Tooltip("止まっている時の位置")]
        public Vector2 basePos;
        [Tooltip("止まっている時、この画像の真ん中が画面のどこにあるか（画面の真ん中が 0,0）")]
        public Vector2 restCenter;
        [Tooltip("絵が描いてある部分の中心（画面の真ん中が 0,0）")]
        public Vector2 boundsCenter;
        [Tooltip("絵が描いてある部分の大きさ（ピクセル）")]
        public Vector2 boundsSize;
        [Tooltip("灰色版のレイヤーかどうか")]
        public bool gray;
    }

    [Serializable]
    public class OpeningCaptionLine
    {
        [Tooltip("話している人の名前")]
        public string speaker;
        [TextArea(1, 3), Tooltip("せりふ")]
        public string text;
    }

    /// <summary>1カット分の情報</summary>
    [Serializable]
    public class OpeningCutView
    {
        [Tooltip("カットの番号（1〜10）")]
        public int number;
        [Tooltip("ムービー全体の中で、このカットが始まる秒数")]
        public float start;
        [Tooltip("ムービー全体の中で、このカットが終わる秒数")]
        public float end;
        [TextArea(1, 3), Tooltip("絵コンテの説明（メモ）")]
        public string description;
        [Tooltip("このカットの親オブジェクト")]
        public RectTransform root;
        [Tooltip("カットが始まってから何秒後に、せりふを出し始めるか")]
        public float captionStart = 1f;
        [Tooltip("このカットのせりふ")]
        public List<OpeningCaptionLine> captions = new List<OpeningCaptionLine>();
        [Tooltip("このカットのレイヤー（奥から順）")]
        public List<OpeningLayerView> layers = new List<OpeningLayerView>();

        [NonSerialized] private Dictionary<string, OpeningLayerView> lookup;

        /// <summary>このカットの長さ（秒）</summary>
        public float Duration => Mathf.Max(0.01f, end - start);

        /// <summary>名前からレイヤーを探す。無ければ null</summary>
        public OpeningLayerView Find(string layerName)
        {
            if (lookup == null) BuildLookup();
            lookup.TryGetValue(layerName, out OpeningLayerView found);
            return found;
        }

        public void BuildLookup()
        {
            lookup = new Dictionary<string, OpeningLayerView>();
            foreach (OpeningLayerView layer in layers)
            {
                if (layer == null || string.IsNullOrEmpty(layer.name)) continue;
                lookup[layer.name] = layer;
            }
        }
    }

    /// <summary>決まった秒数に鳴らす効果音</summary>
    [Serializable]
    public class OpeningSoundCue
    {
        [Tooltip("何の音かのメモ")]
        public string memo;
        [Tooltip("ムービーの始まりから何秒後に鳴らすか")]
        public float time;
        [Tooltip("鳴らす音。空なら鳴らない")]
        public AudioClip clip;
        [Range(0f, 1f), Tooltip("音量")]
        public float volume = 1f;
    }
}
