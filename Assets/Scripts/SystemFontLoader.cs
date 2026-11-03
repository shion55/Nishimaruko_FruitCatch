
using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public class SystemFontLoader : MonoBehaviour
{
    private TMP_FontAsset systemFont;

    void Awake()
    {
        string[] paths = Font.GetPathsToOSFonts();

        Debug.Log($"OSフォントファイル数: {paths.Length}");

        FontEngine.InitializeFontEngine();

        foreach (string path in paths)
        {
            try
            {
                // フォントファイルを直接読み込む
                if (FontEngine.LoadFontFace(path, 90, 0)
                    != FontEngineError.Success)
                    continue;

                // TTCは複数の書体を含むため、それぞれ確認
                int faceCount = 1;

                if (Path.GetExtension(path).Equals(
                    ".ttc", StringComparison.OrdinalIgnoreCase))
                {
                    string[] faces = FontEngine.GetFontFaces();
                    if (faces != null && faces.Length > 0)
                        faceCount = faces.Length;
                }

                for (int faceIndex = 0; faceIndex < faceCount; faceIndex++)
                {
                    if (FontEngine.LoadFontFace(path, 90, faceIndex)
                        != FontEngineError.Success)
                        continue;

                    // 実際のグリフの有無を確認
                    if (!FontEngine.TryGetGlyphIndex('あ', out _) ||
                        !FontEngine.TryGetGlyphIndex('イ', out _) ||
                        !FontEngine.TryGetGlyphIndex('漢', out _))
                        continue;

                    // ファイルパスから直接TMP Font Assetを生成
                    TMP_FontAsset asset =
                        TMP_FontAsset.CreateFontAsset(
                            path,
                            faceIndex,
                            90,
                            9,
                            GlyphRenderMode.SDFAA,
                            1024,
                            1024
                        );

                    if (asset == null)
                        continue;

                    asset.isMultiAtlasTexturesEnabled = true;

                    if (!asset.TryAddCharacters("あイ漢"))
                    {
                        Destroy(asset);
                        continue;
                    }

                    systemFont = asset;
                    TMP_Settings.fallbackFontAssets.Add(systemFont);

                    Debug.Log(
                        $"日本語フォント読み込み成功: {path} " +
                        $"(face: {faceIndex})"
                    );

                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"フォント読み込み失敗: {path} / {e.Message}"
                );
            }
        }

        Debug.LogWarning(
            "日本語フォントのTMP生成に失敗しました。"
        );
    }

    void OnDestroy()
    {
        if (systemFont != null)
        {
            TMP_Settings.fallbackFontAssets.Remove(systemFont);
            Destroy(systemFont);
        }
    }
}
