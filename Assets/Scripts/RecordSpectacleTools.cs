using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedalPusher
{
    /// <summary>
    /// レコードのスクリプトの道具（大技・変化メダル・補充）。N001〜N010 の演出で使う。
    ///
    /// ・変化メダル：MarkMedal で、メダルをレコードの色のメダルに変える（光ってはねる）
    /// ・補充：RainFromSky（上から降らせる）・LaunchArc（弧を描いて飛ばす）
    /// ・押し出した分を数える：WatchPushed で数え始め、NoteDropForPush を OnDrop で呼び、TakePushed で取り出して降らせる
    /// ・大技の道具（どれも本物のメダルを使う。最後は必ず放して、盤面のメダルになる）
    ///   ColumnRoutine    ：メダルの柱がらせんに積み上がって回り、崩れて散らばる
    ///   TornadoRoutine   ：近くのメダルをらせんに巻き上げて回し、最後にばらまく（足すメダルも混ぜる）
    ///   SculptureRoutine ：メダルが飛んできて形（ビル・機関車など）に組み上がり、動いてから崩れる
    ///   ExplodeAt        ：爆発。まわりをはじき、煙を出し、画面を揺らし、メダルを飛び散らせる
    ///   Beam             ：上空から光の線（レーザー・光の線）を一瞬出す
    /// ・SparkAt（火花）・SmokeAt（煙）で、大技の粒を出せる
    /// ・盤面のメダルが MedalSoftCap を超えている時は、Budget で降らせる枚数を半分にする（重くならないように）
    /// ・持ち上げている（物理を止めている）メダルは、リセットの時と ReleaseAllHeld で必ず放す
    /// </summary>
    public abstract partial class RecordScriptBase
    {
        /// <summary>盤面のメダルがこの数を超えたら、降らせる枚数を半分にする</summary>
        public static int MedalSoftCap = 260;

        readonly List<Medal> heldByTools = new List<Medal>();
        bool toolsHooked;
        Transform fxRoot;
        ParticleSystem toolSparks, toolSmoke;
        static Material beamMaterial;
        int pushedCount;
        float pushWatchUntil = -1f;

        // ================================================================
        // 準備・後かたづけ
        // ================================================================

        void HookTools()
        {
            if (toolsHooked || game == null) return;
            toolsHooked = true;
            game.GameReset += OnToolsReset;   // リセットでメダルが消える前に放す（使い回しのメダルが止まったままにならないように）
        }

        /// <summary>リセット：動いている大技を止めて、持ち上げているメダルを放し、光の線を消す</summary>
        void OnToolsReset()
        {
            StopAllCoroutines();
            ReleaseAllHeld();
            if (fxRoot != null)
                for (int i = fxRoot.childCount - 1; i >= 0; i--)
                {
                    var c = fxRoot.GetChild(i);
                    if (c.name == "Beam") Destroy(c.gameObject);
                }
        }

        /// <summary>大技の粒の入れ物（Board とは別。発動が終わっても大技の終わりまで見える）</summary>
        protected Transform FxRoot
        {
            get
            {
                if (fxRoot == null)
                {
                    HookTools();
                    fxRoot = new GameObject(GetType().Name + "_Fx").transform;
                    toolSparks = Particles("Sparks", fxRoot, Vector3.up, D * 4f, D * 0.12f, 0.7f, 0.5f, 60f);
                    toolSmoke = Particles("Smoke", fxRoot, Vector3.up, D * 2.5f, D * 2.2f, 1.6f, -0.05f, 80f);
                    var main = toolSmoke.main;
                    main.maxParticles = 300;
                }
                return fxRoot;
            }
        }

        /// <summary>持ち上げている（物理を止めている）メダルを全部放す</summary>
        protected void ReleaseAllHeld()
        {
            foreach (var m in heldByTools)
                if (m != null && m.Body != null) m.Body.isKinematic = false;
            heldByTools.Clear();
        }

        /// <summary>メダルの物理を止めて、道具が動かす（必ず Release で放す）</summary>
        protected Medal Hold(Medal m)
        {
            if (m == null || m.Body == null) return m;
            HookTools();
            m.Body.isKinematic = true;
            if (!heldByTools.Contains(m)) heldByTools.Add(m);
            return m;
        }

        /// <summary>止めていたメダルを放して、勢いを付ける</summary>
        protected void Release(Medal m, Vector3 velocity)
        {
            if (m == null) return;
            heldByTools.Remove(m);
            if (m.Body == null || !m.IsActiveInPool) return;
            m.Body.isKinematic = false;
            m.Body.velocity = velocity;
            m.Body.angularVelocity = UnityEngine.Random.insideUnitSphere * 6f;
            m.Body.WakeUp();
        }

        /// <summary>盤面のメダルの数に合わせた枚数（多すぎる時は半分）</summary>
        protected int Budget(int count) => BoardMedals.Count > MedalSoftCap ? Mathf.Max(1, count / 2) : count;

        /// <summary>メダルを出す（使い回しのメダルが止まったままでも、必ず動くようにする）</summary>
        protected Medal SpawnFree(Vector3 position, Vector3 velocity)
        {
            var m = SpawnMedal(position, velocity);
            if (m != null && m.Body != null && m.Body.isKinematic)
            {
                m.Body.isKinematic = false;
                m.Body.velocity = velocity;
            }
            return m;
        }

        // ================================================================
        // 押し出した分を数える
        // ================================================================

        /// <summary>これから seconds 秒の間に穴に落ちたメダルを「押し出した分」として数える</summary>
        protected void WatchPushed(float seconds) => pushWatchUntil = Mathf.Max(pushWatchUntil, Time.time + seconds);

        /// <summary>OnDrop から呼ぶ（数えている間なら1枚足す）</summary>
        protected void NoteDropForPush()
        {
            if (Time.time <= pushWatchUntil) pushedCount++;
        }

        /// <summary>数えた枚数を取り出す（cap まで）。数え直しになる</summary>
        protected int TakePushed(int cap)
        {
            int n = Mathf.Min(cap, pushedCount);
            pushedCount = 0;
            return n;
        }

        protected int PushedCount => pushedCount;

        // ================================================================
        // 場所
        // ================================================================

        /// <summary>その場所の床の高さ（メダル・動く物・レコードの物は数えない）</summary>
        protected float FloorY(float x, float z)
        {
            var hits = Physics.RaycastAll(new Vector3(x, Layout.wallHeight + 5f, z), Vector3.down, Layout.wallHeight + 20f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits)
            {
                if (h.collider.GetComponentInParent<Medal>() != null) continue;
                var rb = h.collider.attachedRigidbody;
                if (rb != null && !rb.isKinematic) continue;
                if (Board != null && h.collider.transform.IsChildOf(Board)) continue;
                if (h.point.y > best) best = h.point.y;
            }
            return float.IsNegativeInfinity(best) ? 0f : best;
        }

        /// <summary>プッシャーの前の床のまん中あたり（大技の中心）</summary>
        protected Vector3 FloorCenter(float z01 = 0.5f)
        {
            float z = Mathf.Lerp(FrontZ, PusherFrontZ, z01);
            return new Vector3(0f, FloorY(0f, z), z);
        }

        /// <summary>上空（メダルを降らせる高さ）</summary>
        protected float SkyY => Layout.launcherHeight + D * 0.5f;

        /// <summary>
        /// マウスのカーソルが指している盤面の床の場所（高さ floorY の平らな面との交わり）。
        /// 画面のゆがみ（LensDistortion）の分を戻してから調べる。指していなければ false
        /// </summary>
        protected bool MouseOnFloor(float floorY, out Vector3 point)
        {
            point = Vector3.zero;
            var main = Camera.main;
            if (main == null) return false;
            Vector2 mouse = InputCompat.MousePosition();
            var uv = new Vector2(mouse.x / Mathf.Max(1, UnityEngine.Screen.width), mouse.y / Mathf.Max(1, UnityEngine.Screen.height));
            var lens = main.GetComponent<LensDistortion>();
            if (lens != null && lens.isActiveAndEnabled && lens.strength > 0f)
            {
                float aspect = (float)UnityEngine.Screen.width / Mathf.Max(1, UnityEngine.Screen.height);
                var d = uv - new Vector2(0.5f, 0.5f);
                d.x *= aspect;
                float r2 = Vector2.Dot(d, d);
                float cornerR2 = 0.25f * (aspect * aspect + 1f);
                d *= (1f + lens.strength * r2) / (1f + lens.strength * cornerR2);
                d.x /= aspect;
                uv = d + new Vector2(0.5f, 0.5f);
            }
            var ray = main.ViewportPointToRay(new Vector3(uv.x, uv.y, 0f));
            var plane = new Plane(Vector3.up, new Vector3(0f, floorY, 0f));
            if (!plane.Raycast(ray, out float dist)) return false;
            point = ray.GetPoint(dist);
            return true;
        }

        // ================================================================
        // 見た目：火花・煙・光の線
        // ================================================================

        protected void SparkAt(Vector3 position, Color color, int count)
        {
            _ = FxRoot;
            Emit(toolSparks, position, color, count);
        }

        protected void SmokeAt(Vector3 position, Color color, int count)
        {
            _ = FxRoot;
            Emit(toolSmoke, position, color, count);
        }

        /// <summary>光の線を from から to へ、seconds 秒だけ出す（太さ width。だんだん細く消える）</summary>
        protected void Beam(Vector3 from, Vector3 to, Color color, float seconds = 0.25f, float width = 0f)
        {
            if (width <= 0f) width = D * 0.18f;
            if (beamMaterial == null) beamMaterial = new Material(Shader.Find("Sprites/Default"));
            var go = new GameObject("Beam");
            go.transform.SetParent(FxRoot, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = beamMaterial;
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.useWorldSpace = true;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 2;
            StartCoroutine(BeamFade(lr, color, seconds, width));
        }

        IEnumerator BeamFade(LineRenderer lr, Color color, float seconds, float width)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                if (lr == null) yield break;
                float k = t / seconds;
                float w = width * (1f - k * k);
                lr.startWidth = w;
                lr.endWidth = w * 0.6f;
                var c = Color.Lerp(Color.white, color, Mathf.Clamp01(k * 3f));
                c.a = 1f - k;
                lr.startColor = c;
                lr.endColor = c;
                yield return null;
            }
            if (lr != null) Destroy(lr.gameObject);
        }

        // ================================================================
        // 変化メダル
        // ================================================================

        /// <summary>メダルをレコードの色のメダルに変える（ぱっと光って、小さくはねる）。glow は光る強さ（強すぎると白っぽく見える）</summary>
        protected void MarkMedal(Medal m, string kind, Color color, float hop = 1f, float glow = 1.2f)
        {
            if (m == null || !m.IsActiveInPool || m.Consumed) return;
            MedalMarks.Set(m, kind, color, glow);
            SparkAt(m.transform.position + Vector3.up * D * 0.2f, color, 10);
            if (hop > 0f && m.Body != null && !m.Body.isKinematic) Push(m, Vector3.up * D * 1.2f * hop);
        }

        /// <summary>盤面（プッシャーの前の床）から、まだ印の無いメダルを1枚えらぶ</summary>
        protected Medal PickPlainFloorMedal()
        {
            return PickMedal(m => !MedalMarks.Has(m) && m.Body != null && !m.Body.isKinematic
                                  && m.transform.position.z > FrontZ + D * 0.3f && m.transform.position.z < PusherFrontZ
                                  && m.transform.position.y < Layout.pusherHeight * 0.8f);
        }

        // ================================================================
        // 補充：上から降らせる・弧を描いて飛ばす
        // ================================================================

        /// <summary>
        /// count 枚を seconds 秒かけて、上空から降らせる。center のまわり（横 halfX・奥行き halfZ）にばらまく。
        /// onSpawn で、出したメダルに印を付けたりできる
        /// </summary>
        protected Coroutine RainFromSky(int count, float seconds, Vector3 center, float halfX, float halfZ, Action<Medal> onSpawn = null, float height = 0f)
            => StartCoroutine(RainRoutine(count, seconds, center, halfX, halfZ, onSpawn, height));

        IEnumerator RainRoutine(int count, float seconds, Vector3 center, float halfX, float halfZ, Action<Medal> onSpawn, float height)
        {
            float y = height > 0f ? height : SkyY;
            float step = count > 0 ? seconds / count : 0f;
            for (int i = 0; i < count; i++)
            {
                float x = Mathf.Clamp(center.x + UnityEngine.Random.Range(-halfX, halfX), -HalfW * 0.9f, HalfW * 0.9f);
                float z = Mathf.Clamp(center.z + UnityEngine.Random.Range(-halfZ, halfZ), FrontZ + D * 0.5f, PusherFrontZ - D * 0.3f);
                var m = SpawnFree(new Vector3(x, y + UnityEngine.Random.Range(0f, D), z), new Vector3(0f, -D * 2f, 0f));
                if (m != null) onSpawn?.Invoke(m);
                if (step > 0f) yield return new WaitForSeconds(step);
            }
        }

        /// <summary>from から target へ、flightTime 秒で弧を描いて落ちるようにメダルを1枚飛ばす</summary>
        protected Medal LaunchArc(Vector3 from, Vector3 target, float flightTime, Action<Medal> onSpawn = null)
        {
            float t = Mathf.Max(0.2f, flightTime);
            Vector3 g = Physics.gravity * GravityScale;
            Vector3 v = (target - from) / t - 0.5f * g * t;
            var m = SpawnFree(from, v);
            if (m != null) onSpawn?.Invoke(m);
            return m;
        }

        // ================================================================
        // 大技：メダルの柱
        // ================================================================

        /// <summary>
        /// メダルの柱：center から count 枚がらせんに積み上がり（buildSeconds 秒）、spinSpeed 度/秒で回りながら holdSeconds 秒立つ。
        /// まわりのメダル（gatherRadius 以内）を柱の方へ寄せる。最後に崩れて、外へ散らばる。
        /// onSpawn は積んだメダルに、onCollapse は崩れる瞬間に呼ぶ
        /// </summary>
        protected Coroutine ColumnRoutine(Vector3 center, int count, float buildSeconds, float holdSeconds, float spinSpeed,
                                          float gatherRadius, Action<Medal> onSpawn = null, Action onCollapse = null)
            => StartCoroutine(Column(center, count, buildSeconds, holdSeconds, spinSpeed, gatherRadius, onSpawn, onCollapse));

        IEnumerator Column(Vector3 center, int count, float buildSeconds, float holdSeconds, float spinSpeed,
                           float gatherRadius, Action<Medal> onSpawn, Action onCollapse)
        {
            var stack = new List<Medal>();
            var angles = new List<float>();
            float th = game.tuning.medalThickness * 1.05f;
            float radius = D * 0.35f;
            float spin = 0f;
            float step = count > 0 ? buildSeconds / count : 0f;
            float timer = 0f;
            int built = 0;
            var near = new List<Medal>();

            void Place(float s)
            {
                for (int i = 0; i < stack.Count; i++)
                {
                    var m = stack[i];
                    if (m == null || !m.IsActiveInPool) continue;
                    float a = (angles[i] + s) * Mathf.Deg2Rad;
                    var p = center + new Vector3(Mathf.Cos(a) * radius, th * (i + 0.5f), Mathf.Sin(a) * radius);
                    var r = Quaternion.Euler(0f, angles[i] + s, 0f);
                    m.transform.SetPositionAndRotation(p, r);
                    m.Body.position = p;
                    m.Body.rotation = r;
                }
            }

            float total = buildSeconds + holdSeconds;
            for (float t = 0f; t < total; t += Time.deltaTime)
            {
                spin += spinSpeed * Time.deltaTime;
                timer += Time.deltaTime;
                while (built < count && (timer >= step || step <= 0f))
                {
                    timer -= step;
                    var m = SpawnFree(center + Vector3.up * th * (built + 0.5f), Vector3.zero);
                    if (m == null) break;
                    Hold(m);
                    stack.Add(m);
                    angles.Add(built * 37f);
                    built++;
                    onSpawn?.Invoke(m);
                    if (built % 4 == 0) SparkAt(center + Vector3.up * th * built, Color.white, 4);
                }
                Place(spin);
                // まわりのメダルを柱の方へ寄せる
                if (gatherRadius > 0f && Mathf.FloorToInt(t * 10f) != Mathf.FloorToInt((t - Time.deltaTime) * 10f))
                {
                    MedalsNear(center, gatherRadius, near);
                    foreach (var m in near)
                    {
                        if (m.Body == null || m.Body.isKinematic) continue;
                        Vector3 d = center - m.transform.position;
                        d.y = 0f;
                        if (d.sqrMagnitude < D * D) continue;
                        Push(m, d.normalized * D * 0.25f);
                    }
                }
                yield return null;
            }

            // 崩れる：上の方ほど遠くへ
            onCollapse?.Invoke();
            SparkAt(center + Vector3.up * th * stack.Count * 0.5f, Color.white, 40);
            for (int i = 0; i < stack.Count; i++)
            {
                var m = stack[i];
                if (m == null) continue;
                float up = stack.Count > 1 ? i / (stack.Count - 1f) : 1f;
                float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (dir.z > 0f) dir.z *= 0.4f;   // 奥（プッシャーの方）へは飛ばしすぎない
                Release(m, dir * D * (1.5f + 5f * up) + Vector3.up * D * (1f + 3f * up));
            }
        }

        // ================================================================
        // 大技：竜巻
        // ================================================================

        /// <summary>
        /// 竜巻：center の radius 以内のメダルを maxCapture 枚まで巻き上げ、らせんに回す（seconds 秒）。
        /// その間に extraCount 枚を足して、いっしょに回す。最後に弾けて、全部ばらまく。
        /// onExtra は足したメダルに、onBurst は弾ける瞬間に呼ぶ（巻き上げた枚数を渡す）
        /// </summary>
        protected Coroutine TornadoRoutine(Vector3 center, float radius, int maxCapture, float seconds, int extraCount,
                                           Action<Medal> onExtra = null, Action<int> onBurst = null)
            => StartCoroutine(Tornado(center, radius, maxCapture, seconds, extraCount, onExtra, onBurst));

        IEnumerator Tornado(Vector3 center, float radius, int maxCapture, float seconds, int extraCount,
                            Action<Medal> onExtra, Action<int> onBurst)
        {
            var near = MedalsNear(center, radius);
            var caught = new List<Medal>();
            var phase = new List<float>();
            var heightK = new List<float>();
            var start = new List<Vector3>();
            int captured = 0;
            foreach (var m in near)
            {
                if (caught.Count >= maxCapture) break;
                if (m.Body == null || m.Body.isKinematic) continue;
                if (m.transform.position.y > Layout.pusherHeight * 0.8f) continue;   // 上の段は巻き込まない
                Hold(m);
                caught.Add(m);
                phase.Add(UnityEngine.Random.Range(0f, 360f));
                heightK.Add(UnityEngine.Random.value);
                start.Add(m.transform.position);
                captured++;
            }

            float extraTimer = 0f;
            float extraStep = extraCount > 0 ? seconds * 0.6f / extraCount : 0f;
            int extraMade = 0;
            float topH = D * 6f;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = t / seconds;
                // 足すメダル：竜巻の上から入ってくる
                extraTimer += Time.deltaTime;
                while (extraMade < extraCount && extraTimer >= extraStep && extraStep > 0f)
                {
                    extraTimer -= extraStep;
                    var m = SpawnFree(center + Vector3.up * topH, Vector3.zero);
                    if (m == null) break;
                    Hold(m);
                    caught.Add(m);
                    phase.Add(UnityEngine.Random.Range(0f, 360f));
                    heightK.Add(UnityEngine.Random.Range(0.6f, 1f));
                    start.Add(m.transform.position);
                    extraMade++;
                    onExtra?.Invoke(m);
                }
                float lift = Ease.OutCubic(Mathf.Clamp01(k * 2.5f));
                for (int i = 0; i < caught.Count; i++)
                {
                    var m = caught[i];
                    if (m == null || !m.IsActiveInPool) continue;
                    float h = topH * (0.1f + 0.9f * heightK[i]);
                    float r = D * (0.6f + 1.6f * heightK[i]);   // 上ほど広い
                    float a = (phase[i] + t * (420f - 200f * heightK[i])) * Mathf.Deg2Rad;
                    var orbit = center + new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r);
                    var p = Vector3.Lerp(start[i], orbit, lift);
                    var rot = Quaternion.Euler(70f, a * Mathf.Rad2Deg, t * 300f);
                    m.transform.SetPositionAndRotation(p, rot);
                    m.Body.position = p;
                    m.Body.rotation = rot;
                }
                yield return null;
            }

            onBurst?.Invoke(captured);
            SparkAt(center + Vector3.up * topH * 0.5f, Color.white, 50);
            foreach (var m in caught)
            {
                if (m == null) continue;
                Vector3 d = m.transform.position - center;
                d.y = 0f;
                if (d.sqrMagnitude < 0.0001f) d = Vector3.back;
                d = d.normalized;
                if (d.z > 0f) d.z *= 0.4f;
                Release(m, d * D * UnityEngine.Random.Range(2.5f, 5f) + Vector3.up * D * UnityEngine.Random.Range(0.5f, 2f));
            }
        }

        // ================================================================
        // 大技：メダルの形（ビル・機関車など）
        // ================================================================

        /// <summary>
        /// メダルの形：上空からメダルが飛んできて、points（anchor から見た場所）に組み上がる（assembleSeconds 秒）。
        /// そのあと pose(t) の場所・向きへ moveSeconds 秒かけて動く（t は 0〜1。止めておくなら同じ場所を返す）。
        /// 最後に崩れて、まわりへ散らばる。onSpawn は出したメダルに、onCollapse は崩れる瞬間に呼ぶ
        /// </summary>
        protected Coroutine SculptureRoutine(List<Vector3> points, Func<float, Pose> pose, float assembleSeconds, float moveSeconds,
                                             Action<Medal> onSpawn = null, Action onCollapse = null)
            => StartCoroutine(Sculpture(points, pose, assembleSeconds, moveSeconds, onSpawn, onCollapse));

        IEnumerator Sculpture(List<Vector3> points, Func<float, Pose> pose, float assembleSeconds, float moveSeconds,
                              Action<Medal> onSpawn, Action onCollapse)
        {
            int n = points.Count;
            var medals = new Medal[n];
            var from = new Vector3[n];
            var startT = new float[n];
            Pose p0 = pose(0f);
            for (int i = 0; i < n; i++)
            {
                startT[i] = assembleSeconds * 0.7f * i / Mathf.Max(1, n);
                from[i] = new Vector3(UnityEngine.Random.Range(-HalfW, HalfW), SkyY + D * 2f, Mathf.Lerp(FrontZ, PusherFrontZ, UnityEngine.Random.value));
            }

            // 組み上がる
            for (float t = 0f; t < assembleSeconds + 0.3f; t += Time.deltaTime)
            {
                for (int i = 0; i < n; i++)
                {
                    if (t < startT[i]) continue;
                    if (medals[i] == null)
                    {
                        var m = SpawnFree(from[i], Vector3.zero);
                        if (m == null) continue;
                        Hold(m);
                        medals[i] = m;
                        onSpawn?.Invoke(m);
                    }
                    if (!medals[i].IsActiveInPool) continue;
                    float k = Mathf.Clamp01((t - startT[i]) / (assembleSeconds * 0.3f + 0.01f));
                    var target = p0.position + p0.rotation * points[i];
                    var pos = Vector3.Lerp(from[i], target, Ease.OutCubic(k)) + Vector3.up * Mathf.Sin(k * Mathf.PI) * D * 1.5f;
                    var rot = p0.rotation * Quaternion.Euler(90f, 0f, 0f);   // 面を前に向ける（立てて並べる）
                    medals[i].transform.SetPositionAndRotation(pos, rot);
                    medals[i].Body.position = pos;
                    medals[i].Body.rotation = rot;
                    if (k >= 1f && k - Time.deltaTime / (assembleSeconds * 0.3f + 0.01f) < 1f) SparkAt(target, Color.white, 2);
                }
                yield return null;
            }

            // 動く
            for (float t = 0f; t < moveSeconds; t += Time.deltaTime)
            {
                var p = pose(Mathf.Clamp01(t / Mathf.Max(0.01f, moveSeconds)));
                for (int i = 0; i < n; i++)
                {
                    if (medals[i] == null || !medals[i].IsActiveInPool) continue;
                    var pos = p.position + p.rotation * points[i];
                    var rot = p.rotation * Quaternion.Euler(90f, 0f, 0f);
                    medals[i].transform.SetPositionAndRotation(pos, rot);
                    medals[i].Body.position = pos;
                    medals[i].Body.rotation = rot;
                }
                yield return null;
            }

            // 崩れる
            onCollapse?.Invoke();
            var last = pose(1f);
            for (int i = 0; i < n; i++)
            {
                var m = medals[i];
                if (m == null) continue;
                Vector3 d = m.transform.position - last.position;
                d.y = 0f;
                if (d.sqrMagnitude < 0.0001f) d = UnityEngine.Random.insideUnitSphere;
                d.y = 0f;
                d = d.normalized;
                if (d.z > 0f) d.z *= 0.4f;
                Release(m, d * D * UnityEngine.Random.Range(1f, 3.5f) + Vector3.up * D * UnityEngine.Random.Range(0.5f, 2f));
            }
        }

        // ================================================================
        // 大技：爆発
        // ================================================================

        /// <summary>
        /// 爆発：center のまわり（radius）のメダルをはじき、煙と火花を出し、画面を揺らす。
        /// medals 枚を、爆発の中から周りへ飛び散らせる（onSpawn で印を付けられる）
        /// </summary>
        protected void ExplodeAt(Vector3 center, float radius, float strength, int medals, Color color, Action<Medal> onSpawn = null)
        {
            Blast(center, radius, strength, 0.5f);
            SparkAt(center + Vector3.up * D * 0.5f, color, 60);
            SparkAt(center + Vector3.up * D * 0.5f, Color.white, 30);
            for (int i = 0; i < 6; i++)
                SmokeAt(center + new Vector3(UnityEngine.Random.Range(-radius, radius) * 0.5f, D * (0.5f + i * 0.3f), UnityEngine.Random.Range(-radius, radius) * 0.3f),
                      Color.Lerp(new Color(0.55f, 0.5f, 0.45f, 0.9f), color, 0.25f), 6);
            Shake(0.08f, 0.6f);
            for (int i = 0; i < medals; i++)
            {
                float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (dir.z > 0f) dir.z *= 0.5f;
                var m = SpawnFree(center + Vector3.up * D * (1f + i * 0.04f), dir * D * UnityEngine.Random.Range(2f, 5f) + Vector3.up * D * UnityEngine.Random.Range(3f, 6f));
                if (m != null) onSpawn?.Invoke(m);
            }
        }
    }
}
