using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MedalPusher
{
    /// <summary>
    /// 盤面の意味のある物にマウスカーソルを合わせると、その物のまわりに白い輪郭を出し、説明の枠を出す。
    /// 説明の枠は、ショップ・レコード棚と同じ絵（Resources/ShopTooltipFrame.png）を使う。
    ///
    /// 説明を出す物（エリア1。見つけた物に HoverInfo を自動で付ける）
    ///   メダル・レアメダル／発射口（左・右）／中央の穴・端の穴（手前のふちの帯）
    ///   グルーヴゲージ／レコーダー（ターンテーブル）／スピーカー／モニター
    ///   ボス戦：ミュートブロック／南京錠（投入ストップ）／ボスの手／ボスの顔／お邪魔の予告（赤い影）
    ///   ほかの物にも、HoverInfo を付けて文を書けば出る
    ///
    /// しくみ
    /// ・カーソルの位置から盤面へ線をのばし、いちばん手前で当たった物を選ぶ（物の見た目をまとめた箱で調べる）。
    ///   画面のゆがみ（LensDistortion）も計算に入れる
    /// ・輪郭：選んだ物の形を白で描いた絵を作り、そのまわり1マスを白くぬる。ゲーム画面と同じ粗さ（ArcadeCamera の
    ///   Pixel Height）で作るので、ドットの輪郭になる。モニターやボスより上に重ねる
    /// ・説明の枠は、カーソルの右下に出す（画面からはみ出す時は左・上に出す）
    /// ・ショップの間、レコード棚などの UI の上にカーソルがある時、マウスを押している間は出さない
    /// ・Blockers に何か入っている間も出さない（例：射的のレコードで狙っている間）
    /// ・MedalPusherGame は変えない。シーンに置かなくても、MedalPusherGame があれば再生時に自動で付く
    /// </summary>
    [DisallowMultipleComponent]
    public class HoverTooltip : MonoBehaviour
    {
        [Tooltip("つなぐ相手（空なら自動で探す）")]
        public MedalPusherGame game;
        [Tooltip("説明を出す")]
        public bool showTooltip = true;
        [Tooltip("白い輪郭を出す")]
        public bool showOutline = true;
        [Tooltip("輪郭の色")]
        public Color outlineColor = Color.white;
        [Tooltip("輪郭を作る縦のマス数（0 ならゲーム画面と同じ粗さ）")]
        [Range(0, 1080)] public int outlinePixelHeight = 0;
        [Tooltip("説明の枠の横はば（1920×1080 の画面のつもりの大きさ）")]
        [Range(250f, 800f)] public float tooltipWidth = 440f;
        [Tooltip("カーソルを合わせてから、説明を出すまでの秒数")]
        [Range(0f, 1f)] public float showDelay = 0.08f;

        /// <summary>ここに入っている物があれば、説明と輪郭を出さない（演出がマウスを使う間に入れる）</summary>
        public static readonly HashSet<object> Blockers = new HashSet<object>();

        // ---- 色 ----
        static readonly Color SubColor = new Color(0.72f, 0.75f, 0.85f);
        static readonly Color BodyColor = new Color(0.92f, 0.92f, 0.95f);
        static readonly Color StatusColor = new Color(0.78f, 1f, 0.35f);
        static readonly Color WarningColor = new Color(1f, 0.42f, 0.42f);

        // ---- 選んでいる物 ----
        HoverInfo hovered;
        float hoverTime;
        float scanTimer;

        // ---- 説明の枠 ----
        Canvas canvas;
        RectTransform canvasRect, tooltip;
        Text tipName, tipSub, tipBody, tipStatus;
        Font font;

        // ---- 輪郭 ----
        GameObject outlineCanvas;
        RawImage outlineImage;
        RenderTexture maskTexture, edgeTexture, lensTexture;
        Material maskMaterial, edgeMaterial, lensMaterial;
        CommandBuffer command;
        Mesh quadMesh;

        // ---- ボスの物を受け取る入れ物 ----
        readonly List<(GameObject go, BossBattle.HoverKind kind, Renderer[] extra)> bossTargets = new List<(GameObject, BossBattle.HoverKind, Renderer[])>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoAttach()
        {
            if (FindFirstObjectByType<HoverTooltip>() != null) return;
            var g = FindFirstObjectByType<MedalPusherGame>();
            if (g != null) g.gameObject.AddComponent<HoverTooltip>();
        }

        void Start()
        {
            if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
            if (game == null)
            {
                enabled = false;
                return;
            }
            font = Resources.Load<Font>(MonitorDisplay.FontFolder + "/Body");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildTooltip();
        }

        void OnDestroy()
        {
            if (maskTexture != null) maskTexture.Release();
            if (edgeTexture != null) edgeTexture.Release();
            if (lensTexture != null) lensTexture.Release();
            if (command != null) command.Release();
        }

        void Update()
        {
            if (game == null) return;
            scanTimer -= Time.unscaledDeltaTime;
            if (scanTimer <= 0f)
            {
                scanTimer = 0.5f;
                ScanTargets();
            }

            var pick = Allowed() ? Pick() : null;
            if (pick != hovered)
            {
                hovered = pick;
                hoverTime = 0f;
            }
            else if (hovered != null) hoverTime += Time.unscaledDeltaTime;

            bool show = hovered != null && hoverTime >= showDelay;
            UpdateTooltip(show);
        }

        void LateUpdate()
        {
            bool show = showOutline && hovered != null && hoverTime >= showDelay;
            UpdateOutline(show);
        }

        /// <summary>説明を出してよい時か。</summary>
        bool Allowed()
        {
            if (!showTooltip && !showOutline) return false;
            if (Blockers.Count > 0) return false;                                               // 演出がマウスを使っている
            var s = game.State;
            if (s == GameState.Shop || s == GameState.Finished) return false;                 // ショップはモニターの説明がある
            if (InputCompat.MouseHeld() || InputCompat.MouseRightHeld()) return false;         // 発射している間はじゃましない
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;   // UI の上
            var m = InputCompat.MousePosition();
            if (m.x < 0f || m.y < 0f || m.x > Screen.width || m.y > Screen.height) return false;
            return true;
        }

        // ================================================================
        // 説明を出す物を探して、HoverInfo を付ける
        // ================================================================

        void ScanTargets()
        {
            var rules = game.rules;

            // メダル（普通・レア）
            foreach (var medal in FindObjectsByType<Medal>(FindObjectsSortMode.None))
            {
                if (medal.GetComponent<HoverInfo>() != null) continue;
                var info = medal.gameObject.AddComponent<HoverInfo>();
                var m = medal;
                info.refresh = h =>
                {
                    if (m.IsRare)
                    {
                        float mult = rules.rarePointMultiplier;
                        h.title = "レアメダル";
                        h.sub = "盤面のメダル（レア）";
                        h.body = $"落とすと、ポイントが {mult:0.#} 倍になる。\n中央の穴に落とすと {Mathf.RoundToInt(rules.centerPoints * mult)}pt。";
                    }
                    else
                    {
                        h.title = "メダル";
                        h.sub = "盤面のメダル";
                        h.body = $"プッシャーで押し出して、手前の穴に落とそう。\n中央の穴：{rules.centerPoints}pt ＋ メダル {rules.centerMedalReturn} 枚が戻る\n端の穴：{rules.sidePoints}pt";
                    }
                    h.status = "";
                };
            }

            // 発射口（左・右）
            for (int side = -1; side <= 1; side += 2)
            {
                var go = GameObject.Find(side < 0 ? "Launcher_L" : "Launcher_R");
                if (go == null) continue;
                int sd = side;
                var info = Ensure(go, sd < 0 ? "発射口（左）" : "発射口（右）", "メダルの投入口",
                    (sd < 0 ? "左クリック・A・← で" : "右クリック・D・→ で") + "、メダルを1枚投入する。長押しで続けて投入。Space で左右交互。\n1ラウンドに投入できる数には上限がある。");
                info.refresh = h =>
                {
                    float stop = BossBattle.Instance != null ? BossBattle.Instance.InsertStopRemaining : 0f;
                    if (stop > 0f)
                    {
                        h.status = $"投入ストップ中（あと {stop:0.0} 秒）";
                        h.statusWarning = true;
                    }
                    else
                    {
                        h.status = $"このラウンド {game.InsertedThisRound} / {game.InsertLimit}　所持メダル {game.OwnedMedals}";
                        h.statusWarning = !game.HasInsertsLeft;
                    }
                };
            }

            // 中央の穴・端の穴（手前のふちの帯）
            var marker = GameObject.Find("SideHoleMarker_Visual");
            if (marker != null)
                foreach (Transform child in marker.transform)
                {
                    if (child.name == "SideHoleMarker_CenterBar")
                    {
                        var info = Ensure(child.gameObject, "中央の穴", "メダルの落とし口", "");
                        info.refresh = h =>
                            h.body = $"ここに落ちたメダルは {rules.centerPoints}pt。メダルが {rules.centerMedalReturn} 枚戻ってくる。\nレコードの効果で、ポイントが増えることもある。";
                    }
                    else if (child.name == "SideHoleMarker_SideBar")
                    {
                        var info = Ensure(child.gameObject, "端の穴", "メダルの落とし口", "");
                        info.refresh = h =>
                            h.body = $"ここに落ちたメダルは {rules.sidePoints}pt。" + (rules.sideMedalReturn > 0 ? $"メダルが {rules.sideMedalReturn} 枚戻ってくる。" : "メダルは戻らない。")
                                   + "\nなるべく中央の穴に落とそう。";
                    }
                }

            // グルーヴゲージ
            var gauge = FindFirstObjectByType<GrooveGauge>();
            if (gauge != null)
            {
                var info = Ensure(gauge.gameObject, "グルーヴゲージ", "レコードの強化",
                    "メダルが穴に落ちるとたまる。\nたまったゲージを使って、Q キーで流しているレコードの効果を発動（強化）できる。");
                info.refresh = h =>
                {
                    var fx = FindEffects();
                    if (fx == null) { h.status = ""; return; }
                    if (fx.IsActivateLocked)
                    {
                        h.status = "× " + (string.IsNullOrEmpty(fx.ActivateLockReason) ? "今は発動できない" : fx.ActivateLockReason);
                        h.statusWarning = true;
                    }
                    else if (fx.IsActive)
                    {
                        h.status = $"発動中：{fx.Current.name}";
                        h.statusWarning = false;
                    }
                    else
                    {
                        string cost = fx.Loaded != null ? $"　発動に {fx.GetGaugeCost(fx.Loaded):0}" : "";
                        h.status = $"ゲージ {fx.Gauge:0} / {fx.gaugeMax:0}{cost}";
                        h.statusWarning = false;
                    }
                };
            }

            // レコーダー（ターンテーブル）
            var turntable = FindFirstObjectByType<TurntableController>();
            if (turntable != null)
            {
                var info = Ensure(turntable.gameObject, "レコーダー", "レコード",
                    "流しているレコードの曲が鳴る。\nE キー（右のレコード棚）でレコードを選んで流す。流すだけでは効果は出ず、Q キーで発動すると効果が出る。");
                info.refresh = h =>
                {
                    var fx = FindEffects();
                    h.status = fx != null && fx.Loaded != null ? $"流している：{fx.Loaded.name}" : "レコードを流していない";
                    h.statusWarning = false;
                };
            }

            // スピーカー
            var speaker = FindFirstObjectByType<SpeakerUnit>();
            if (speaker != null)
                Ensure(speaker.gameObject, "スピーカー", "演出",
                    "流している曲に合わせてゆれる。効果を発動している間は、レコードの色に光る。\n（ゲームの点数には関係しない）");

            // モニター
            var screen = GameObject.Find("MonitorScreen");
            if (screen != null)
                Ensure(screen, "モニター", "情報の表示",
                    "ラウンド、ノルマ（ボス戦ではボスの HP）、流しているレコードなどを表示する。\nボス戦では、ここからボスが出てくる。");

            // ボス戦の物
            var boss = BossBattle.Instance;
            if (boss != null)
            {
                boss.CollectHoverTargets(bossTargets);
                foreach (var (go, kind, extra) in bossTargets)
                {
                    if (go == null || go.GetComponent<HoverInfo>() != null) continue;
                    var info = go.AddComponent<HoverInfo>();
                    if (extra != null) info.extraRenderers = new List<Renderer>(extra);
                    SetupBoss(info, kind, boss);
                }
            }
        }

        void SetupBoss(HoverInfo info, BossBattle.HoverKind kind, BossBattle boss)
        {
            switch (kind)
            {
                case BossBattle.HoverKind.MuteBlock:
                    info.title = "ミュートブロック";
                    info.sub = "ボスのお邪魔";
                    info.body = "盤面にある間は、Q キーで強化（発動）できない。発動中の効果も止められてしまう。\nプッシャーで押し出して、穴に落とすと消える。";
                    info.refresh = h =>
                    {
                        h.status = $"盤面に {boss.MuteBlockCount} 個";
                        h.statusWarning = true;
                    };
                    break;
                case BossBattle.HoverKind.Lock:
                    info.title = "南京錠";
                    info.sub = "ボスのお邪魔：投入ストップ";
                    info.body = "鍵がかかっている間は、メダルを投入できない。\n時間がたつと外れる。";
                    info.refresh = h =>
                    {
                        float t = boss.InsertStopRemaining;
                        h.status = t > 0f ? $"あと {t:0.0} 秒" : "";
                        h.statusWarning = true;
                    };
                    break;
                case BossBattle.HoverKind.Hand:
                    info.refresh = h =>
                    {
                        h.title = $"{boss.BossName}の手";
                        h.sub = "ボス";
                        h.body = "モニターから出てきて、ミュートブロックを置いたり、発射口に鍵をかけたりする。";
                    };
                    break;
                case BossBattle.HoverKind.Head:
                    info.refresh = h =>
                    {
                        h.title = boss.BossName;
                        h.sub = "ボス";
                        h.body = "ノルマのポイントをためると倒せる。\n攻撃ゲージがたまると、モニターから出てきてお邪魔をする。";
                        h.status = $"HP {game.BossHp} / {game.CurrentQuota}";
                        h.statusWarning = false;
                    };
                    break;
                case BossBattle.HoverKind.Shadow:
                    info.title = "お邪魔の予告";
                    info.sub = "ボスのお邪魔";
                    info.body = "まもなく、ここにミュートブロックが置かれる。";
                    break;
            }
        }

        /// <summary>HoverInfo が無ければ付けて、文を入れる（ある時は、そのまま返す）。</summary>
        static HoverInfo Ensure(GameObject go, string title, string sub, string body)
        {
            var info = go.GetComponent<HoverInfo>();
            if (info != null) return info;
            info = go.AddComponent<HoverInfo>();
            info.title = title;
            info.sub = sub;
            info.body = body;
            return info;
        }

        RecordEffectSystem effects;
        RecordEffectSystem FindEffects()
        {
            if (effects == null) effects = FindFirstObjectByType<RecordEffectSystem>();
            return effects;
        }

        // ================================================================
        // カーソルの下の物を選ぶ
        // ================================================================

        HoverInfo Pick()
        {
            var main = Camera.main;
            if (main == null) return null;
            // 画面のゆがみの分を戻して、カメラの映像の上の位置にする
            Vector2 mouse = InputCompat.MousePosition();
            var uv = new Vector2(mouse.x / Mathf.Max(1, Screen.width), mouse.y / Mathf.Max(1, Screen.height));
            var lens = main.GetComponent<LensDistortion>();
            if (lens != null && lens.isActiveAndEnabled && lens.strength > 0f)
            {
                float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
                var d = uv - new Vector2(0.5f, 0.5f);
                d.x *= aspect;
                float r2 = Vector2.Dot(d, d);
                float cornerR2 = 0.25f * (aspect * aspect + 1f);
                d *= (1f + lens.strength * r2) / (1f + lens.strength * cornerR2);
                d.x /= aspect;
                uv = d + new Vector2(0.5f, 0.5f);
            }
            var ray = main.ViewportPointToRay(new Vector3(uv.x, uv.y, 0f));

            HoverInfo best = null;
            float bestDist = float.MaxValue;
            float bestSize = float.MaxValue;
            foreach (var info in HoverInfo.All)
            {
                if (info == null || !info.isActiveAndEnabled) continue;
                if (!info.TryGetBounds(out var b)) continue;
                if (!b.IntersectRay(ray, out float dist)) continue;
                float size = b.size.sqrMagnitude;
                // いちばん手前の物。ほぼ同じ距離なら、小さい物（中に入っている物）を選ぶ
                if (dist < bestDist - 0.01f || (Mathf.Abs(dist - bestDist) <= 0.01f && size < bestSize))
                {
                    best = info;
                    bestDist = dist;
                    bestSize = size;
                }
            }
            return best;
        }

        // ================================================================
        // 説明の枠
        // ================================================================

        void BuildTooltip()
        {
            var go = new GameObject("HoverTooltip");
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 45;   // レコード棚（40）より上、クリア後の帯（60）より下
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRect = (RectTransform)go.transform;

            tooltip = NewRect("Tooltip", canvasRect, new Vector2(tooltipWidth, 200f));
            tooltip.anchorMin = tooltip.anchorMax = new Vector2(0.5f, 0.5f);
            tooltip.pivot = new Vector2(0f, 1f);   // 左上をカーソルの右下に合わせる
            var bg = tooltip.gameObject.AddComponent<Image>();
            bg.raycastTarget = false;
            var tex = Resources.Load<Texture2D>("ShopTooltipFrame");
            if (tex != null)
            {
                tex.filterMode = FilterMode.Point;
                bg.sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(18f, 18f, 18f, 18f));   // 四隅と縁を伸ばさない（ショップと同じ）
                bg.type = Image.Type.Sliced;
            }
            else
            {
                bg.color = new Color(0.11f, 0.13f, 0.20f, 0.97f);
            }
            tipName = NewText("Name", 26, Color.white, FontStyle.Bold);
            tipSub = NewText("Sub", 16, SubColor, FontStyle.Normal);
            tipBody = NewText("Body", 19, BodyColor, FontStyle.Normal);
            tipStatus = NewText("Status", 17, StatusColor, FontStyle.Bold);
            tooltip.gameObject.SetActive(false);
        }

        RectTransform NewRect(string name, RectTransform parent, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = size;
            return rt;
        }

        Text NewText(string name, int size, Color color, FontStyle style)
        {
            var rt = NewRect(name, tooltip, new Vector2(tooltipWidth - 50f, 30f));
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAnchor.UpperLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            return t;
        }

        void UpdateTooltip(bool show)
        {
            if (tooltip == null) return;
            if (!show || !showTooltip)
            {
                if (tooltip.gameObject.activeSelf) tooltip.gameObject.SetActive(false);
                return;
            }
            hovered.refresh?.Invoke(hovered);

            // 中身をならべて、高さを決める
            float w = tooltipWidth - 50f;
            float y = -22f;
            Place(tipName, hovered.title, w, ref y, 2f);
            Place(tipSub, hovered.sub, w, ref y, 10f);
            Place(tipBody, hovered.body, w, ref y, 8f);
            tipStatus.color = hovered.statusWarning ? WarningColor : StatusColor;
            Place(tipStatus, hovered.status, w, ref y, 0f);
            float height = -y + 20f;
            tooltip.sizeDelta = new Vector2(tooltipWidth, height);

            // カーソルの右下に出す。はみ出す時は左・上に
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, InputCompat.MousePosition(), null, out var local);
            var half = canvasRect.rect.size * 0.5f;
            float x = local.x + 26f;
            float top = local.y - 26f;
            if (x + tooltipWidth > half.x - 8f) x = local.x - 18f - tooltipWidth;
            if (top - height < -half.y + 8f) top = local.y + 18f + height;
            x = Mathf.Clamp(x, -half.x + 8f, half.x - tooltipWidth - 8f);
            top = Mathf.Clamp(top, -half.y + height + 8f, half.y - 8f);
            tooltip.anchoredPosition = new Vector2(x, top);
            if (!tooltip.gameObject.activeSelf) tooltip.gameObject.SetActive(true);
        }

        /// <summary>文を入れて、上から順にならべる（空の文は出さない）。</summary>
        static void Place(Text t, string text, float width, ref float y, float gap)
        {
            bool on = !string.IsNullOrEmpty(text);
            if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
            if (!on) return;
            t.text = text;
            var rt = t.rectTransform;
            rt.anchoredPosition = new Vector2(25f, y);
            rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);   // 先にはばを決めてから、折り返した高さをはかる
            float h = Mathf.Ceil(t.preferredHeight);
            rt.sizeDelta = new Vector2(width, h);
            y -= h + gap;
        }

        // ================================================================
        // 白い輪郭
        // ================================================================

        void UpdateOutline(bool show)
        {
            var main = Camera.main;
            if (!show || main == null || hovered == null || !EnsureOutline(main))
            {
                if (outlineCanvas != null && outlineCanvas.activeSelf) outlineCanvas.SetActive(false);
                return;
            }

            // ゲーム画面と同じ粗さ
            var arcade = main.GetComponent<ArcadeCamera>();
            int h = outlinePixelHeight > 0 ? outlinePixelHeight
                  : arcade != null && arcade.isActiveAndEnabled && arcade.effectEnabled ? arcade.pixelHeight : Screen.height;
            h = Mathf.Clamp(h, 32, 2160);
            int w = Mathf.Max(1, Mathf.RoundToInt(h * (float)Screen.width / Mathf.Max(1, Screen.height)));
            maskTexture = Ensure(maskTexture, w, h, "HoverMask");
            edgeTexture = Ensure(edgeTexture, w, h, "HoverEdge");

            // 1. 選んだ物の形を、白で描く
            command.Clear();
            command.SetRenderTarget(maskTexture);
            command.ClearRenderTarget(true, true, Color.clear);
            // コマンドバッファの SetViewProjectionMatrices は、グラフィックスの種類に合わせた上下の向きを自分で直すので、
            // カメラの行列をそのまま渡す（GL.GetGPUProjectionMatrix で直すと二重になって、上下さかさまにずれる）
            command.SetViewProjectionMatrices(main.worldToCameraMatrix, main.projectionMatrix);
            foreach (var r in hovered.Renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                int subs = Mathf.Max(1, r.sharedMaterials.Length);
                for (int i = 0; i < subs; i++) command.DrawRenderer(r, maskMaterial, i, 0);
            }
            if (hovered.rectTarget != null && hovered.rectTarget.gameObject.activeInHierarchy)
            {
                // UI の四角
                var rt = hovered.rectTarget;
                var size = rt.rect.size;
                var center = rt.TransformPoint(rt.rect.center);
                var matrix = Matrix4x4.TRS(center, rt.rotation, Vector3.Scale(new Vector3(size.x, size.y, 1f), rt.lossyScale));
                command.DrawMesh(QuadMesh(), matrix, maskMaterial, 0, 0);
            }
            Graphics.ExecuteCommandBuffer(command);

            // 2. 形のまわり1マスを白くぬる
            edgeMaterial.SetColor("_Color", outlineColor);
            Graphics.Blit(maskTexture, edgeTexture, edgeMaterial);

            // 3. メインカメラと同じゆがみをかける
            Texture shown = edgeTexture;
            var lens = main.GetComponent<LensDistortion>();
            if (lens != null && lens.isActiveAndEnabled && lens.strength > 0f && lensMaterial != null)
            {
                lensTexture = Ensure(lensTexture, w, h, "HoverLens");
                lensMaterial.SetFloat("_Strength", lens.strength);
                lensMaterial.SetFloat("_Aspect", (float)w / Mathf.Max(1, h));
                Graphics.Blit(edgeTexture, lensTexture, lensMaterial);
                shown = lensTexture;
            }
            outlineImage.texture = shown;
            if (!outlineCanvas.activeSelf) outlineCanvas.SetActive(true);
        }

        bool EnsureOutline(Camera main)
        {
            if (outlineCanvas != null) return maskMaterial != null && edgeMaterial != null;
            var maskShader = Shader.Find("Hidden/MedalPusher/HoverMask");
            var edgeShader = Shader.Find("Hidden/MedalPusher/HoverOutline");
            if (maskShader != null) maskMaterial = new Material(maskShader) { hideFlags = HideFlags.HideAndDontSave };
            if (edgeShader != null) edgeMaterial = new Material(edgeShader) { hideFlags = HideFlags.HideAndDontSave };
            var lensShader = Shader.Find("Hidden/MedalPusher/LensDistortion");
            if (lensShader != null) lensMaterial = new Material(lensShader) { hideFlags = HideFlags.HideAndDontSave };
            command = new CommandBuffer { name = "HoverOutlineMask" };

            outlineCanvas = new GameObject("HoverOutline");
            outlineCanvas.transform.SetParent(transform, false);
            var c = outlineCanvas.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            var arcade = main.GetComponent<ArcadeCamera>();
            c.sortingOrder = arcade != null ? arcade.sortingOrder + 3 : 2;   // モニター（+1）とボス（+2）の上
            var imageGo = new GameObject("Outline");
            imageGo.transform.SetParent(outlineCanvas.transform, false);
            outlineImage = imageGo.AddComponent<RawImage>();
            outlineImage.raycastTarget = false;
            var premultiplied = Shader.Find("Hidden/MedalPusher/OverlayPremultiplied");
            if (premultiplied != null) outlineImage.material = new Material(premultiplied);
            var rt = outlineImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            outlineCanvas.SetActive(false);
            if (maskMaterial == null || edgeMaterial == null)
                Debug.LogWarning("HoverTooltip: 輪郭のシェーダー（Resources の HoverMask / HoverOutline）が見つかりません。輪郭は出しません。", this);
            return maskMaterial != null && edgeMaterial != null;
        }

        static RenderTexture Ensure(RenderTexture rt, int w, int h, string name)
        {
            if (rt != null && rt.width == w && rt.height == h) return rt;
            if (rt != null) rt.Release();
            rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32)
            {
                name = name,
                filterMode = FilterMode.Point,   // 引き伸ばしてもぼかさない（ドットの輪郭）
                wrapMode = TextureWrapMode.Clamp
            };
            rt.Create();
            return rt;
        }

        Mesh QuadMesh()
        {
            if (quadMesh != null) return quadMesh;
            quadMesh = new Mesh { name = "HoverQuad" };
            quadMesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            quadMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quadMesh.RecalculateBounds();
            return quadMesh;
        }
    }
}
