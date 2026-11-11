using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N037 力士「どすこい張り手」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：プッシャーの前面に力士の大きな手形が付く。押すたびに土煙。張り手の時は手形が赤く光って、前のメダルを強く押し出す
    /// ・モニター：土俵。力士が四股を踏み、張り手までの回数（●●●○）。張り手で「どすこい！」
    /// ・ルール：
    ///   発動中は、プッシャーが前に出る距離が ×strokeScale（CSV のとおり）
    ///   プッシャーが slapEvery 回押すごとに「張り手」：プッシャーの前のメダルを、手前へ強く押し出す（押す力も強く）
    ///   張り手のあと slapWindow 秒の間に中央の穴に落ちると「押し出し」：1枚 +pushOutBonus pt
    /// ・張り手の直前に、プッシャーの前へメダルを集めておく遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// プッシャーの距離は、効果が終わると元に戻る。
    /// </summary>
    [DisallowMultipleComponent]
    public class SumoRecord : RecordScriptBase
    {
        public static SumoRecord Instance { get; private set; }

        [Header("プッシャー")]
        [Range(1f, 3f)] public float strokeScale = 1.5f;

        [Header("張り手")]
        [Range(1, 20)] public int slapEvery = 4;
        [Range(0f, 20f)] public float slapStrength = 4f;
        [Tooltip("押し出す範囲（プッシャーの前、メダルの直径に対する倍率）")]
        [Range(0.3f, 4f)] public float slapReach = 1.4f;
        [Range(0f, 10f)] public float slapWindow = 2.5f;
        [Range(0, 100)] public int pushOutBonus = 10;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip stompSound;
        public AudioClip slapSound;

        protected override string DefaultKey => "Sumo";
        protected override string ScreenTitle => "SUMO";
        protected override string ScreenSubtitle => "どすこい張り手";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<SumoRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color Hand = new Color32(0xc8, 0x3a, 0x2a, 0xff);
        static readonly Color Clay = new Color32(0xc8, 0xa0, 0x6a, 0xff);
        static readonly Color Gold = new Color32(0xff, 0xd2, 0x3f, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        int pushes, slaps, pushedOut;
        float lastPhase, slapTimer, slapFlash, stomp;
        readonly List<Medal> scratch = new List<Medal>();

        Transform handprint;
        Material handMaterial;
        ParticleSystem dust;

        RectTransform wrestler;
        readonly List<Image> pips = new List<Image>();
        Text slapText, outText, statusText;
        AudioClip madeStomp, madeSlap;

        float PusherFaceZ
        {
            get
            {
                if (game.Pusher == null) return PusherFrontZ;
                float t = (1f - Mathf.Cos(game.Pusher.Phase01 * Mathf.PI * 2f)) * 0.5f;
                return Layout.pusherRetractZ - game.Pusher.stroke * t;
            }
        }

        protected override void BuildBoard()
        {
            handprint = new GameObject("Handprint").transform;
            handprint.SetParent(Board, false);
            handMaterial = Glow(Hand, 0.5f);
            Cube("Palm", handprint, Vector3.zero, new Vector3(D * 0.7f, D * 0.6f, D * 0.02f), handMaterial);
            for (int i = 0; i < 4; i++)
                Cube($"Finger{i}", handprint, new Vector3(-D * 0.24f + i * D * 0.16f, D * 0.45f, 0f), new Vector3(D * 0.1f, D * 0.32f, D * 0.02f), handMaterial);
            Cube("Thumb", handprint, new Vector3(D * 0.42f, D * 0.05f, 0f), new Vector3(D * 0.12f, D * 0.26f, D * 0.02f), handMaterial).transform.localRotation = Quaternion.Euler(0f, 0f, -40f);
            dust = Particles("Dust", Board, Vector3.up, D * 1.5f, D * 0.3f, 1f, 0.05f, 70f);
        }

        protected override void OnBegin()
        {
            pushes = 0;
            slaps = 0;
            pushedOut = 0;
            slapTimer = 0f;
            lastPhase = game.Pusher != null ? game.Pusher.Phase01 : 0f;
        }

        protected override void Tick(float dt, bool running)
        {
            PusherStrokeScale = strokeScale;
            slapFlash = Mathf.MoveTowards(slapFlash, 0f, dt * 2f);
            stomp = Mathf.MoveTowards(stomp, 0f, dt * 3f);
            if (slapTimer > 0f) slapTimer -= dt;
            if (game.Pusher == null) return;
            float ph = game.Pusher.Phase01;
            bool pushed = lastPhase < 0.5f && ph >= 0.5f;
            lastPhase = ph;
            if (!pushed || !running) return;

            pushes++;
            stomp = 1f;
            Vector3 face = new Vector3(0f, D * 0.1f, PusherFaceZ - D * 0.2f);
            Emit(dust, face + new Vector3(Random.Range(-HalfW * 0.6f, HalfW * 0.6f), 0f, 0f), Clay, 8);
            Play(stompSound, ref madeStomp, () => RecordSynth.Thud("SumoStomp", 55f, 0.4f, 0.7f), 0.5f);
            if (pushes < slapEvery) return;
            pushes = 0;
            Slap();
        }

        void Slap()
        {
            slaps++;
            slapFlash = 1f;
            slapTimer = slapWindow;
            float faceZ = PusherFaceZ;
            foreach (var m in BoardMedals)
            {
                if (m == null || m.Consumed) continue;
                float z = m.transform.position.z;
                if (z > faceZ + D * 0.2f || z < faceZ - D * slapReach || m.transform.position.y > Layout.pusherHeight) continue;
                scratch.Add(m);
            }
            foreach (var m in scratch) Push(m, new Vector3(Random.Range(-0.15f, 0.15f), 0.25f, -1f) * slapStrength);
            scratch.Clear();
            Emit(dust, new Vector3(0f, D * 0.2f, faceZ - D * 0.3f), Clay, 50);
            Play(slapSound, ref madeSlap, () => RecordSynth.Noise("SumoSlap", 0.35f, 0.7f, 0.002f, 0.1f, 1.2f, 27));
            Shake(0.04f, 0.3f);
            PopCenter("どすこい！", Hand);
            Speaker(0.9f, 8, Hand);
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center || slapTimer <= 0f) return;
            pushedOut++;
            AddPoints(pushOutBonus);
            Pop($"押し出し +{pushOutBonus}", Gold, new Vector2(BodyW * Random.Range(0.3f, 0.7f), 420f));
        }

        protected override bool KeepBoardVisible() => dust != null && dust.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            handprint.gameObject.SetActive(Active);
            if (!Active) return;
            handprint.position = new Vector3(0f, Layout.pusherHeight * 0.55f, PusherFaceZ - D * 0.02f);
            float s = 1f + 0.4f * slapFlash;
            handprint.localScale = new Vector3(s, s, 1f);
            SetGlow(handMaterial, Hand, 0.4f + 4f * slapFlash);
        }

        protected override void BuildScreen()
        {
            pips.Clear();
            float W = BodyW;
            var dohyo = Panel(Screen, "Dohyo", W * 0.5f, 200f, W - 48f, 360f, new Color32(0x22, 0x18, 0x10, 0xff));
            float dw = W - 48f;
            Img(dohyo, "Roof", dw * 0.5f, 30f, dw * 0.7f, 40f, new Color32(0x6a, 0x2a, 0x2a, 0xff));
            Img(dohyo, "Ring", dw * 0.5f, 300f, dw * 0.8f, 90f, Clay, MonitorDisplay.ScreenShape.Pill);
            Img(dohyo, "Rope", dw * 0.5f, 300f, dw * 0.7f, 70f, new Color32(0xe8, 0xd8, 0xa0, 0xff), MonitorDisplay.ScreenShape.Ring);
            var w = Grp(dohyo, "Wrestler", dw * 0.35f, 220f, 180f, 200f);
            Img(w, "Body", 90f, 120f, 170f, 150f, new Color32(0xff, 0xd8, 0xb8, 0xff), MonitorDisplay.ScreenShape.Circle);
            Img(w, "Mawashi", 90f, 160f, 150f, 36f, new Color32(0x2a, 0x2a, 0x6a, 0xff), MonitorDisplay.ScreenShape.Pill);
            Img(w, "Head", 90f, 30f, 70f, 64f, new Color32(0xff, 0xd8, 0xb8, 0xff), MonitorDisplay.ScreenShape.Circle);
            Img(w, "Topknot", 90f, 0f, 30f, 20f, new Color32(0x20, 0x18, 0x14, 0xff), MonitorDisplay.ScreenShape.Pill);
            Img(w, "Hand", 175f, 90f, 50f, 60f, new Color32(0xff, 0xd8, 0xb8, 0xff), MonitorDisplay.ScreenShape.Circle);
            wrestler = w;
            slapText = Lbl(dohyo, "Slap", dw * 0.75f, 120f, dw * 0.4f, 100f, "", 56, Hand);
            for (int i = 0; i < slapEvery; i++)
                pips.Add(Img(dohyo, $"Pip{i}", dw * 0.75f + (i - (slapEvery - 1) * 0.5f) * 40f, 200f, 28f, 28f, new Color(1f, 1f, 1f, 0.15f), MonitorDisplay.ScreenShape.Circle));
            Lbl(dohyo, "PipLabel", dw * 0.75f, 232f, 300f, 26f, "張り手まで", 16, Mute);
            outText = Lbl(Screen, "Out", 24f, 440f, 600f, 40f, "", 24, Gold, TextAnchor.MiddleLeft);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            float dw = BodyW - 48f;
            Place(wrestler, dw * 0.35f + slapFlash * 50f, 220f + stomp * 10f);
            wrestler.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 3f) * 3f);
            slapText.text = slapFlash > 0f ? "どすこい！" : "";
            Scale(slapText.transform, 1f + 0.3f * slapFlash);
            for (int i = 0; i < pips.Count; i++) pips[i].color = i < pushes ? Hand : new Color(1f, 1f, 1f, 0.15f);
            outText.text = $"張り手 {slaps}回 ／ 押し出し {pushedOut}枚";
            statusText.text = slapTimer > 0f
                ? $"<color=#FFD23F>押し出し！</color>  いま中央の穴に落ちると +{pushOutBonus}pt"
                : $"プッシャーが {slapEvery}回押すと張り手。プッシャーの前のメダルを強く押し出す";
        }
    }
}
