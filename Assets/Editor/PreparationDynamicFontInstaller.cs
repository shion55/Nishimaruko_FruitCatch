#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

public static class PreparationDynamicFontInstaller
{
    private const string SourceFontPath = "Assets/Font/MochiyPopPOne-Regular.ttf";
    private const string DynamicFontPath = "Assets/Font/MochiyPopPOne-Regular SDF-Dynamic.asset";
    private const string PreparationScenePath = "Assets/Scenes/Preparation.unity";

    [InitializeOnLoadMethod]
    private static void InstallMissingFontAfterScriptReload()
    {
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isCompiling &&
                !EditorApplication.isPlayingOrWillChangePlaymode &&
                AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DynamicFontPath) == null)
            {
                Install();
            }
        };
    }

    [MenuItem("FruitCatch/Preparation用Dynamicフォントを作成・適用")]
    public static void Install()
    {
        TMP_FontAsset dynamicFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DynamicFontPath);
        if (dynamicFont == null)
            dynamicFont = CreateDynamicFontAsset();

        if (dynamicFont == null)
            return;

        ApplyToPreparationScene(dynamicFont);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Preparation専用Dynamicフォントを作成・適用しました: " + DynamicFontPath);
    }

    private static TMP_FontAsset CreateDynamicFontAsset()
    {
        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (sourceFont == null)
        {
            Debug.LogError("PreparationDynamicFontInstaller: 元フォントが見つかりません: " + SourceFontPath);
            return null;
        }

        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
            sourceFont,
            90,
            9,
            GlyphRenderMode.SDFAA,
            1024,
            1024,
            AtlasPopulationMode.Dynamic,
            true);

        if (fontAsset == null)
        {
            Debug.LogError("PreparationDynamicFontInstaller: Dynamicフォントの生成に失敗しました。");
            return null;
        }

        string assetName = Path.GetFileNameWithoutExtension(DynamicFontPath);
        fontAsset.name = assetName;
        AssetDatabase.CreateAsset(fontAsset, DynamicFontPath);

        if (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0)
        {
            Texture2D atlas = fontAsset.atlasTextures[0];
            atlas.name = assetName + " Atlas";
            AssetDatabase.AddObjectToAsset(atlas, fontAsset);
        }

        if (fontAsset.material != null)
        {
            fontAsset.material.name = assetName + " Material";
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        }

        fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        fontAsset.isMultiAtlasTexturesEnabled = true;
        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();
        return fontAsset;
    }

    private static void ApplyToPreparationScene(TMP_FontAsset fontAsset)
    {
        if (!File.Exists(PreparationScenePath))
        {
            Debug.LogError("PreparationDynamicFontInstaller: Preparationシーンが見つかりません。");
            return;
        }

        Scene scene = SceneManager.GetSceneByPath(PreparationScenePath);
        bool openedForInstall = !scene.IsValid() || !scene.isLoaded;

        if (openedForInstall)
            scene = EditorSceneManager.OpenScene(PreparationScenePath, OpenSceneMode.Additive);

        bool changed = false;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.font == fontAsset)
                    continue;

                text.font = fontAsset;
                EditorUtility.SetDirty(text);
                changed = true;
            }
        }

        if (changed)
            EditorSceneManager.SaveScene(scene);

        if (openedForInstall)
            EditorSceneManager.CloseScene(scene, true);
    }
}
#endif
