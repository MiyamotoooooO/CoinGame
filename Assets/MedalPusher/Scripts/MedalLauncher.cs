using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 盤面の左右の壁についた発射口。メダルを内側（中央）へ向けて飛ばす。
    /// 左: 左クリック / A / ←、右: 右クリック / D / →、Space: 左右交互。長押しで連続発射。
    /// </summary>
    public class MedalLauncher : MonoBehaviour
    {
        MedalPusherGame game;
        float nextLeftTime;
        float nextRightTime;
        float nextAlternateTime;
        bool alternateRight;
        Transform leftChute;
        Transform rightChute;
        float leftKick;
        float rightKick;

        public void Init(MedalPusherGame game)
        {
            this.game = game;
            var mat = ProtoAssets.MakeMaterial(new Color(0.85f, 0.45f, 0.6f), 0f, 0.3f);
            leftChute = BuildChute(-1, mat);
            rightChute = BuildChute(1, mat);
        }

        /// <summary>メダルが出てくる位置。side: -1=左, 1=右</summary>
        public Vector3 MuzzlePosition(int side)
        {
            float x = side * (game.BoardHalfWidth - game.tuning.medalDiameter * 0.5f - 0.08f);
            return new Vector3(x, game.layout.launcherHeight, game.BackWallFrontZ - game.layout.launcherZFromWall);
        }

        /// <summary>発射速度。内側へ飛ばし、少しだけ上と手前に向ける。強さにばらつきを入れる。</summary>
        public Vector3 LaunchVelocity(int side)
        {
            var r = game.rules;
            float k = 1f + Random.Range(-r.launchSpread, r.launchSpread);
            return new Vector3(-side * r.launchSpeed * k, r.launchUpSpeed, -r.launchForwardSpeed * k);
        }

        void Update()
        {
            if (game == null) return;

            Vector2 mouse = InputCompat.MousePosition();
            bool overPanel = game.Hud != null && game.Hud.IsPointerOverPanel(mouse);

            if (InputCompat.LeftFireHeld() || (!overPanel && InputCompat.MouseHeld())) TryFire(-1);
            if (InputCompat.RightFireHeld() || (!overPanel && InputCompat.MouseRightHeld())) TryFire(1);

            if (InputCompat.AlternateFireHeld() && Time.time >= nextAlternateTime)
            {
                if (TryFire(alternateRight ? 1 : -1))
                {
                    alternateRight = !alternateRight;
                    nextAlternateTime = Time.time + game.rules.insertInterval;
                }
            }

            // 発射時に発射口を少し引っ込める演出
            leftKick = Mathf.MoveTowards(leftKick, 0f, Time.deltaTime * 2f);
            rightKick = Mathf.MoveTowards(rightKick, 0f, Time.deltaTime * 2f);
            leftChute.localScale = new Vector3(1f - leftKick, 1f, 1f);
            rightChute.localScale = new Vector3(1f - rightKick, 1f, 1f);
        }

        bool TryFire(int side)
        {
            float next = side < 0 ? nextLeftTime : nextRightTime;
            if (Time.time < next) return false;
            if (!game.TryInsert(MuzzlePosition(side), LaunchVelocity(side))) return false;

            float t = Time.time + game.rules.insertInterval;
            if (side < 0) { nextLeftTime = t; leftKick = 0.2f; }
            else { nextRightTime = t; rightKick = 0.2f; }
            return true;
        }

        /// <summary>見た目だけの発射口（当たり判定なし）。壁から内側へ突き出た樋の形。</summary>
        Transform BuildChute(int side, Material mat)
        {
            Vector3 muzzle = MuzzlePosition(side);
            float wallX = side * game.BoardHalfWidth;

            var root = new GameObject(side < 0 ? "Launcher_L" : "Launcher_R").transform;
            root.position = new Vector3(wallX, muzzle.y, muzzle.z);

            // 子は「壁から内側へ」を -side 方向に並べる。root の X スケールで引っ込み演出をする
            float len = Mathf.Abs(muzzle.x - wallX) + 0.45f;
            float cx = -side * len * 0.5f;
            Part(root, mat, new Vector3(cx, -0.13f, 0f), new Vector3(len, 0.06f, 1.15f));  // 底
            Part(root, mat, new Vector3(cx, 0.02f, 0.6f), new Vector3(len, 0.36f, 0.06f));  // 奥の側板
            Part(root, mat, new Vector3(cx, 0.02f, -0.6f), new Vector3(len, 0.36f, 0.06f)); // 手前の側板
            Part(root, mat, new Vector3(-side * 0.2f, 0.55f, 0f), new Vector3(0.4f, 0.9f, 0.7f)); // 壁側の筒
            return root;
        }

        static void Part(Transform parent, Material mat, Vector3 localPos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }
}
