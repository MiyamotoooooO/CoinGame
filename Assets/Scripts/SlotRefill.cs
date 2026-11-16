using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// メダルの補充スロット。筐体の右上（モニターの右、パイプの手前）に置いた小さなスロットマシン（3D）。
    /// 見た目：赤い本体と青い横の板、上はアーチ型の窓（お札・白い玉・黄色いランプ）、星 3 つ、回るドラムのリール 3 本、
    ///         前の台にボタン 3 つとメダルの口、右にレバー。すべてコードで作る
    ///
    /// ・リールは 3 本ともいつも回っている。メダルが中央の穴に 1 枚入るたびに、左から 1 本ずつ止まる
    ///   （止まったリールの上の星と、前のボタンが光る）
    ///   入ったメダルは最大 4 枚までためておける（アーチの中の黄色いランプで見える）
    /// ・左とまん中がちがっても、右まで 1 枚ずつ止める。3 本止まったところで判定する
    ///   左とまん中が同じならリーチ（右のリールが速く回り、次の 1 枚でゆっくり止まる）
    ///   3 つそろえばあたり。そろわなければ「そろわず」。どちらも少し待って、3 本とも回り直す（メダルはいらない）
    /// ・3 つのリールがそろった出目で、盤面にメダルが出る（所持メダルは減らない。落とせばいつもどおりポイントとメダル）
    ///     777　　：盤面の下からメダルのタワーがせり上がり、崩れて広がる（30 枚）
    ///     チェリー：上からチェリーの爆弾が 2 つ落ちてきて、盤面で破裂してメダルが飛び散る（12 枚 × 2）
    ///     ベル　　：盤面の上に大きなベルが出て鳴り、メダルのシャワーが盤面全体に降る（15 枚）
    ///     スイカ　：奥から大きなスイカが転がってきて割れ、プッシャーの前にメダルがこぼれる（18 枚）
    ///     BAR　　 ：金の棒が横切りながら、プッシャーの前に横一列のメダルの壁を並べる（12 枚）
    ///     レコード：大きなレコード盤が回りながら降りてきて、回転にのってメダルを渦巻き状に飛ばす（20 枚）
    ///     ダイヤ　：光るダイヤが落ちてきて、盤面でレアメダルに変わる（レアメダル 3 枚）
    ///   確率と枚数は仮。Outcomes の Weight は「左のリールにその絵が出やすい度合い」
    ///   Reach Chance（まん中が左と同じになる確率）と Win Chance（リーチの時に右もそろう確率）で、あたりの出やすさを決める
    /// ・あたりの時は、星とランプが点滅し、アーチの窓に「+30」などの枚数が出る
    /// ・演出は盤面の上だけ（画面全体を光らせたり、カメラを動かしたりはしない）。777 だけ少し派手
    /// ・置き場所は、ワールドの位置・向き・大きさで決める（World Position / World Rotation / World Scale）
    ///   Use World Pose を切ると、前のやり方（画面の割合とカメラからの距離）で置く
    /// ・MedalPusherGame は変えない。シーンに置かなくても、MedalPusherGame があれば再生時に自動で付く
    /// </summary>
    [DisallowMultipleComponent]
    public class SlotRefill : MonoBehaviour
    {
        public enum Symbol { Seven, Cherry, Bell, Melon, Bar, Record, Diamond, Miss }

        [System.Serializable]
        public class Outcome
        {
            public Symbol symbol;
            [Tooltip("出やすさ（重み。全部の出目の重みを足した中の割合で出る）")]
            [Min(0f)] public float weight = 1f;
            [Tooltip("出るメダルの枚数（チェリーは 1 発ぶん）")]
            [Min(0)] public int medals = 10;
        }

        [Tooltip("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;

        [Header("出目（確率は仮。Weight は左のリールに出やすい度合い）")]
        public List<Outcome> outcomes = new List<Outcome>
        {
            new Outcome { symbol = Symbol.Seven,   weight = 2f,  medals = 30 },
            new Outcome { symbol = Symbol.Cherry,  weight = 6f,  medals = 12 },
            new Outcome { symbol = Symbol.Bell,    weight = 8f,  medals = 15 },
            new Outcome { symbol = Symbol.Melon,   weight = 5f,  medals = 18 },
            new Outcome { symbol = Symbol.Bar,     weight = 8f,  medals = 12 },
            new Outcome { symbol = Symbol.Record,  weight = 4f,  medals = 20 },
            new Outcome { symbol = Symbol.Diamond, weight = 3f,  medals = 3 },
        };

        [Tooltip("まん中のリールが、左と同じ絵で止まる確率（リーチ）")]
        [Range(0f, 1f)] public float reachChance = 0.3f;
        [Tooltip("リーチの時に、右のリールもそろう確率（あたり）")]
        [Range(0f, 1f)] public float winChance = 0.4f;

        [Header("回り方")]
        [Tooltip("ためておけるメダルの枚数（ランプは 6 個まで）")]
        [Range(1, 6)] public int maxStock = 4;
        [Tooltip("リールが回る速さ（1 秒に進む絵の数）")]
        [Range(2f, 30f)] public float spinSpeed = 12f;
        [Tooltip("メダルが入ってから、リールが止まるまでの秒数")]
        [Range(0.1f, 2f)] public float stopSeconds = 0.35f;
        [Tooltip("リーチの時、右のリールが止まるまでの秒数（ゆっくり止まる）")]
        [Range(0.1f, 3f)] public float reachStopSeconds = 1.2f;
        [Tooltip("リールが止まってから、次のメダルで止められるようになるまでの秒数")]
        [Range(0f, 3f)] public float restSeconds = 0.2f;
        [Tooltip("3 本止まって判定したあと、3 本とも回り直すまでの秒数")]
        [Range(0f, 3f)] public float resetDelay = 0.9f;

        [Header("置き場所（ワールド）")]
        [Tooltip("オン：下の位置・向き・大きさで置く。オフ：画面の割合（Viewport）で置く")]
        public bool useWorldPose = true;
        public Vector3 worldPosition = new Vector3(1.915926f, 1.327f, -0.15f);
        [Tooltip("向き（度）")]
        public Vector3 worldRotation = new Vector3(3.484f, 83.603f, -0.817f);
        public float worldScale = 0.7f;

        [Header("置き場所（画面の割合。Use World Pose がオフの時だけ。左下が (0,0)、右上が (1,1)）")]
        public Vector2 viewport = new Vector2(0.68f, 0.73f);
        [Tooltip("カメラからの距離（メートル）")]
        [Range(0.5f, 10f)] public float distance = 2.4f;
        [Tooltip("画面の高さに対する、スロットの高さの割合")]
        [Range(0.05f, 0.6f)] public float screenHeight = 0.2f;
        [Tooltip("向きのずれ（度）。X はマイナスで上の面がこちらに傾く、Y はプラスで正面が左（盤面の中央）を向く")]
        public Vector3 tilt = new Vector3(-12f, 30f, 0f);

        [Header("見た目")]
        [Tooltip("リールの明るさ（自分で光る強さ。暗くて出目が見えにくい時に上げる）")]
        [Range(0f, 1.5f)] public float reelBrightness = 0.6f;

        [Header("音")]
        [Range(0f, 1f)] public float volume = 0.55f;

        // ---- 状態 ----
        int stock;
        bool busy;                                  // リールを止めている途中
        int step;                                   // 今の回で止まったリールの数（0〜2）
        readonly Symbol[] got = new Symbol[3];      // 止まった出目
        float restTimer;
        readonly List<Medal> heldMedals = new List<Medal>();   // タワーで持ち上げているメダル（リセットの時に戻す）
        readonly List<GameObject> props = new List<GameObject>(); // 演出の物（リセットの時に消す）

        // ---- 本体 ----
        Transform machine, lever;
        Reel[] reels;
        Renderer[] lampR, starR, buttonR;
        Transform[] buttons;
        Vector3[] buttonBase;
        Material lampOn, lampOff, starOn, starOff, buttonOn, buttonOff;
        Text winText;
        float winTime = -1f, frameGlow, textHold;
        Font font;
        Texture2D[] symbolTex;
        AudioSource audioSource;
        AudioClip sfxSpin, sfxStop, sfxWin, sfxBig, sfxPop, sfxBell, sfxThud, sfxScratch;
        ParticleSystem fx;

        const float ReelRadius = 0.25f;   // ドラムの半径
        const float ReelWidth = 0.22f;    // ドラムの幅

        class Reel
        {
            public Transform drum;
            public Symbol[] strip;
            public float pos, speed, from, to, t, dur;
            public int state;   // 0 止まっている、1 回っている、2 止まりかけ
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (FindFirstObjectByType<SlotRefill>() != null) return;
            var g = FindFirstObjectByType<MedalPusherGame>();
            if (g != null) g.gameObject.AddComponent<SlotRefill>();
        }

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (game == null)
            {
                enabled = false;
                return;
            }
            font = Resources.Load<Font>(MonitorDisplay.FontFolder + "/Display");
            if (font == null) font = Resources.Load<Font>(MonitorDisplay.FontFolder + "/Body");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            MakeSymbolTextures();
            MakeSounds();
            BuildMachine();
            game.MedalDropped += OnDropped;
            game.GameReset += OnReset;
        }

        void OnDestroy()
        {
            if (game != null)
            {
                game.MedalDropped -= OnDropped;
                game.GameReset -= OnReset;
            }
            if (machine != null) Destroy(machine.gameObject);
        }

        void OnDropped(Medal medal, DropZoneType type)
        {
            if (type != DropZoneType.Center) return;
            var s = game.State;
            if (s != GameState.Playing && s != GameState.Grace) return;
            stock = Mathf.Min(maxStock, stock + 1);
        }

        void OnReset()
        {
            StopAllCoroutines();
            stock = 0;
            busy = false;
            restTimer = 0f;
            foreach (var m in heldMedals) if (m != null && m.Body != null) m.Body.isKinematic = false;
            heldMedals.Clear();
            foreach (var p in props) if (p != null) Destroy(p);
            props.Clear();
            winTime = -1f;
            frameGlow = 0f;
            if (winText != null) winText.text = "";
            if (reels != null) RestartReels(false);
        }

        // ================================================================
        // 毎フレーム
        // ================================================================

        void Update()
        {
            if (machine == null) return;
            float dt = Time.deltaTime;
            PlaceMachine();

            if (!busy)
            {
                restTimer -= dt;
                if (stock > 0 && restTimer <= 0f)
                {
                    stock--;
                    StartCoroutine(StopNext());
                }
            }

            foreach (var r in reels) UpdateReel(r, dt);

            // ランプ（ためているメダル）と星（止まったリール）。あたりの時は点滅
            frameGlow = Mathf.MoveTowards(frameGlow, 0f, dt * 0.6f);
            bool blink = frameGlow > 0f && Mathf.Repeat(Time.time * 7f, 1f) < 0.5f;
            for (int i = 0; i < lampR.Length; i++)
                lampR[i].sharedMaterial = (frameGlow > 0f ? blink : i < stock) ? lampOn : lampOff;
            for (int i = 0; i < starR.Length; i++)
                starR[i].sharedMaterial = (frameGlow > 0f ? blink : i < step) ? starOn : starOff;

            if (winTime >= 0f)
            {
                winTime += dt;
                float k = winTime < 0.2f ? winTime / 0.2f : 1f;
                winText.rectTransform.localScale = Vector3.one * (0.6f + 0.4f * Ease.OutBack(k));
                if (winTime > textHold) { winTime = -1f; winText.text = ""; }
            }
        }

        /// <summary>画面の左上（Viewport）に、カメラの方を向けて置く。大きさは画面の高さに合わせる。</summary>
        void PlaceMachine()
        {
            if (useWorldPose)
            {
                machine.position = worldPosition;
                machine.rotation = Quaternion.Euler(worldRotation);
                machine.localScale = Vector3.one * worldScale;
                return;
            }
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 pos = cam.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, distance));
            machine.position = pos;
            machine.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up) * Quaternion.Euler(tilt);
            float worldH = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * screenHeight;
            machine.localScale = Vector3.one * (worldH / 1.4f);   // 本体の高さを 1.4 で作っている
        }

        // ================================================================
        // 回す
        // ================================================================

        /// <summary>左から 1 本だけリールを止める。3 本止まったところで、そろえばあたり、そろわなければ回り直し。</summary>
        IEnumerator StopNext()
        {
            busy = true;
            int i = step;
            bool reach = i == 2 && got[0] == got[1];
            Symbol target;
            if (i == 0) target = PickFirst();
            else if (i == 1) target = Random.value < reachChance ? got[0] : OtherThan(got[0]);
            else if (reach) target = Random.value < winChance ? got[0] : OtherThan(got[0]);
            else target = PickFirst();   // 左とまん中がちがう時は、右は何が出てもハズレ
            var r = reels[i];
            float dur = reach ? reachStopSeconds : stopSeconds;
            StopReel(r, target, dur, reach);
            PressButton(i, true);
            yield return new WaitForSeconds(dur + 0.05f);
            got[i] = target;
            step = i + 1;

            if (i == 1 && got[0] == got[1])
            {
                // リーチ：右のリールを速く回す
                ShowText("リーチ", new Color(1f, 0.55f, 0.3f), 99f);
                reels[2].speed = spinSpeed * 1.8f;
                Play(sfxSpin, 0.4f, 1.3f);
            }
            else if (i == 2 && !(got[0] == got[1] && got[1] == got[2]))
            {
                // そろわず：少し待って、3 本とも回り直す
                ShowText("そろわず", new Color(0.7f, 0.75f, 0.85f), resetDelay + 0.2f);
                Play(sfxStop, 0.4f, 0.6f);
                yield return new WaitForSeconds(resetDelay);
                RestartReels(true);
            }
            else if (i == 2)
            {
                var outcome = FindOutcome(target);
                frameGlow = 1.6f;
                int shown = target == Symbol.Cherry ? outcome.medals * 2 : outcome.medals;
                ShowText(target == Symbol.Diamond ? $"+{shown} ★" : $"+{shown}", new Color(1f, 0.92f, 0.4f), 2.6f);
                Play(target == Symbol.Seven ? sfxBig : sfxWin, 0.8f);
                StartCoroutine(RunOutcome(outcome));
                yield return new WaitForSeconds(resetDelay + 1.2f);
                RestartReels(true);
            }
            restTimer = restSeconds;
            busy = false;
        }

        /// <summary>3 本とも回り直す（星とボタンも消す）</summary>
        void RestartReels(bool withLever)
        {
            step = 0;
            for (int i = 0; i < reels.Length; i++)
            {
                reels[i].state = 1;
                reels[i].speed = spinSpeed * Random.Range(0.9f, 1.1f);
                PressButton(i, false);
            }
            if (withLever)
            {
                Play(sfxSpin, 0.5f);
                StartCoroutine(PullLever());
            }
        }

        void PressButton(int i, bool on)
        {
            if (buttons == null || i >= buttons.Length) return;
            buttons[i].localPosition = buttonBase[i] + (on ? Vector3.down * 0.012f : Vector3.zero);
            buttonR[i].sharedMaterial = on ? buttonOn : buttonOff;
        }

        void ShowText(string text, Color color, float hold)
        {
            winText.text = text;
            winText.color = color;
            winTime = 0f;
            textHold = hold;
        }

        /// <summary>左のリールの絵（Weight の割合で選ぶ）</summary>
        Symbol PickFirst()
        {
            float total = 0f;
            foreach (var o in outcomes) if (o.symbol != Symbol.Miss) total += Mathf.Max(0f, o.weight);
            if (total <= 0f) return Symbol.Cherry;
            float v = Random.value * total;
            foreach (var o in outcomes)
            {
                if (o.symbol == Symbol.Miss) continue;
                v -= Mathf.Max(0f, o.weight);
                if (v <= 0f) return o.symbol;
            }
            return outcomes[outcomes.Count - 1].symbol;
        }

        static Symbol OtherThan(Symbol s)
        {
            int n = (int)Symbol.Miss;
            return (Symbol)(((int)s + 1 + Random.Range(0, n - 1)) % n);
        }

        Outcome FindOutcome(Symbol s)
        {
            foreach (var o in outcomes) if (o.symbol == s) return o;
            return new Outcome { symbol = s, medals = 10 };
        }

        IEnumerator PullLever()
        {
            for (float t = 0f; t < 0.4f; t += Time.deltaTime)
            {
                float k = t < 0.15f ? t / 0.15f : 1f - (t - 0.15f) / 0.25f;
                lever.localRotation = Quaternion.Euler(-60f * Mathf.Clamp01(k), 0f, 0f);
                yield return null;
            }
            lever.localRotation = Quaternion.identity;
        }

        /// <summary>回っているリールを、目当ての絵で止める（OutBack で少し行きすぎて戻る）</summary>
        void StopReel(Reel r, Symbol target, float seconds, bool extraTurn)
        {
            int n = r.strip.Length;
            int idx = System.Array.IndexOf(r.strip, target);
            if (idx < 0) idx = 0;
            float baseCell = Mathf.Ceil(r.pos);
            int cur = ((int)baseCell % n + n) % n;
            int ahead = ((idx - cur) % n + n) % n;
            r.from = r.pos;
            r.to = baseCell + ahead;
            if (extraTurn || r.to - r.pos < 0.6f) r.to += n;   // リーチの時は 1 周してゆっくり止まる
            r.t = 0f;
            r.dur = seconds;
            r.state = 2;
        }

        void UpdateReel(Reel r, float dt)
        {
            if (r.state == 1) r.pos += r.speed * dt;
            else if (r.state == 2)
            {
                r.t += dt;
                float k = Mathf.Clamp01(r.t / r.dur);
                r.pos = Mathf.LerpUnclamped(r.from, r.to, Ease.OutBack(k));
                if (k >= 1f)
                {
                    r.pos = r.to;
                    r.state = 0;
                    Play(sfxStop, 0.5f);
                }
            }
            // pos = k の時、strip[k] が正面のまん中に来る（絵は上から下へ流れる）
            r.drum.localRotation = Quaternion.Euler(-(r.pos + 0.5f) * 360f / r.strip.Length, 0f, 0f);
        }

        // ================================================================
        // 出目ごとの演出
        // ================================================================

        IEnumerator RunOutcome(Outcome o)
        {
            switch (o.symbol)
            {
                case Symbol.Seven: yield return Tower(o.medals); break;
                case Symbol.Cherry:
                    StartCoroutine(CherryBomb(o.medals, -1));
                    yield return new WaitForSeconds(0.35f);
                    yield return CherryBomb(o.medals, 1);
                    break;
                case Symbol.Bell: yield return BellShower(o.medals); break;
                case Symbol.Melon: yield return Watermelon(o.medals); break;
                case Symbol.Bar: yield return BarWall(o.medals); break;
                case Symbol.Record: yield return RecordSpin(o.medals); break;
                case Symbol.Diamond: yield return DiamondDrop(o.medals); break;
            }
        }

        // ---- 盤面の寸法 ----
        float D => game.tuning.medalDiameter;
        float Half => game.BoardHalfWidth - D * 0.6f;
        float FrontZ => game.layout.floorFrontZ + D * 0.7f;
        float PushFrontZ => game.layout.pusherRetractZ - (game.Pusher != null ? game.Pusher.stroke : game.tuning.pusherStroke);
        float LowBackZ => Mathf.Max(FrontZ + D, PushFrontZ - D * 0.6f);

        /// <summary>その場所の床の高さ（メダルや動く物は数えない）</summary>
        float FloorY(float x, float z)
        {
            var hits = Physics.RaycastAll(new Vector3(x, 6f, z), Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits)
            {
                if (h.collider.GetComponentInParent<Medal>() != null) continue;
                var rb = h.collider.attachedRigidbody;
                if (rb != null && !rb.isKinematic) continue;
                if (machine != null && h.collider.transform.IsChildOf(machine)) continue;
                if (h.point.y > best) best = h.point.y;
            }
            return float.IsNegativeInfinity(best) ? 0f : best;
        }

        Medal SpawnMedal(Vector3 pos, Vector3 vel, bool rare = false, bool flat = false)
        {
            var rot = flat ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
                           : Quaternion.Euler(Random.Range(-20f, 20f), Random.Range(0f, 360f), Random.Range(-20f, 20f));
            var m = game.Pool.Spawn(pos, rot);
            if (rare) game.Pool.SetRare(m, true);
            m.Body.velocity = vel;
            m.Body.angularVelocity = Random.insideUnitSphere * 4f;
            return m;
        }

        /// <summary>777：盤面からメダルのタワーがせり上がり、てっぺんで崩れて広がる</summary>
        IEnumerator Tower(int count)
        {
            float x = Random.Range(-Half * 0.35f, Half * 0.35f);
            float z = Mathf.Lerp(FrontZ, LowBackZ, 0.55f);
            float fy = FloorY(x, z);
            float th = game.tuning.medalThickness * 1.02f;
            float height = count * th;
            // せり上がる間は見た目だけの金の柱（本物のメダルを床の下に置くと、落ちた判定に入ることがあるため）
            var gold = ProtoAssets.MakeMaterial(new Color(1f, 0.8f, 0.3f), 0.9f, 0.75f);
            var pillar = Prim(PrimitiveType.Cylinder, null, Vector3.zero, Vector3.one, gold);
            props.Add(pillar.gameObject);
            Burst(new Vector3(x, fy + D * 0.1f, z), new Color(1f, 0.85f, 0.3f), 30, 1.2f, Vector3.up);
            Play(sfxScratch, 0.4f, 0.7f);
            float rise = 1.3f;
            for (float t = 0f; t < rise; t += Time.deltaTime)
            {
                float h = Mathf.Max(0.001f, height * Ease.OutCubic(t / rise));
                pillar.position = new Vector3(x, fy + h * 0.5f, z);
                pillar.localScale = new Vector3(D * 0.98f, h * 0.5f, D * 0.98f);   // Cylinder は高さ 2
                if (Random.value < 0.4f) Burst(new Vector3(x, fy + h, z), new Color(1f, 0.9f, 0.4f), 2, 0.6f, Vector3.up);
                yield return null;
            }
            // 本物のメダルに入れかえる（同じ場所に積む）
            Destroy(pillar.gameObject);
            props.Remove(pillar.gameObject);
            var stack = new List<Medal>();
            for (int i = 0; i < count; i++)
            {
                var m = SpawnMedal(new Vector3(x, fy + th * (i + 0.5f), z), Vector3.zero, false, true);
                m.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                m.Body.rotation = m.transform.rotation;
                m.Body.isKinematic = true;
                stack.Add(m);
                heldMedals.Add(m);
            }
            Burst(new Vector3(x, fy + height, z), new Color(1f, 0.95f, 0.5f), 40, 1.6f, Vector3.up);
            Play(sfxPop, 0.6f, 0.8f);
            yield return new WaitForSeconds(0.35f);
            // 崩れる：上の方ほど大きく横へ
            Vector2 lean = Random.insideUnitCircle.normalized;
            for (int i = 0; i < stack.Count; i++)
            {
                var m = stack[i];
                heldMedals.Remove(m);
                if (m == null || m.Body == null || !m.IsActiveInPool) continue;
                m.Body.isKinematic = false;
                float up = (float)i / Mathf.Max(1, stack.Count - 1);   // 0 = いちばん下
                var dir = new Vector3(lean.x, 0f, lean.y) * (0.2f + 1.4f * up) + Random.insideUnitSphere * 0.3f;
                dir.y = Random.Range(0f, 0.6f) * up;
                m.Body.velocity = dir;
                m.Body.WakeUp();
            }
        }

        /// <summary>チェリー：上からチェリーの爆弾が落ちてきて、盤面で破裂してメダルが飛び散る</summary>
        IEnumerator CherryBomb(int count, int side)
        {
            float x = Mathf.Clamp(side * Random.Range(Half * 0.2f, Half * 0.65f), -Half, Half);
            float z = Mathf.Lerp(FrontZ, LowBackZ, Random.Range(0.35f, 0.75f));
            float fy = FloorY(x, z);
            var bomb = new GameObject("CherryBomb");
            props.Add(bomb);
            var red = ProtoAssets.MakeMaterial(new Color(0.85f, 0.08f, 0.15f), 0.1f, 0.8f);
            var green = ProtoAssets.MakeMaterial(new Color(0.2f, 0.6f, 0.2f), 0f, 0.4f);
            Prim(PrimitiveType.Sphere, bomb.transform, new Vector3(-0.32f, 0f, 0f), Vector3.one * 0.7f, red);
            Prim(PrimitiveType.Sphere, bomb.transform, new Vector3(0.32f, -0.05f, 0f), Vector3.one * 0.66f, red);
            var s1 = Prim(PrimitiveType.Cylinder, bomb.transform, new Vector3(-0.15f, 0.55f, 0f), new Vector3(0.06f, 0.3f, 0.06f), green);
            s1.localRotation = Quaternion.Euler(0f, 0f, -25f);
            var s2 = Prim(PrimitiveType.Cylinder, bomb.transform, new Vector3(0.18f, 0.52f, 0f), new Vector3(0.06f, 0.28f, 0.06f), green);
            s2.localRotation = Quaternion.Euler(0f, 0f, 25f);
            Prim(PrimitiveType.Sphere, bomb.transform, new Vector3(0.02f, 0.85f, 0f), new Vector3(0.25f, 0.08f, 0.15f), green);   // 葉
            bomb.transform.localScale = Vector3.one * D * 1.4f;
            float startY = fy + 2.2f, fall = 0.65f;
            for (float t = 0f; t < fall; t += Time.deltaTime)
            {
                float k = t / fall;
                bomb.transform.position = new Vector3(x, Mathf.Lerp(startY, fy + D * 0.4f, k * k), z);
                bomb.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 20f) * 10f);
                yield return null;
            }
            // 着地：ふくらんで破裂
            Play(sfxThud, 0.5f);
            for (float t = 0f; t < 0.18f; t += Time.deltaTime)
            {
                bomb.transform.localScale = Vector3.one * D * 1.4f * (1f + t * 2.2f);
                yield return null;
            }
            Vector3 c = new Vector3(x, fy + D * 0.4f, z);
            Destroy(bomb);
            props.Remove(bomb);
            Burst(c, new Color(1f, 0.25f, 0.35f), 40, 2.2f, null);
            Burst(c, new Color(1f, 0.85f, 0.4f), 20, 1.6f, null);
            Play(sfxPop, 0.8f);
            for (int i = 0; i < count; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                var v = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(0.6f, 1.4f) + Vector3.up * Random.Range(1.4f, 2.4f);
                SpawnMedal(c + Vector3.up * D * 0.3f, v);
            }
        }

        /// <summary>ベル：盤面の上に大きなベルが出て鳴り、メダルのシャワーが盤面全体に降る</summary>
        IEnumerator BellShower(int count)
        {
            float z = Mathf.Lerp(FrontZ, LowBackZ, 0.5f);
            float fy = FloorY(0f, z);
            var bell = new GameObject("Bell");
            props.Add(bell);
            var gold = ProtoAssets.MakeMaterial(new Color(1f, 0.78f, 0.2f), 0.9f, 0.75f);
            Prim(PrimitiveType.Sphere, bell.transform, new Vector3(0f, 0.25f, 0f), new Vector3(0.7f, 0.7f, 0.7f), gold);
            Prim(PrimitiveType.Cylinder, bell.transform, new Vector3(0f, -0.05f, 0f), new Vector3(0.82f, 0.3f, 0.82f), gold);
            Prim(PrimitiveType.Cylinder, bell.transform, new Vector3(0f, -0.3f, 0f), new Vector3(1.05f, 0.05f, 1.05f), gold);
            Prim(PrimitiveType.Sphere, bell.transform, new Vector3(0f, -0.4f, 0f), Vector3.one * 0.22f, gold);
            Prim(PrimitiveType.Cylinder, bell.transform, new Vector3(0f, 0.68f, 0f), new Vector3(0.12f, 0.08f, 0.12f), gold);
            Vector3 c = new Vector3(0f, fy + 1.0f, z);
            float size = D * 2.2f;
            bool rang = false, rang2 = false;
            for (float t = 0f; t < 2.4f; t += Time.deltaTime)
            {
                float pop = Ease.OutBack(Mathf.Clamp01(t / 0.35f));
                float gone = Mathf.Clamp01((t - 2.0f) / 0.4f);
                bell.transform.position = c;
                bell.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 9f) * 22f * (1f - gone));
                bell.transform.localScale = Vector3.one * size * pop * (1f - gone);
                if (!rang && t >= 0.4f)
                {
                    rang = true;
                    Play(sfxBell, 0.6f);
                    StartCoroutine(BellRain(count, fy));
                }
                if (!rang2 && t >= 1.0f) { rang2 = true; Play(sfxBell, 0.5f, 1.12f); }
                yield return null;
            }
            Destroy(bell);
            props.Remove(bell);
        }

        IEnumerator BellRain(int count, float fy)
        {
            for (int i = 0; i < count; i++)
            {
                float x = Random.Range(-Half, Half);
                float z = Random.Range(FrontZ, game.BackWallFrontZ - D);
                SpawnMedal(new Vector3(x, fy + 1.3f, z), Vector3.down * 0.5f);
                yield return new WaitForSeconds(0.08f);
            }
        }

        /// <summary>スイカ：奥から大きなスイカが転がってきて割れ、プッシャーの前にメダルがこぼれる</summary>
        IEnumerator Watermelon(int count)
        {
            float x = Random.Range(-Half * 0.5f, Half * 0.5f);
            float zFrom = game.BackWallFrontZ - D;
            float zTo = PushFrontZ - D * 0.9f;
            float yFrom = FloorY(x, zFrom), yTo = FloorY(x, zTo);
            float r = D * 0.9f;
            var melon = new GameObject("Watermelon");
            props.Add(melon);
            var green = ProtoAssets.MakeMaterial(new Color(0.15f, 0.55f, 0.2f), 0f, 0.6f);
            var dark = ProtoAssets.MakeMaterial(new Color(0.05f, 0.3f, 0.1f), 0f, 0.6f);
            var ball = Prim(PrimitiveType.Sphere, melon.transform, Vector3.zero, Vector3.one, green);
            for (int i = 0; i < 4; i++)
            {
                var stripe = Prim(PrimitiveType.Sphere, ball, Vector3.zero, new Vector3(1.02f, 1.02f, 0.18f), dark);
                stripe.localRotation = Quaternion.Euler(0f, i * 45f, 0f);
            }
            melon.transform.localScale = Vector3.one * r * 2f;
            float dur = 1.1f;
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                float k = t / dur;
                float z = Mathf.Lerp(zFrom, zTo, k);
                float y = Mathf.Lerp(yFrom, yTo, k * k) + r;
                melon.transform.position = new Vector3(x, y, z);
                ball.localRotation = Quaternion.Euler(-(zFrom - z) / r * Mathf.Rad2Deg, 0f, 0f);   // 転がる
                yield return null;
            }
            Play(sfxThud, 0.6f);
            Vector3 c = new Vector3(x, yTo + r * 0.6f, zTo);
            // 割れる：左右に開いて消える
            var red = ProtoAssets.MakeMaterial(new Color(0.95f, 0.2f, 0.25f), 0f, 0.5f);
            var halfL = Prim(PrimitiveType.Sphere, null, c, new Vector3(r * 1.0f, r * 2f, r * 2f), red);
            var halfR = Prim(PrimitiveType.Sphere, null, c, new Vector3(r * 1.0f, r * 2f, r * 2f), red);
            props.Add(halfL.gameObject);
            props.Add(halfR.gameObject);
            Destroy(melon);
            props.Remove(melon);
            Burst(c, new Color(1f, 0.3f, 0.35f), 30, 1.6f, null);
            Play(sfxPop, 0.7f, 0.8f);
            for (int i = 0; i < count; i++)
            {
                var v = new Vector3(Random.Range(-0.8f, 0.8f), Random.Range(0.8f, 1.8f), Random.Range(-1.0f, -0.2f));
                SpawnMedal(c, v);
            }
            for (float t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                float k = t / 0.5f;
                halfL.position = c + Vector3.left * r * 1.5f * k;
                halfR.position = c + Vector3.right * r * 1.5f * k;
                halfL.localScale = halfR.localScale = new Vector3(r, r * 2f, r * 2f) * (1f - k);
                yield return null;
            }
            Destroy(halfL.gameObject);
            Destroy(halfR.gameObject);
            props.Remove(halfL.gameObject);
            props.Remove(halfR.gameObject);
        }

        /// <summary>BAR：金の棒が横切りながら、プッシャーの前に横一列のメダルの壁を並べる</summary>
        IEnumerator BarWall(int count)
        {
            float z = PushFrontZ - D * 0.75f;
            float fy = FloorY(0f, z);
            var bar = Prim(PrimitiveType.Cube, null, Vector3.zero, new Vector3(D * 2.2f, D * 0.6f, D * 0.6f),
                ProtoAssets.MakeMaterial(new Color(1f, 0.8f, 0.25f), 0.9f, 0.8f));
            props.Add(bar.gameObject);
            for (int i = 0; i < count; i++)
            {
                float k = count <= 1 ? 0.5f : (float)i / (count - 1);
                float x = Mathf.Lerp(-Half, Half, k);
                bar.position = new Vector3(x, fy + D * 1.6f, z);
                SpawnMedal(new Vector3(x, FloorY(x, z) + D * 0.35f, z), Vector3.down * 0.3f, false, true);
                if (i % 3 == 0) Play(sfxStop, 0.35f, 1.3f);
                yield return new WaitForSeconds(0.07f);
            }
            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                bar.localScale = new Vector3(D * 2.2f, D * 0.6f, D * 0.6f) * (1f - t / 0.3f);
                yield return null;
            }
            Destroy(bar.gameObject);
            props.Remove(bar.gameObject);
        }

        /// <summary>レコード：大きなレコード盤が回りながら降りてきて、回転にのってメダルを渦巻き状に飛ばす</summary>
        IEnumerator RecordSpin(int count)
        {
            float z = Mathf.Lerp(FrontZ, LowBackZ, 0.55f);
            float fy = FloorY(0f, z);
            float radius = D * 1.5f;
            var disc = new GameObject("BigRecord");
            props.Add(disc);
            var black = ProtoAssets.MakeMaterial(new Color(0.06f, 0.06f, 0.08f), 0.3f, 0.9f);
            var label = ProtoAssets.MakeMaterial(new Color(0.9f, 0.25f, 0.3f), 0f, 0.4f);
            Prim(PrimitiveType.Cylinder, disc.transform, Vector3.zero, new Vector3(2f, 0.02f, 2f), black);
            Prim(PrimitiveType.Cylinder, disc.transform, new Vector3(0f, 0.025f, 0f), new Vector3(0.7f, 0.02f, 0.7f), label);
            Prim(PrimitiveType.Cylinder, disc.transform, new Vector3(0f, 0.04f, 0f), new Vector3(0.08f, 0.03f, 0.08f), ProtoAssets.MakeMaterial(Color.white, 0f, 0.5f));
            disc.transform.localScale = Vector3.one * radius;
            Vector3 top = new Vector3(0f, fy + 1.4f, z), hover = new Vector3(0f, fy + D * 2.2f, z);
            Play(sfxScratch, 0.45f);
            float angle = 0f;
            for (float t = 0f; t < 0.6f; t += Time.deltaTime)
            {
                disc.transform.position = Vector3.Lerp(top, hover, Ease.OutCubic(t / 0.6f));
                angle += 360f * Time.deltaTime;
                disc.transform.rotation = Quaternion.Euler(8f, angle, 0f);
                yield return null;
            }
            float emit = 1.6f, acc = 0f;
            int spawned = 0;
            for (float t = 0f; t < emit; t += Time.deltaTime)
            {
                angle += 540f * Time.deltaTime;
                disc.transform.rotation = Quaternion.Euler(8f, angle, 0f);
                acc += count / emit * Time.deltaTime;
                while (acc >= 1f && spawned < count)
                {
                    acc -= 1f;
                    spawned++;
                    float a = -angle * Mathf.Deg2Rad;
                    var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var tangent = new Vector3(-radial.z, 0f, radial.x);
                    var p = hover + radial * radius * 1.05f;
                    p.x = Mathf.Clamp(p.x, -Half, Half);
                    SpawnMedal(p, tangent * 1.2f + radial * 0.7f + Vector3.down * 0.2f);
                }
                yield return null;
            }
            while (spawned < count) { spawned++; SpawnMedal(hover, Random.insideUnitSphere); }
            for (float t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                float k = t / 0.5f;
                disc.transform.position = Vector3.Lerp(hover, top, k * k);
                disc.transform.localScale = Vector3.one * radius * (1f - k);
                yield return null;
            }
            Destroy(disc);
            props.Remove(disc);
        }

        /// <summary>ダイヤ：光るダイヤが落ちてきて、盤面でレアメダルに変わる</summary>
        IEnumerator DiamondDrop(int count)
        {
            float x = Random.Range(-Half * 0.5f, Half * 0.5f);
            float z = Mathf.Lerp(FrontZ, LowBackZ, 0.5f);
            float fy = FloorY(x, z);
            var mat = ProtoAssets.MakeMaterial(new Color(0.5f, 0.95f, 1f), 0.2f, 0.95f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0.3f, 0.8f, 1f) * 1.5f);
            var gem = Prim(PrimitiveType.Cube, null, Vector3.zero, Vector3.one * D * 0.9f, mat);
            props.Add(gem.gameObject);
            float fall = 0.8f;
            for (float t = 0f; t < fall; t += Time.deltaTime)
            {
                float k = t / fall;
                gem.position = new Vector3(x, Mathf.Lerp(fy + 2f, fy + D * 0.6f, k * k), z);
                gem.rotation = Quaternion.Euler(45f, t * 360f, 45f);
                if (Random.value < 0.5f) Burst(gem.position, new Color(0.6f, 0.95f, 1f), 1, 0.3f, Vector3.up);
                yield return null;
            }
            Vector3 c = new Vector3(x, fy + D * 0.5f, z);
            Destroy(gem.gameObject);
            props.Remove(gem.gameObject);
            Burst(c, new Color(0.6f, 0.95f, 1f), 40, 1.8f, null);
            Burst(c, Color.white, 20, 1.2f, Vector3.up);
            Play(sfxBell, 0.6f, 1.6f);
            for (int i = 0; i < count; i++)
            {
                var v = new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(0.8f, 1.4f), Random.Range(-0.5f, 0.5f));
                SpawnMedal(c, v, true);
            }
        }

        // ================================================================
        // 本体を作る
        // ================================================================

        void BuildMachine()
        {
            machine = new GameObject("SlotRefill").transform;
            var red = ProtoAssets.MakeMaterial(new Color(0.78f, 0.1f, 0.12f), 0.1f, 0.6f);
            var blue = ProtoAssets.MakeMaterial(new Color(0.12f, 0.24f, 0.62f), 0.1f, 0.55f);
            var dark = ProtoAssets.MakeMaterial(new Color(0.08f, 0.08f, 0.1f), 0.2f, 0.5f);
            var black = ProtoAssets.MakeMaterial(new Color(0.02f, 0.02f, 0.03f), 0f, 0.3f);
            var white = ProtoAssets.MakeMaterial(new Color(0.95f, 0.95f, 0.95f), 0f, 0.6f);
            lampOff = ProtoAssets.MakeMaterial(new Color(0.55f, 0.42f, 0.08f), 0f, 0.6f);
            lampOn = Glow(new Color(1f, 0.85f, 0.15f), 1.6f);
            buttonOff = white;
            buttonOn = Glow(new Color(1f, 0.95f, 0.7f), 1.2f);
            var unlit = Shader.Find("Sprites/Default");
            starOff = new Material(unlit) { color = new Color(0.85f, 0.85f, 0.9f) };
            starOff.mainTexture = Paint(StarShape, 64);
            starOn = new Material(starOff) { color = new Color(1f, 0.85f, 0.2f) };

            // 下の台（赤）とメダルの口。前が -Z
            Prim(PrimitiveType.Cube, machine, new Vector3(0f, -0.55f, -0.02f), new Vector3(0.84f, 0.4f, 0.56f), red);
            Prim(PrimitiveType.Cube, machine, new Vector3(0f, -0.6f, -0.3f), new Vector3(0.36f, 0.11f, 0.015f), dark);
            Prim(PrimitiveType.Cube, machine, new Vector3(0f, -0.6f, -0.308f), new Vector3(0.3f, 0.06f, 0.01f), black);
            // ボタンの台（手前に下がる斜めの板）とボタン 3 つ
            var ledge = new GameObject("Ledge").transform;
            ledge.SetParent(machine, false);
            ledge.localPosition = new Vector3(0f, -0.37f, -0.33f);
            ledge.localRotation = Quaternion.Euler(-22f, 0f, 0f);
            Prim(PrimitiveType.Cube, ledge, Vector3.zero, new Vector3(0.8f, 0.07f, 0.2f), red);
            Prim(PrimitiveType.Cube, ledge, new Vector3(0f, 0f, -0.1f), new Vector3(0.82f, 0.08f, 0.02f), dark);
            buttons = new Transform[3];
            buttonR = new Renderer[3];
            buttonBase = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 0.22f;
                Prim(PrimitiveType.Cylinder, ledge, new Vector3(x, 0.036f, 0f), new Vector3(0.11f, 0.006f, 0.11f), dark);
                buttons[i] = Prim(PrimitiveType.Cylinder, ledge, new Vector3(x, 0.048f, 0f), new Vector3(0.085f, 0.014f, 0.085f), white);
                buttonR[i] = buttons[i].GetComponent<Renderer>();
                buttonBase[i] = buttons[i].localPosition;
            }

            // まん中（リールの所）の本体と、黒いふち
            Prim(PrimitiveType.Cube, machine, new Vector3(0f, 0.05f, 0f), new Vector3(0.84f, 0.8f, 0.5f), dark);
            // ふちは薄くして、リールの前の面とほぼ同じ高さにする（ななめから見ても出目がかくれないように）
            Prim(PrimitiveType.Cube, machine, new Vector3(-0.39f, 0.05f, -0.31f), new Vector3(0.06f, 0.8f, 0.04f), dark);
            Prim(PrimitiveType.Cube, machine, new Vector3(0.39f, 0.05f, -0.31f), new Vector3(0.06f, 0.8f, 0.04f), dark);
            Prim(PrimitiveType.Cube, machine, new Vector3(0f, 0.43f, -0.31f), new Vector3(0.84f, 0.05f, 0.04f), dark);
            Prim(PrimitiveType.Cube, machine, new Vector3(0f, 0.16f, -0.31f), new Vector3(0.84f, 0.04f, 0.04f), dark);
            Prim(PrimitiveType.Cube, machine, new Vector3(0f, -0.31f, -0.31f), new Vector3(0.84f, 0.06f, 0.04f), dark);
            for (int i = 0; i < 2; i++)
                Prim(PrimitiveType.Cube, machine, new Vector3((i - 0.5f) * 0.25f, -0.065f, -0.31f), new Vector3(0.03f, 0.43f, 0.04f), dark);
            Prim(PrimitiveType.Cube, machine, new Vector3(0f, 0.29f, -0.27f), new Vector3(0.74f, 0.24f, 0.02f), black);

            // 横の板（青）
            for (int s = -1; s <= 1; s += 2)
            {
                Prim(PrimitiveType.Cube, machine, new Vector3(s * 0.45f, -0.17f, 0f), new Vector3(0.06f, 1.16f, 0.58f), blue);
                Prim(PrimitiveType.Cube, machine, new Vector3(s * 0.45f, -0.17f, -0.29f), new Vector3(0.075f, 1.16f, 0.03f), dark);
            }

            // 星 3 つ（止まったリールの上が光る）
            starR = new Renderer[3];
            for (int i = 0; i < 3; i++)
            {
                var q = Prim(PrimitiveType.Quad, machine, new Vector3((i - 1) * 0.25f, 0.29f, -0.285f), Vector3.one * 0.14f, starOff);
                starR[i] = q.GetComponent<Renderer>();
            }

            // リール（回るドラム）
            reels = new Reel[3];
            var drumMesh = MakeDrumMesh(36);
            for (int i = 0; i < 3; i++)
            {
                var strip = ReelStrip(i);
                var tex = ReelTexture(strip);
                var mat = ProtoAssets.MakeMaterial(Color.white, 0f, 0.1f);
                mat.mainTexture = tex;
                // 少し自分で光らせて、暗い場所でも出目が見えるようにする
                mat.EnableKeyword("_EMISSION");
                mat.SetTexture("_EmissionMap", tex);
                mat.SetColor("_EmissionColor", Color.white * reelBrightness);
                var go = new GameObject("Reel" + i);
                go.transform.SetParent(machine, false);
                // 前の面が z = -0.335（ふちの前の面とほぼ同じ）に来るように置く
                go.transform.localPosition = new Vector3((i - 1) * 0.25f, -0.07f, -0.335f + ReelRadius);
                go.transform.localScale = new Vector3(ReelWidth, ReelRadius, ReelRadius);
                go.AddComponent<MeshFilter>().sharedMesh = drumMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var r = new Reel { drum = go.transform, strip = strip, pos = Random.Range(0, strip.Length), state = 1, speed = spinSpeed * Random.Range(0.9f, 1.1f) };
                reels[i] = r;
            }

            // 上のアーチ（赤）と、中の黒い窓
            var arch = Prim(PrimitiveType.Cylinder, machine, new Vector3(0f, 0.45f, 0.03f), new Vector3(0.8f, 0.2f, 0.8f), red);
            arch.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var trim = Prim(PrimitiveType.Cylinder, machine, new Vector3(0f, 0.45f, -0.18f), new Vector3(0.7f, 0.02f, 0.7f), dark);
            trim.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var inner = Prim(PrimitiveType.Cylinder, machine, new Vector3(0f, 0.45f, -0.2f), new Vector3(0.6f, 0.015f, 0.6f), black);
            inner.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // 窓の中：お札、白い玉、黄色いランプ（ためているメダルの数）
            var bill = Prim(PrimitiveType.Cube, machine, new Vector3(-0.12f, 0.72f, -0.22f), new Vector3(0.18f, 0.08f, 0.01f),
                ProtoAssets.MakeMaterial(new Color(0.35f, 0.75f, 0.4f), 0f, 0.4f));
            bill.localRotation = Quaternion.Euler(0f, 0f, 22f);
            var ballPos = new[] { new Vector2(0.06f, 0.65f), new Vector2(0.13f, 0.65f), new Vector2(0.2f, 0.65f), new Vector2(0.095f, 0.71f), new Vector2(0.165f, 0.71f), new Vector2(0.13f, 0.77f) };
            foreach (var b in ballPos) Prim(PrimitiveType.Sphere, machine, new Vector3(b.x, b.y, -0.23f), Vector3.one * 0.065f, white);
            int lampCount = Mathf.Clamp(maxStock, 1, 6);
            lampR = new Renderer[lampCount];
            float span = Mathf.Min(0.42f, 0.12f * (lampCount - 1));
            for (int i = 0; i < lampCount; i++)
            {
                float x = lampCount == 1 ? 0f : -span * 0.5f + span * i / (lampCount - 1);
                Prim(PrimitiveType.Cylinder, machine, new Vector3(x, 0.5f, -0.23f), new Vector3(0.05f, 0.015f, 0.05f), dark);
                var bulb = Prim(PrimitiveType.Sphere, machine, new Vector3(x, 0.555f, -0.23f), new Vector3(0.075f, 0.09f, 0.06f), lampOff);
                lampR[i] = bulb.GetComponent<Renderer>();
            }

            // レバー（右側）
            Prim(PrimitiveType.Cube, machine, new Vector3(0.49f, -0.05f, 0f), new Vector3(0.04f, 0.16f, 0.16f), dark);
            lever = new GameObject("Lever").transform;
            lever.SetParent(machine, false);
            lever.localPosition = new Vector3(0.53f, -0.05f, 0f);
            var hub = Prim(PrimitiveType.Cylinder, lever, Vector3.zero, new Vector3(0.12f, 0.03f, 0.12f), dark);
            hub.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Prim(PrimitiveType.Cylinder, lever, new Vector3(0f, 0.28f, 0f), new Vector3(0.03f, 0.28f, 0.03f), dark);
            Prim(PrimitiveType.Sphere, lever, new Vector3(0f, 0.57f, 0f), Vector3.one * 0.11f, black);

            // あたりの枚数・リーチの文字（アーチの窓の前）
            var cgo = new GameObject("SlotCanvas", typeof(RectTransform));
            cgo.transform.SetParent(machine, false);
            var canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var crt = (RectTransform)cgo.transform;
            crt.sizeDelta = new Vector2(240f, 90f);
            crt.localScale = Vector3.one * (1f / 400f);
            crt.localPosition = new Vector3(0f, 0.66f, -0.26f);
            var wt = UiRect("Win", crt, Vector2.zero, new Vector2(240f, 90f));
            winText = wt.gameObject.AddComponent<Text>();
            winText.font = font;
            winText.fontSize = 56;
            winText.fontStyle = FontStyle.Bold;
            winText.alignment = TextAnchor.MiddleCenter;
            winText.color = new Color(1f, 0.92f, 0.4f);
            winText.horizontalOverflow = HorizontalWrapMode.Overflow;
            winText.raycastTarget = false;
            winText.text = "";
            var outline = wt.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(3f, -3f);

            // 粒
            var fxGo = new GameObject("SlotFx");
            fxGo.transform.SetParent(transform, false);
            fx = fxGo.AddComponent<ParticleSystem>();
            fx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = fx.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(D * 0.08f, D * 0.2f);
            main.gravityModifier = 0.3f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 2000;
            var em = fx.emission;
            em.rateOverTime = 0f;
            var sh = fx.shape;
            sh.enabled = false;
            var col = fx.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var pr = fxGo.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = new Material(unlit);

            // 説明の枠（カーソルを合わせた時）
            var info = machine.gameObject.AddComponent<HoverInfo>();
            info.title = "スロット";
            info.sub = "メダルの補充";
            info.body = "リールはいつも回っていて、メダルが中央の穴に 1 枚入るたびに左から 1 本ずつ止まる（最大 4 枚までためられる）。\n左とまん中がちがうと、そこで回り直し。3 つそろうと、出目に合わせて盤面にメダルが出る。\n777・チェリー・ベル・スイカ・BAR・レコード・ダイヤ（レアメダル）";
            info.refresh = h => h.status = (step == 2 ? "リーチ中　" : "") + (stock > 0 ? $"ためているメダル {stock}" : "");
        }

        static Material Glow(Color c, float power)
        {
            var m = ProtoAssets.MakeMaterial(c, 0f, 0.6f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * power);
            return m;
        }

        /// <summary>リールのドラム（半径 1、幅 1 の筒の横の面）。v が回る方向（上が +）、u が横</summary>
        static Mesh MakeDrumMesh(int seg)
        {
            var v = new Vector3[(seg + 1) * 2];
            var nrm = new Vector3[v.Length];
            var uv = new Vector2[v.Length];
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var p = new Vector3(0f, Mathf.Sin(a), -Mathf.Cos(a));   // a = 0 が正面（-Z）
                for (int s = 0; s < 2; s++)
                {
                    int k = i * 2 + s;
                    v[k] = new Vector3(s == 0 ? -0.5f : 0.5f, p.y, p.z);
                    nrm[k] = p;
                    uv[k] = new Vector2(s, i / (float)seg);
                }
            }
            var tri = new int[seg * 6];
            for (int i = 0; i < seg; i++)
            {
                int a0 = i * 2, b0 = i * 2 + 1, c0 = i * 2 + 2, d0 = i * 2 + 3;
                tri[i * 6 + 0] = a0; tri[i * 6 + 1] = c0; tri[i * 6 + 2] = d0;
                tri[i * 6 + 3] = a0; tri[i * 6 + 4] = d0; tri[i * 6 + 5] = b0;
            }
            var mesh = new Mesh { name = "SlotDrum" };
            mesh.vertices = v;
            mesh.normals = nrm;
            mesh.uv = uv;
            mesh.triangles = tri;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>リール 1 本ぶんの絵（下から strip の順に縦に並べる）</summary>
        Texture2D ReelTexture(Symbol[] strip)
        {
            int c = symbolTex[0].width;
            var tex = new Texture2D(c, c * strip.Length, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            var bg = new Color(0.97f, 0.96f, 0.92f);
            var px = new Color[c * c * strip.Length];
            // ドラム 1 マスの縦（弧の長さ）÷ 横。絵がつぶれないように、横はこの割合だけ使う
            float aspect = (2f * Mathf.PI * ReelRadius / strip.Length) / ReelWidth;
            for (int k = 0; k < strip.Length; k++)
            {
                var srcTex = symbolTex[(int)strip[k]];
                for (int j = 0; j < c; j++)
                    for (int i = 0; i < c; i++)
                    {
                        float su = 0.5f + ((i + 0.5f) / c - 0.5f) / aspect;
                        var s = su < 0f || su > 1f ? Color.clear : srcTex.GetPixelBilinear(su, (j + 0.5f) / c);
                        // 横のはしは少し暗くする（丸みに見せる）
                        var col = Color.Lerp(bg, new Color(s.r, s.g, s.b, 1f), s.a);
                        float edge = Mathf.Abs((i + 0.5f) / c * 2f - 1f);
                        col *= 1f - 0.18f * edge * edge;
                        col.a = 1f;
                        px[(k * c + j) * c + i] = col;
                    }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>リールの並び（リールごとに少し変える）</summary>
        static Symbol[] ReelStrip(int i)
        {
            var s = new[] { Symbol.Seven, Symbol.Cherry, Symbol.Bell, Symbol.Melon, Symbol.Bar, Symbol.Record, Symbol.Diamond, Symbol.Cherry, Symbol.Bell };
            var list = new List<Symbol>(s);
            for (int k = 0; k < i * 2; k++) { var x = list[0]; list.RemoveAt(0); list.Add(x); }
            return list.ToArray();
        }

        Transform Prim(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            var c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        RectTransform UiRect(string name, RectTransform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        Image UiImage(string name, RectTransform parent, Vector2 pos, Vector2 size, Color color)
        {
            var img = UiRect(name, parent, pos, size).gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        void Burst(Vector3 pos, Color c, int count, float speed, Vector3? dir)
        {
            if (fx == null) return;
            var p = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                Vector3 v = dir.HasValue ? (dir.Value + Random.insideUnitSphere * 0.6f).normalized : Random.onUnitSphere;
                p.position = pos;
                p.velocity = v * speed * Random.Range(0.5f, 1f);
                p.startColor = Color.Lerp(c, Color.white, Random.Range(0f, 0.3f));
                fx.Emit(p, 1);
            }
        }

        // ================================================================
        // 絵（リールの絵柄。7 と BAR は文字）
        // ================================================================

        void MakeSymbolTextures()
        {
            const int n = 64;
            symbolTex = new Texture2D[(int)Symbol.Miss + 1];
            symbolTex[(int)Symbol.Seven] = Paint((x, y) =>
            {
                bool top = y > 0.42f && y < 0.72f && x > -0.55f && x < 0.62f;
                bool diag = Line(x, y, 0.55f, 0.6f, -0.12f, -0.78f, 0.17f);
                return top || diag ? new Color(0.9f, 0.08f, 0.12f) : Color.clear;
            }, n);
            symbolTex[(int)Symbol.Bar] = Paint((x, y) =>
            {
                if (Mathf.Abs(x) > 0.86f || Mathf.Abs(y) > 0.34f) return Color.clear;
                return BarLetter(x, y) ? Color.white : new Color(0.12f, 0.12f, 0.15f);
            }, n);
            symbolTex[(int)Symbol.Cherry] = Paint((x, y) =>
            {
                if (Circle(x, y, -0.32f, -0.35f, 0.32f) || Circle(x, y, 0.3f, -0.42f, 0.3f)) return new Color(0.9f, 0.1f, 0.15f);
                if (Line(x, y, -0.3f, -0.1f, 0.15f, 0.65f, 0.07f) || Line(x, y, 0.28f, -0.15f, 0.15f, 0.65f, 0.07f)) return new Color(0.2f, 0.6f, 0.2f);
                if (Circle(x, y, 0.38f, 0.6f, 0.2f)) return new Color(0.3f, 0.75f, 0.3f);
                return Color.clear;
            }, n);
            symbolTex[(int)Symbol.Bell] = Paint((x, y) =>
            {
                if (Circle(x, y, 0f, 0.15f, 0.45f) && y > -0.1f) return new Color(1f, 0.78f, 0.1f);
                if (y > -0.55f && y <= -0.1f && Mathf.Abs(x) < 0.45f + (-0.1f - y) * 0.6f) return new Color(1f, 0.78f, 0.1f);
                if (Circle(x, y, 0f, -0.68f, 0.13f)) return new Color(0.8f, 0.55f, 0.1f);
                return Color.clear;
            }, n);
            symbolTex[(int)Symbol.Melon] = Paint((x, y) =>
            {
                float r = Mathf.Sqrt(x * x + (y + 0.25f) * (y + 0.25f));
                if (y < -0.25f || r > 0.85f) return Color.clear;
                if (r > 0.72f) return new Color(0.15f, 0.55f, 0.2f);
                if (r > 0.64f) return new Color(0.9f, 0.95f, 0.8f);
                if (Circle(x, y, -0.3f, 0.05f, 0.06f) || Circle(x, y, 0.05f, 0.25f, 0.06f) || Circle(x, y, 0.35f, 0.02f, 0.06f)) return Color.black;
                return new Color(0.95f, 0.25f, 0.3f);
            }, n);
            symbolTex[(int)Symbol.Record] = Paint((x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                if (r > 0.85f) return Color.clear;
                if (r < 0.08f) return new Color(0.95f, 0.95f, 0.9f);
                if (r < 0.32f) return new Color(0.9f, 0.25f, 0.3f);
                return Mathf.Repeat(r * 18f, 1f) < 0.2f ? new Color(0.25f, 0.25f, 0.3f) : new Color(0.08f, 0.08f, 0.1f);
            }, n);
            symbolTex[(int)Symbol.Diamond] = Paint((x, y) =>
            {
                if (Mathf.Abs(x) * 0.9f + Mathf.Abs(y - 0.05f) > 0.75f) return Color.clear;
                return x + y > 0.1f ? new Color(0.6f, 0.95f, 1f) : new Color(0.2f, 0.65f, 0.95f);
            }, n);
            symbolTex[(int)Symbol.Miss] = Paint((x, y) => Color.clear, n);
        }

        static readonly string[][] BarFont =
        {
            new[] { "110", "101", "110", "101", "110" },   // B
            new[] { "010", "101", "111", "101", "101" },   // A
            new[] { "110", "101", "110", "101", "101" },   // R
        };

        /// <summary>BAR の文字（3×5 マスの字）</summary>
        static bool BarLetter(float x, float y)
        {
            if (Mathf.Abs(y) > 0.24f) return false;
            for (int j = 0; j < 3; j++)
            {
                float x0 = -0.72f + j * 0.52f;
                if (x < x0 || x >= x0 + 0.40f) continue;
                int colI = Mathf.Clamp((int)((x - x0) / 0.40f * 3f), 0, 2);
                int row = Mathf.Clamp(4 - (int)((y + 0.24f) / 0.48f * 5f), 0, 4);
                return BarFont[j][row][colI] == '1';
            }
            return false;
        }

        /// <summary>星の形（5 つの角）</summary>
        static Color StarShape(float x, float y)
        {
            float a = Mathf.Atan2(x, y);
            float r = Mathf.Sqrt(x * x + y * y);
            float k = Mathf.Cos(5f * a) * 0.5f + 0.5f;   // 角の方向で 1
            float edge = Mathf.Lerp(0.42f, 0.95f, Mathf.Pow(k, 2.5f));
            return r < edge ? Color.white : Color.clear;
        }

        static Texture2D Paint(System.Func<float, float, Color> f, int n = 32)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                    px[j * n + i] = f((i + 0.5f) / n * 2f - 1f, (j + 0.5f) / n * 2f - 1f);
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static bool Circle(float x, float y, float cx, float cy, float r) => (x - cx) * (x - cx) + (y - cy) * (y - cy) < r * r;

        static bool Line(float x, float y, float ax, float ay, float bx, float by, float w)
        {
            float vx = bx - ax, vy = by - ay;
            float t = Mathf.Clamp01(((x - ax) * vx + (y - ay) * vy) / (vx * vx + vy * vy));
            float dx = ax + vx * t - x, dy = ay + vy * t - y;
            return dx * dx + dy * dy < w * w;
        }

        // ================================================================
        // 音（コードで作る）
        // ================================================================

        const int Rate = 44100;

        void MakeSounds()
        {
            var go = new GameObject("SlotAudio");
            go.transform.SetParent(transform, false);
            audioSource = go.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;

            sfxSpin = Tone("SlotSpin", 0.25f, t => Sq(t, 520f + 400f * t * 4f) * (1f - t * 4f) * 0.25f);
            sfxStop = Tone("SlotStop", 0.08f, t => Noise() * Mathf.Exp(-t * 60f) * 0.6f + Sq(t, 180f) * Mathf.Exp(-t * 40f) * 0.3f);
            sfxWin = Tone("SlotWin", 0.6f, t => Sq(t, Note(t, 0.1f, 523f, 659f, 784f, 1047f, 1047f, 1047f)) * 0.22f * (1f - t / 0.6f));
            sfxBig = Tone("SlotBig", 1.4f, t => (Sq(t, Note(t, 0.1f, 523f, 659f, 784f, 1047f, 784f, 1047f, 1319f, 1319f, 1568f, 1568f, 1568f, 1568f, 1568f, 1568f)) * 0.2f
                                                 + Sin(t, 261f) * 0.15f) * Mathf.Clamp01((1.4f - t) * 3f));
            sfxPop = Tone("SlotPop", 0.35f, t => (Noise() * 0.7f + Sin(t, 90f * (1f - t))) * Mathf.Exp(-t * 12f) * 0.7f);
            sfxBell = Tone("SlotBell", 1.0f, t => (Sin(t, 1320f) * 0.4f + Sin(t, 1980f) * 0.25f + Sin(t, 2640f) * 0.12f) * Mathf.Exp(-t * 4f) * 0.6f);
            sfxThud = Tone("SlotThud", 0.25f, t => Sin(t, 80f) * Mathf.Exp(-t * 18f) * 0.8f);
            sfxScratch = Tone("SlotScratch", 0.5f, t => Noise() * (0.5f + 0.5f * Mathf.Sin(t * 60f)) * Mathf.Exp(-t * 3f) * 0.35f);
        }

        void Play(AudioClip clip, float vol, float pitch = 1f)
        {
            if (audioSource == null || clip == null) return;
            audioSource.pitch = pitch;
            audioSource.PlayOneShot(clip, vol * volume);
        }

        static System.Random noiseRng = new System.Random(7);
        static float Noise() => (float)(noiseRng.NextDouble() * 2.0 - 1.0);
        static float Sin(float t, float f) => Mathf.Sin(2f * Mathf.PI * f * t);
        static float Sq(float t, float f) => Mathf.Sin(2f * Mathf.PI * f * t) >= 0f ? 1f : -1f;
        static float Note(float t, float step, params float[] notes) => notes[Mathf.Clamp((int)(t / step), 0, notes.Length - 1)];

        static AudioClip Tone(string name, float seconds, System.Func<float, float> f)
        {
            int n = Mathf.Max(1, (int)(Rate * seconds));
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(f(i / (float)Rate), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
