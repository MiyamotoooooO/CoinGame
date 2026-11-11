using UnityEditor;

namespace MedalPusher
{
    /// <summary>
    /// Resources/AreaMovies の中の画像を、ムービー用の設定で読みこむ。
    /// （ぼやけないように縮小や Mipmap をしない。1920×1080 のまま使う）
    /// </summary>
    public class AreaMovieTextureImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/AreaMovies/")) return;
            TextureImporter imp = (TextureImporter)assetImporter;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.maxTextureSize = 2048;
            imp.npotScale = TextureImporterNPOTScale.None;
        }
    }
}
