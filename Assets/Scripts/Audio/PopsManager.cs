using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class PopsManager : MonoBehaviour
{
    [Header("参照設定")]
    [Tooltip("パーティクル管理を行っているCoinDropTriggerManager（空欄でも自動取得します）")]
    [SerializeField] private CoinDropTriggerManager triggerManager;

    [Header("テクスチャ切り替え設定")]
    [Tooltip("PopsのAudioが流れている間に適用するスプライト")]
    [SerializeField] private Sprite popsTextureSprite;

    [Tooltip("識別するAudioの名前")]
    [SerializeField] private string targetAudioName = "Pops";

    // 各パーティクルのRendererとマテリアル・テクスチャを安全に管理する内部データ
    private class ManagedRendererData
    {
        public ParticleSystemRenderer renderer;
        public Material runtimeMaterial;
        public Material sharedSourceMaterial;
        public Texture originalTexture;
    }

    private readonly List<ManagedRendererData> managedRenderers = new List<ManagedRendererData>();
    private bool isInitialized = false;
    private bool isPopsPlayingState = false;
    private Texture cachedDefaultParticleTex;

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

    private void Start()
    {
        if (triggerManager == null)
        {
            triggerManager = CoinDropTriggerManager.Instance;
            if (triggerManager == null)
            {
                triggerManager = FindObjectOfType<CoinDropTriggerManager>();
            }
        }

        InitializeManagedParticles();
        UpdateAudioAndTextureState(forceUpdate: true);
    }

    private void Update()
    {
        if (!isInitialized)
        {
            InitializeManagedParticles();
            if (isInitialized)
            {
                UpdateAudioAndTextureState(forceUpdate: true);
            }
        }

        UpdateAudioAndTextureState(forceUpdate: false);
    }

    /// <summary>
    /// CoinDropTriggerManagerのプール内にある実体パーティクルを取得し、安全にテクスチャを切り替えられるよう準備する
    /// </summary>
    private void InitializeManagedParticles()
    {
        if (triggerManager == null || isInitialized) return;

        Type type = triggerManager.GetType();
        BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;

        // CoinDropTriggerManager内で実際に画面で再生される3つのプールを取得
        RegisterPoolRenderers(type.GetField("weakPool", flags)?.GetValue(triggerManager) as List<ParticleSystem>);
        RegisterPoolRenderers(type.GetField("normalPool", flags)?.GetValue(triggerManager) as List<ParticleSystem>);
        RegisterPoolRenderers(type.GetField("grandPool", flags)?.GetValue(triggerManager) as List<ParticleSystem>);

        // 参照元の3つのパーティクルを登録
        RegisterParticleReference(type.GetField("weakParticle", flags)?.GetValue(triggerManager) as ParticleSystem);
        RegisterParticleReference(type.GetField("normalParticle", flags)?.GetValue(triggerManager) as ParticleSystem);
        RegisterParticleReference(type.GetField("grandParticle", flags)?.GetValue(triggerManager) as ParticleSystem);

        if (managedRenderers.Count > 0)
        {
            isInitialized = true;
        }
    }

    private void RegisterPoolRenderers(List<ParticleSystem> pool)
    {
        if (pool == null) return;

        for (int i = 0; i < pool.Count; i++)
        {
            RegisterParticleReference(pool[i]);
        }
    }

    private void RegisterParticleReference(ParticleSystem ps)
    {
        if (ps == null) return;

        Texture defaultTex = GetDefaultParticleTexture();
        bool isSceneObject = ps.gameObject.scene.IsValid();

        ParticleSystemRenderer[] renderers = ps.GetComponentsInChildren<ParticleSystemRenderer>(true);
        foreach (ParticleSystemRenderer rend in renderers)
        {
            if (rend == null) continue;

            // 重複登録防止
            if (managedRenderers.Exists(d => d.renderer == rend)) continue;

            Material baseMat = rend.sharedMaterial;
            Material safeRuntimeMat = null;

            // シーン上のオブジェクトであればインスタンス用マテリアルを作成して割り当てる（Prefabアセットには .material を割り当てない）
            if (isSceneObject)
            {
                safeRuntimeMat = CreateSafeRuntimeMaterial(baseMat);
                SetMaterialTexture(safeRuntimeMat, defaultTex);
                rend.material = safeRuntimeMat;
            }

            // 共有マテリアル自体が存在し正常な場合は、初期状態を Default-Particle にしておく
            if (IsValidMaterial(baseMat))
            {
                SetMaterialTexture(baseMat, defaultTex);
            }

            managedRenderers.Add(new ManagedRendererData
            {
                renderer = rend,
                runtimeMaterial = safeRuntimeMat,
                sharedSourceMaterial = IsValidMaterial(baseMat) ? baseMat : null,
                originalTexture = defaultTex
            });
        }
    }

    private bool IsValidMaterial(Material mat)
    {
        return mat != null && mat.shader != null && mat.shader.name != "Hidden/InternalErrorShader";
    }

    /// <summary>
    /// マテリアルがMissingやエラー（ピンク色）になっていても安全なマテリアルを生成する
    /// </summary>
    private Material CreateSafeRuntimeMaterial(Material sourceMat)
    {
        if (IsValidMaterial(sourceMat))
        {
            return new Material(sourceMat);
        }

#if UNITY_EDITOR
        Material builtinParticleMat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
        if (builtinParticleMat != null)
        {
            return new Material(builtinParticleMat);
        }
#endif

        // 万が一マテリアルが外れてピンク色になっていた場合の自動復旧用シェーダー検索
        string[] fallbackShaders =
        {
            "Universal Render Pipeline/Particles/Unlit",
            "Particles/Standard Unlit",
            "Legacy Shaders/Particles/Alpha Blended",
            "Sprites/Default"
        };

        foreach (string shaderName in fallbackShaders)
        {
            Shader s = Shader.Find(shaderName);
            if (s != null)
            {
                Material fallbackMat = new Material(s);
                fallbackMat.mainTexture = GetDefaultParticleTexture();
                return fallbackMat;
            }
        }

        return new Material(Shader.Find("Sprites/Default"));
    }

    /// <summary>
    /// PopsのAudioが流れているかチェックし、状態が変わったらテクスチャを切り替える
    /// </summary>
    private void UpdateAudioAndTextureState(bool forceUpdate)
    {
        bool popsPlaying = IsPopsAudioPlaying();

        if (!forceUpdate && popsPlaying == isPopsPlayingState) return;

        isPopsPlayingState = popsPlaying;

        Texture popsTex = popsTextureSprite != null ? popsTextureSprite.texture : null;
        Texture defaultTex = GetDefaultParticleTexture();

        for (int i = 0; i < managedRenderers.Count; i++)
        {
            ManagedRendererData data = managedRenderers[i];
            if (data.renderer == null) continue;

            Texture normalTex = data.originalTexture != null ? data.originalTexture : defaultTex;
            Texture targetTex = (isPopsPlayingState && popsTex != null) ? popsTex : normalTex;

            if (data.runtimeMaterial != null)
            {
                SetMaterialTexture(data.runtimeMaterial, targetTex);
            }

            if (data.sharedSourceMaterial != null)
            {
                SetMaterialTexture(data.sharedSourceMaterial, targetTex);
            }
        }
    }

    /// <summary>
    /// シーン内で「Pops」という名前のAudioが再生中か判定する
    /// </summary>
    private bool IsPopsAudioPlaying()
    {
        if (string.IsNullOrEmpty(targetAudioName)) return false;

        AudioSource[] sources = FindObjectsOfType<AudioSource>();
        for (int i = 0; i < sources.Length; i++)
        {
            AudioSource src = sources[i];
            if (src != null && src.isPlaying && src.clip != null)
            {
                if (src.clip.name.IndexOf(targetAudioName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    src.gameObject.name.IndexOf(targetAudioName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private void SetMaterialTexture(Material mat, Texture tex)
    {
        if (mat == null) return;

        mat.mainTexture = tex;
        if (mat.HasProperty(MainTexId))
        {
            mat.SetTexture(MainTexId, tex);
        }
        if (mat.HasProperty(BaseMapId))
        {
            mat.SetTexture(BaseMapId, tex);
        }
    }

    /// <summary>
    /// Unity標準の「Default-Particle」テクスチャを内部から確実に自動識別して取得する
    /// </summary>
    private Texture GetDefaultParticleTexture()
    {
        if (cachedDefaultParticleTex != null) return cachedDefaultParticleTex;

#if UNITY_EDITOR
        // エディタ環境では unity_builtin_extra から直接 Default-Particle を取得
        cachedDefaultParticleTex = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
        if (cachedDefaultParticleTex != null) return cachedDefaultParticleTex;

        Material defaultMat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
        if (defaultMat != null && defaultMat.mainTexture != null)
        {
            cachedDefaultParticleTex = defaultMat.mainTexture;
            return cachedDefaultParticleTex;
        }
#endif

        // メモリ上にロードされているテクスチャから "Default-Particle" を検索
        Texture[] loadedTextures = Resources.FindObjectsOfTypeAll<Texture>();
        for (int i = 0; i < loadedTextures.Length; i++)
        {
            if (loadedTextures[i] != null && loadedTextures[i].name == "Default-Particle")
            {
                cachedDefaultParticleTex = loadedTextures[i];
                return cachedDefaultParticleTex;
            }
        }

        // ビルド後などで万が一取得できなかった場合、Default-Particleと同等のソフト円形テクスチャを自動生成
        int size = 64;
        Texture2D fallbackTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        fallbackTex.name = "Default-Particle";
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float maxRadius = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center) / maxRadius;
                float alpha = Mathf.Clamp01(1f - dist);
                alpha = alpha * alpha;
                fallbackTex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        fallbackTex.Apply();
        cachedDefaultParticleTex = fallbackTex;
        return cachedDefaultParticleTex;
    }

    private void OnDestroy()
    {
        Texture defaultTex = GetDefaultParticleTexture();

        for (int i = 0; i < managedRenderers.Count; i++)
        {
            // 再生終了時に共有マテリアルのテクスチャも Default-Particle に戻しておく
            if (managedRenderers[i].sharedSourceMaterial != null && defaultTex != null)
            {
                SetMaterialTexture(managedRenderers[i].sharedSourceMaterial, defaultTex);
            }

            if (managedRenderers[i].runtimeMaterial != null)
            {
                Destroy(managedRenderers[i].runtimeMaterial);
            }
        }
        managedRenderers.Clear();
    }
}