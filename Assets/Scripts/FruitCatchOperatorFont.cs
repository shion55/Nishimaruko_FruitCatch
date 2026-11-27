using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public sealed class FruitCatchOperatorFont : ScriptableObject
{
    [SerializeField] private Font sourceFont;

    internal TMP_FontAsset CreateRuntimeFont()
    {
        if (sourceFont == null) return null;
        return TMP_FontAsset.CreateFontAsset(sourceFont, 64, 8, GlyphRenderMode.SDFAA,
            1024, 1024, AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true);
    }
}
