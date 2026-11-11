using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// N020 ヒーロー「ネオン・レンジャー」。効果の発動中（曲1曲分）だけ動く。
    ///
    /// ・盤面：中央の穴の左右に、敵の弱点の側を示す光の矢印が出る（弱点の側が赤く光る）
    /// ・モニター：バトル画面。左にヒーロー、右に MUTE CORP. の敵（HPバー）。攻撃するとダメージの数字が出て、倒すと爆発
    /// ・ルール：
    ///   中央の穴にメダルが落ちるたびに、ヒーローが攻撃（damage）。敵の弱点の側（左・右の半分）に落ちると CRITICAL：ダメージ ×criticalMultiplier
    ///   弱点の側は weakSwitchSeconds 秒ごとに入れかわる
    ///   敵のHPを0にすると +defeatBonus pt、ゲージ +defeatGauge。次の敵が出る（HP は前の ×hpGrowth）
    ///   敵は attackSeconds 秒ごとに攻撃してくる：ゲージ -enemyAttackGauge
    /// ・弱点の側をねらって、早く倒す遊び
    ///
    /// 見た目と音はコードで作る仮のもの。「差し替え用」に入れると、そちらを使う。
    /// シーンに置かなくても、RecordEffectSystem があれば再生時に自動で付く。CSV の「効果の型」は Scripted にする。
    /// </summary>
    [DisallowMultipleComponent]
    public class HeroRecord : RecordScriptBase
    {
        public static HeroRecord Instance { get; private set; }

        [Header("攻撃")]
        [Range(1, 100)] public int damage = 10;
        [Range(1f, 5f)] public float criticalMultiplier = 3f;
        [Range(1f, 30f)] public float weakSwitchSeconds = 6f;

        [Header("敵")]
        [Range(10, 1000)] public int firstEnemyHp = 100;
        [Range(1f, 3f)] public float hpGrowth = 1.3f;
        [Range(0, 1000)] public int defeatBonus = 100;
        [Range(0f, 100f)] public float defeatGauge = 20f;
        [Range(2f, 60f)] public float attackSeconds = 10f;
        [Range(0f, 50f)] public float enemyAttackGauge = 5f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.8f;

        [Header("差し替え用（空なら仮の見た目・音）")]
        public AudioClip hitSound;
        public AudioClip criticalSound;
        public AudioClip defeatSound;
        public Sprite heroSprite;
        public Sprite enemySprite;

        protected override string DefaultKey => "Hero";
        protected override string ScreenTitle => "HERO";
        protected override string ScreenSubtitle => "ネオン・レンジャー";
        protected override float RecordSoundVolume => volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach() => Attach<HeroRecord>();
        protected override void OnAwake() => Instance = this;
        protected override void OnDestroyed() { if (Instance == this) Instance = null; }

        static readonly Color HeroColor = new Color32(0x3f, 0xc8, 0xff, 0xff);
        static readonly Color EnemyColor = new Color32(0x8a, 0x3c, 0xd8, 0xff);
        static readonly Color Weak = new Color32(0xff, 0x45, 0x45, 0xff);
        static readonly Color Gold = new Color32(0xff, 0xd2, 0x3f, 0xff);
        static readonly Color Ink = new Color32(0xf4, 0xf4, 0xf6, 0xff);
        static readonly Color Mute = new Color32(0x9c, 0x9e, 0xae, 0xff);

        static readonly string[] EnemyNames = { "ミュート兵", "ノイズキャンセラー", "サイレンサー", "MUTE ロボ", "ボリューム・ゼロ" };

        int weakSide, enemyIndex, hp, maxHp, defeated;
        float weakTimer, attackTimer, heroPunch, enemyHit, boom;

        readonly GameObject[] arrows = new GameObject[2];
        readonly Material[] arrowMaterials = new Material[2];
        ParticleSystem sparks;

        RectTransform hero, enemy, explosion;
        Image hpFill, enemyBody;
        Text enemyName, hpText, defeatedText, statusText, weakText;
        AudioClip madeHit, madeCrit, madeDefeat;

        protected override void BuildBoard()
        {
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                arrowMaterials[i] = Glow(Weak, 0f);
                arrows[i] = Cube(side < 0 ? "WeakLeft" : "WeakRight", Board, new Vector3(side * HoleHalf * 0.5f, D * 0.02f, FrontZ + D * 0.35f), new Vector3(HoleHalf * 0.8f, D * 0.02f, D * 0.12f), arrowMaterials[i]);
            }
            sparks = Particles("Sparks", Board, Vector3.up, D * 3f, D * 0.08f, 0.6f, 0.8f, 50f);
        }

        protected override void OnBegin()
        {
            enemyIndex = 0;
            defeated = 0;
            weakSide = Random.value < 0.5f ? -1 : 1;
            weakTimer = weakSwitchSeconds;
            attackTimer = attackSeconds;
            SpawnEnemy();
        }

        void SpawnEnemy()
        {
            maxHp = Mathf.RoundToInt(firstEnemyHp * Mathf.Pow(hpGrowth, enemyIndex));
            hp = maxHp;
            attackTimer = attackSeconds;
        }

        protected override void Tick(float dt, bool running)
        {
            heroPunch = Mathf.MoveTowards(heroPunch, 0f, dt * 4f);
            enemyHit = Mathf.MoveTowards(enemyHit, 0f, dt * 4f);
            boom = Mathf.MoveTowards(boom, 0f, dt * 1.5f);
            if (!running) return;
            weakTimer -= dt;
            if (weakTimer <= 0f)
            {
                weakTimer = weakSwitchSeconds;
                weakSide = -weakSide;
            }
            attackTimer -= dt;
            if (attackTimer <= 0f)
            {
                attackTimer = attackSeconds;
                AddGauge(-enemyAttackGauge);
                Pop($"敵の攻撃！ ゲージ -{enemyAttackGauge:0}", EnemyColor, new Vector2(BodyW * 0.3f, 200f));
                Shake(0.02f, 0.25f);
            }
        }

        protected override void OnDrop(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center || hp <= 0) return;
            int side = medal.transform.position.x < 0f ? -1 : 1;
            bool crit = side == weakSide;
            int dmg = Mathf.RoundToInt(damage * (crit ? criticalMultiplier : 1f));
            hp = Mathf.Max(0, hp - dmg);
            heroPunch = 1f;
            enemyHit = 1f;
            Emit(sparks, new Vector3(side * HoleHalf * 0.5f, D * 0.2f, FrontZ), crit ? Weak : HeroColor, crit ? 24 : 8);
            if (crit) Play(criticalSound, ref madeCrit, () => RecordSynth.Noise("HeroCrit", 0.35f, 0.5f, 0.002f, 0.12f, 1f, 13), 0.9f);
            else Play(hitSound, ref madeHit, () => RecordSynth.Noise("HeroHit", 0.2f, 0.3f, 0.002f, 0.06f, 0.8f, 12), 0.7f);
            Pop(crit ? $"CRITICAL {dmg}" : $"{dmg}", crit ? Weak : Ink, new Vector2(BodyW * 0.72f + Random.Range(-60f, 60f), 140f));
            if (hp > 0) return;

            defeated++;
            enemyIndex++;
            boom = 1f;
            AddPoints(defeatBonus);
            AddGauge(defeatGauge);
            Play(defeatSound, ref madeDefeat, () => RecordSynth.Thud("HeroDefeat", 50f, 1f, 1f));
            PopCenter($"撃破！ +{defeatBonus}pt", Gold);
            Shake(0.05f, 0.5f);
            Speaker(1f, 16, Gold);
            SpawnEnemy();
        }

        protected override bool KeepBoardVisible() => sparks != null && sparks.particleCount > 0;

        protected override void UpdateBoard(float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                int side = i == 0 ? -1 : 1;
                bool weak = Active && side == weakSide;
                arrows[i].SetActive(BoardShown > 0.01f);
                SetGlow(arrowMaterials[i], weak ? Weak : HeroColor, weak ? 2f + Mathf.Sin(Time.time * 10f) : 0.2f);
            }
        }

        protected override void BuildScreen()
        {
            float W = BodyW;
            var field = Panel(Screen, "Field", W * 0.5f, 200f, W - 48f, 360f, new Color32(0x14, 0x10, 0x24, 0xff));
            float fw = W - 48f;
            Img(field, "Ground", fw * 0.5f, 330f, fw, 60f, new Color32(0x22, 0x1c, 0x34, 0xff));
            // ヒーロー
            hero = Grp(field, "Hero", fw * 0.25f, 220f, 160f, 220f);
            if (heroSprite != null) { var s = Img(hero, "Sprite", 80f, 110f, 160f, 220f, Color.white); s.sprite = heroSprite; s.preserveAspect = true; }
            else
            {
                Img(hero, "Body", 80f, 140f, 80f, 120f, HeroColor, MonitorDisplay.ScreenShape.Pill);
                Img(hero, "Head", 80f, 50f, 70f, 70f, HeroColor, MonitorDisplay.ScreenShape.Circle);
                Img(hero, "Visor", 86f, 50f, 50f, 18f, new Color32(0x10, 0x10, 0x20, 0xff), MonitorDisplay.ScreenShape.Pill);
                Img(hero, "Scarf", 50f, 95f, 60f, 16f, Weak, MonitorDisplay.ScreenShape.Pill);
                Img(hero, "Fist", 130f, 130f, 30f, 30f, Ink, MonitorDisplay.ScreenShape.Circle);
            }
            // 敵
            enemy = Grp(field, "Enemy", fw * 0.72f, 210f, 200f, 220f);
            if (enemySprite != null) { var s = Img(enemy, "Sprite", 100f, 110f, 200f, 220f, Color.white); s.sprite = enemySprite; s.preserveAspect = true; enemyBody = s; }
            else
            {
                enemyBody = Img(enemy, "Body", 100f, 130f, 150f, 150f, EnemyColor);
                Img(enemy, "Screen", 100f, 110f, 110f, 70f, new Color32(0x10, 0x10, 0x20, 0xff));
                Lbl(enemy, "Mute", 100f, 110f, 110f, 70f, "MUTE", 22, Weak, TextAnchor.MiddleCenter, true);
                Img(enemy, "Antenna", 100f, 40f, 6f, 40f, EnemyColor);
            }
            explosion = Grp(field, "Boom", fw * 0.72f, 200f, 300f, 300f);
            for (int i = 0; i < 3; i++) Img(explosion, $"B{i}", 150f, 150f, 300f - i * 80f, 300f - i * 80f, i == 0 ? Weak : i == 1 ? Gold : Ink, MonitorDisplay.ScreenShape.Circle);
            enemyName = Lbl(field, "EnemyName", fw * 0.72f, 34f, 360f, 34f, "", 22, Ink);
            Img(field, "HpBg", fw * 0.72f, 66f, 300f, 16f, new Color(1f, 1f, 1f, 0.15f));
            hpFill = Img(field, "Hp", fw * 0.72f, 66f, 300f, 16f, Weak);
            hpFill.type = Image.Type.Filled;
            hpFill.fillMethod = Image.FillMethod.Horizontal;
            hpText = Lbl(field, "HpText", fw * 0.72f + 160f, 66f, 140f, 24f, "", 16, Ink, TextAnchor.MiddleLeft, true);
            weakText = Lbl(Screen, "Weak", 24f, 440f, W - 48f, 40f, "", 26, Weak, TextAnchor.MiddleLeft);
            defeatedText = Lbl(Screen, "Defeated", W - 24f, 440f, 300f, 40f, "", 24, Gold, TextAnchor.MiddleRight);
            statusText = Lbl(Screen, "Status", 24f, 560f, W - 48f, 36f, "", 20, Ink, TextAnchor.MiddleLeft);
        }

        protected override void UpdateScreen(float dt)
        {
            float fw = BodyW - 48f;
            Place(hero, fw * 0.25f + heroPunch * 60f, 220f - Mathf.Abs(Mathf.Sin(Time.time * 4f)) * 6f);
            Place(enemy, fw * 0.72f + enemyHit * 20f * Mathf.Sin(Time.time * 60f), 210f);
            if (enemyBody != null) enemyBody.color = Color.Lerp(enemySprite != null ? Color.white : EnemyColor, Color.white, enemyHit * 0.7f);
            explosion.gameObject.SetActive(boom > 0f);
            if (boom > 0f) Scale(explosion, 0.3f + (1f - boom) * 1.2f);
            enemyName.text = $"{EnemyNames[enemyIndex % EnemyNames.Length]}  Lv.{enemyIndex + 1}";
            hpFill.fillAmount = maxHp > 0 ? hp / (float)maxHp : 0f;
            hpText.text = $"{hp}/{maxHp}";
            weakText.text = $"弱点：{(weakSide < 0 ? "左" : "右")}の半分（CRITICAL ×{criticalMultiplier:0.#}）  入れかわりまで {Mathf.CeilToInt(weakTimer)}秒";
            defeatedText.text = $"撃破 {defeated}";
            statusText.text = $"中央の穴に落ちると攻撃！ 倒すと +{defeatBonus}pt・ゲージ +{defeatGauge:0}。敵の攻撃まで {Mathf.CeilToInt(attackTimer)}秒";
        }
    }
}
