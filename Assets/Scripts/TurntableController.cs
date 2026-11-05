using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// ターンテーブルを動かす。シーンの「recorder」（Record と Turntable_Tonearm の親）に付けて使う。
    ///
    /// ・いま流しているレコード（RecordAudioPlayer.CurrentRecord）に合わせて、レコードを回し、アームを下ろす
    /// ・流すレコードが替わると、アームを上げる → レコードの見た目を替える → アームを下ろす、の順に動く
    /// ・レコードの見た目：ラベルに画像を貼る
    ///     Label Textures にそのレコードの画像があればそれ、無ければジャケット画像（RecordArtwork）、
    ///     どちらも無ければ系統の色で塗る
    ///
    /// 部品は Inspector で指定する。空なら、この GameObject の子 → シーン全体の順に名前で探す。
    /// アームの角度は、部品の Y 軸まわりの回転（シーンに置いたときの X・Z の回転はそのまま）。
    /// </summary>
    [DisallowMultipleComponent]
    public class TurntableController : MonoBehaviour
    {
        [Header("動かす部品（空なら名前で探す）")]
        [Tooltip("回すレコード（名前：Record）")]
        public Transform record;
        [Tooltip("アーム（名前：Turntable_Tonearm）")]
        public Transform tonearm;
        [Tooltip("レコードと一緒に回す皿（名前：Turntable_Platter）。空でもよい")]
        public Transform platter;
        [Tooltip("曲を流している RecordAudioPlayer（空なら自動で探す）")]
        public RecordAudioPlayer audioPlayer;
        [Tooltip("ジャケット画像を持っている RecordArtwork（空なら自動で探す）")]
        public RecordArtwork artwork;

        [Header("回転")]
        [Tooltip("回る速さ（回/分）。本物の LP は 33.3、シングル盤は 45")]
        public float rpm = 33.3f;
        [Tooltip("回り始めてから決まった速さになるまで、また止まるまでの秒数")]
        [Range(0.05f, 3f)] public float spinUpSeconds = 0.6f;
        [Tooltip("回る向き（1 = 上から見て時計回り、-1 = 反時計回り）")]
        public float spinDirection = 1f;

        [Header("アーム（Y 軸まわりの角度）")]
        [Tooltip("休んでいるときの角度")]
        public float armRestAngle = 0f;
        [Tooltip("レコードの上に下りたときの角度")]
        public float armPlayAngle = 40f;
        [Tooltip("アームが動く秒数")]
        [Range(0.05f, 3f)] public float armMoveSeconds = 0.8f;

        [Header("見た目（ラベル）")]
        [Tooltip("ラベル専用の画像。ファイル名をレコードのキー（英名）にする（Pops.png など）。無いレコードはジャケット画像を使う")]
        public Texture2D[] labelTextures = new Texture2D[0];
        [Tooltip("ラベル専用の画像が無いとき、ジャケット画像をラベルに貼る")]
        public bool useJacketForLabel = true;
        [Tooltip("画像が無いレコードは、系統の色でラベルを塗る")]
        public bool colorByCategory = true;
        [Tooltip("ラベルのマテリアルの名前に含まれる文字（モデルの LabelA と LabelB）")]
        public string[] labelMaterialNames = { "LabelA", "LabelB" };

        /// <summary>いまターンテーブルに載っているレコード（何も載っていなければ null）</summary>
        public RecordData Displayed { get; private set; }

        /// <summary>載っているレコードが替わったときに呼ばれる（演出用）</summary>
        public event System.Action<RecordData> RecordChanged;

        RecordData wanted;            // 載せたいレコード
        bool manual;                  // Play / Stop で直接指定されているか
        Quaternion recordBaseRotation;
        Quaternion platterBaseRotation;
        Vector3 armBaseEuler;
        float spinAngle;
        float spinSpeed;              // いまの回る速さ（度/秒）
        float armAngle;
        readonly List<Material> labelMaterials = new List<Material>();
        readonly Dictionary<string, Texture2D> textureByKey = new Dictionary<string, Texture2D>();
        readonly Dictionary<Material, Color> defaultLabelColors = new Dictionary<Material, Color>();

        void Start()
        {
            if (record == null) record = FindPart("Record");
            if (tonearm == null) tonearm = FindPart("Turntable_Tonearm");
            if (platter == null) platter = FindPart("Turntable_Platter");
            if (record == null) Debug.LogWarning("TurntableController: レコード（Record）が見つかりません。", this);
            if (tonearm == null) Debug.LogWarning("TurntableController: アーム（Turntable_Tonearm）が見つかりません。", this);

            if (record != null) recordBaseRotation = record.localRotation;
            if (platter != null) platterBaseRotation = platter.localRotation;
            if (tonearm != null) armBaseEuler = tonearm.localEulerAngles;

            // 最初はアームを休ませておく
            armAngle = armRestAngle;
            ApplyArm();

            CollectLabelMaterials();
            BuildTextureTable();
            ApplyAppearance(null);
        }

        void OnValidate()
        {
            if (Application.isPlaying) BuildTextureTable();
        }

        /// <summary>
        /// レコードを直接載せる（RecordAudioPlayer を使わないとき用）。
        /// これを呼ぶと、Stop を呼ぶまで RecordAudioPlayer には合わせなくなる。
        /// </summary>
        public void Play(RecordData recordData)
        {
            manual = true;
            wanted = RecordData.IsNullOrEmpty(recordData) ? null : recordData;
        }

        /// <summary>レコードを止めてアームを上げる。RecordAudioPlayer に合わせる動きに戻る。</summary>
        public void Stop()
        {
            manual = false;
            wanted = null;
        }

        void Update()
        {
            if (!manual)
            {
                if (audioPlayer == null) audioPlayer = FindFirstObjectByType<RecordAudioPlayer>();
                wanted = audioPlayer != null ? audioPlayer.CurrentRecord : null;
            }

            bool changing = !SameRecord(wanted, Displayed);
            float targetArm;
            float targetSpin;

            if (changing)
            {
                // 替えるときは、アームを上げてレコードを止めてから載せ替える
                targetArm = armRestAngle;
                targetSpin = 0f;
                bool armUp = Mathf.Approximately(armAngle, armRestAngle);
                bool stopped = Mathf.Abs(spinSpeed) < 1f;
                if (armUp && (stopped || Displayed == null))
                {
                    Displayed = wanted;
                    ApplyAppearance(Displayed);
                    RecordChanged?.Invoke(Displayed);
                }
            }
            else if (Displayed != null)
            {
                targetArm = armPlayAngle;
                targetSpin = rpm * 6f * Mathf.Sign(spinDirection == 0f ? 1f : spinDirection);   // 回/分 → 度/秒
            }
            else
            {
                targetArm = armRestAngle;
                targetSpin = 0f;
            }

            // アーム
            float armSpeed = Mathf.Abs(armPlayAngle - armRestAngle) / Mathf.Max(0.05f, armMoveSeconds);
            armAngle = Mathf.MoveTowards(armAngle, targetArm, armSpeed * Time.deltaTime);
            ApplyArm();

            // 回転
            float maxSpin = Mathf.Max(1f, rpm * 6f);
            spinSpeed = Mathf.MoveTowards(spinSpeed, targetSpin, maxSpin / Mathf.Max(0.05f, spinUpSeconds) * Time.deltaTime);
            spinAngle = Mathf.Repeat(spinAngle + spinSpeed * Time.deltaTime, 360f);
            Quaternion spin = Quaternion.Euler(0f, spinAngle, 0f);
            if (record != null) record.localRotation = recordBaseRotation * spin;
            if (platter != null) platter.localRotation = platterBaseRotation * spin;
        }

        void ApplyArm()
        {
            if (tonearm == null) return;
            tonearm.localEulerAngles = new Vector3(armBaseEuler.x, armAngle, armBaseEuler.z);
        }

        static bool SameRecord(RecordData a, RecordData b)
        {
            if (a == null || b == null) return a == b;
            return a.key == b.key;
        }

        Transform FindPart(string partName)
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == partName) return t;
            }
            GameObject found = GameObject.Find(partName);
            return found != null ? found.transform : null;
        }

        // ================================================================
        // 見た目
        // ================================================================

        void CollectLabelMaterials()
        {
            labelMaterials.Clear();
            defaultLabelColors.Clear();
            if (record == null) return;

            foreach (Renderer renderer in record.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.materials;   // このレコード専用のマテリアルを作る（ほかのレコードの見た目を変えない）
                foreach (Material material in materials)
                {
                    foreach (string labelName in labelMaterialNames)
                    {
                        if (!string.IsNullOrEmpty(labelName) && material.name.Contains(labelName))
                        {
                            labelMaterials.Add(material);
                            defaultLabelColors[material] = GetColor(material);
                            break;
                        }
                    }
                }
            }
            if (labelMaterials.Count == 0)
            {
                Debug.LogWarning("TurntableController: レコードのラベルのマテリアル（LabelA / LabelB）が見つからないため、見た目は変わりません。", this);
            }
        }

        void BuildTextureTable()
        {
            textureByKey.Clear();
            if (labelTextures == null) return;
            foreach (Texture2D texture in labelTextures)
            {
                if (texture != null) textureByKey[texture.name] = texture;
            }
        }

        /// <summary>ラベルに貼る画像。ラベル専用の画像 → ジャケット画像の順に探す。無ければ null。</summary>
        Texture2D FindLabelTexture(RecordData recordData)
        {
            if (recordData == null) return null;
            if (textureByKey.TryGetValue(recordData.key, out Texture2D texture)) return texture;
            if (!useJacketForLabel) return null;
            if (artwork == null) artwork = FindFirstObjectByType<RecordArtwork>();
            return artwork != null ? artwork.GetJacket(recordData) : null;
        }

        /// <summary>レコードの種類に合わせて、ラベルの画像と色を替える。null なら元の見た目に戻す。</summary>
        void ApplyAppearance(RecordData recordData)
        {
            Texture2D texture = FindLabelTexture(recordData);

            foreach (Material material in labelMaterials)
            {
                Color color = defaultLabelColors[material];
                if (texture != null)
                {
                    color = Color.white;   // 画像の色をそのまま見せる
                }
                else if (recordData != null && colorByCategory && RecordArtwork.HasCategoryColor(recordData.category))
                {
                    color = RecordArtwork.CategoryColor(recordData.category);
                }
                SetTexture(material, texture);
                SetColor(material, color);
            }
        }

        static Color GetColor(Material material)
        {
            if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");   // URP
            if (material.HasProperty("_Color")) return material.GetColor("_Color");           // Built-in
            return Color.white;
        }

        static void SetColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        static void SetTexture(Material material, Texture texture)
        {
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
        }
    }
}
