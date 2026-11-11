using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N034 鏡「ミラー・ワールド」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：筐体の上に、左右どちらかに大きな鏡が出る（moveSeconds 秒ごとに反対へ動く）。分身したメダルは、鏡から光りながら降ってくる
    /// ・モニター：鏡の部屋。いまの鏡の側、分身までの残り（4枚に1枚 / 鏡の側は2枚に1枚）、分身した数
    /// ・ルール：
    ///   中央の穴に落ちたメダルの cloneEvery 枚に1枚が分身し、盤面の上からもう1枚降ってくる（CSV のとおり）
    ///   鏡のある側の半分に落ちたメダルは、mirrorEvery 枚に1枚が分身する（より分身しやすい）
    ///   分身は、鏡の側の盤面に降る
    /// ・鏡の側をねらって落とす遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class MirrorRecord : RecordScriptBase
    {
        public static MirrorRecord Instance { get; private set; }

        [Header("分身")]
        [Range(1, 20)] public int cloneEvery = 4;
        [Range(1, 20)] public int mirrorEvery = 2;
        [Range(2f, 60f)] public float moveSeconds = 8f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip cloneSound;
        public GameObject mirrorPrefab;

        protected override string DefaultKey => "Mirror";
        protected override string ScreenTitle => "MIRROR";
        protected override string ScreenSubtitle => "ミラー・ワールド";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<MirrorRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Glass = new Color32(0xc8, 0xe8, 0xff, 0xff);
        static readonly Color Frame = new Color32(0xd8, 0xb0, 0x5a, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        int side, countAll, countMirror, clones;
        float moveTimer, flash, mirrorX;

        Transform mirror;
        Material glassMaterial;
        ParticleSystem shine;

        RectTransform screenMirror;
        Text sideText, nextText, cloneText, statusText;
        AudioClip madeClone;

        float MirrorTargetX => side * HalfW * 0.5f;

        protected override void BuildBoard()
        {
            mirror = new GameObject("Mirror").transform;
            mirror.SetParent(Board, false);
            if (mirrorPrefab != null) Instantiate(mirrorPrefab, mirror).transform.localPosition = Vector3.zero;
            else
            {
                Cube("Frame", mirror, Vector3.zero, new Vector3(HalfW * 0.9f, D * 0.08f, D * 1.6f), Glow(Frame, 0.6f));
                glassMaterial = Glow(Glass, 1.5f);
                Cube("Glass", mirror, new Vector3(0f, -D * 0.05f, 0f), new Vector3(HalfW * 0.85f, D * 0.02f, D * 1.5f), glassMaterial);
            }
            shine = Particles("Shine", Board, Vector3.down, D * 1.5f, D * 0.12f, 0.8f, 0.3f, 30f);
        }

        protected override void OnBegin()
        {
            side = Random.value < 0.5f ? -1 : 1;
            mirrorX = MirrorTargetX;
            moveTimer = moveSeconds;
            countAll = countMirror = clones = 0;
        }

        protected override void Tick(float dt, bool running)
        {
            flash = Mathf.MoveTowards(flash, 0f, dt * 2f);
            mirrorX = Mathf.MoveTowards(mirrorX, MirrorTargetX, dt * HalfW);
            if (!running) return;
            moveTimer -= dt;
            if (moveTimer <= 0f)
            {
                moveTimer = moveSeconds;
                side = -side;
            }
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center) return;
            bool mirrorSide = (medal.transform.position.x < 0f ? -1 : 1) == side;
            bool clone;
            if (mirrorSide)
            {
                countMirror++;
                clone = countMirror % Mathf.Max(1, mirrorEvery) == 0;
            }
            else
            {
                countAll++;
                clone = countAll % Mathf.Max(1, cloneEvery) == 0;
            }
            if (!clone) return;
            clones++;
            flash = 1f;
            var list = RainMedals(1, side);
            if (list.Count > 0) Emit(shine, list[0].transform.position, Glass, 20);
            Play(cloneSound, ref madeClone, () => RecordSynth.Sweep("MirrorClone", 1200f, 2400f, 0.35f, 0.25f, RecordSynth.Wave.Triangle));
            Pop("分身！", Glass, new Vector2(BodyW * (side < 0 ? 0.3f : 0.7f), 200f));
        }

        protected override bool KeepBoardVisible() => shine != null && shine.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            mirror.position = new Vector3(mirrorX, Layout.wallHeight * 1.05f, Mathf.Lerp(FrontZ, PusherFrontZ, 0.5f));
            mirror.localScale = new Vector3(Mathf.Max(0.01f, BoardShown), 1f, 1f);
            if (glassMaterial != null) SetGlow(glassMaterial, Glass, 1f + 3f * flash);
        }

        protected override void BuildScreen()
        {
            float W = BodyW;
            var room = Panel(Screen, "Room", W * 0.5f, 200f, W - 48f, 360f, new Color32(0x14, 0x18, 0x24, 0xff));
            float rw = W - 48f;
            Img(room, "Floor", rw * 0.5f, 330f, rw, 60f, new Color32(0x22, 0x26, 0x34, 0xff));
            var m = Grp(room, "Mirror", rw * 0.5f, 170f, rw * 0.4f, 260f);
            Img(m, "Frame", rw * 0.2f, 130f, rw * 0.4f, 260f, Frame, MonitorDisplay.ScreenShape.Pill);
            Img(m, "Glass", rw * 0.2f, 130f, rw * 0.4f - 20f, 240f, Glass, MonitorDisplay.ScreenShape.Pill);
            Img(m, "Glint", rw * 0.12f, 80f, 20f, 120f, new Color(1f, 1f, 1f, 0.6f), MonitorDisplay.ScreenShape.Pill);
            screenMirror = m;
            sideText = Lbl(room, "Side", rw * 0.5f, 30f, rw, 40f, "", 24, Ink);
            nextText = Lbl(Screen, "Next", 24f, 440f, W * 0.6f, 40f, "", 22, Ink, TextAnchor.MiddleLeft);
            cloneText = Lbl(Screen, "Clones", W - 24f, 440f, 400f, 40f, "", 24, Glass, TextAnchor.MiddleRight);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            float rw = BodyW - 48f;
            Place(screenMirror, rw * 0.5f + (mirrorX / Mathf.Max(0.01f, HalfW)) * rw * 0.5f, 170f);
            Scale(screenMirror, 1f + 0.05f * flash);
            sideText.text = $"鏡は{(side < 0 ? "左" : "右")}  （動くまで {Mathf.CeilToInt(moveTimer)}秒）";
            int a = cloneEvery - countAll % Mathf.Max(1, cloneEvery), b = mirrorEvery - countMirror % Mathf.Max(1, mirrorEvery);
            nextText.text = $"分身まで：鏡の側 あと{b}枚 ／ 反対 あと{a}枚";
            cloneText.text = $"分身 {clones}枚";
            statusText.text = $"中央の穴の、鏡のある側の半分に落とすと {mirrorEvery}枚に1枚が分身（反対側は {cloneEvery}枚に1枚）";
        }
    }
}
