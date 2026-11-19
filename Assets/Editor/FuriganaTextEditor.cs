#if UNITY_EDITOR
using FruitCatch.Preparation;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(FuriganaText))]
public sealed class FuriganaTextEditor : Editor
{
    private SerializedProperty annotatedText;
    private SerializedProperty rubyFontScale;
    private SerializedProperty verticalOffset;
    private SerializedProperty rubyColor;

    private void OnEnable()
    {
        annotatedText = serializedObject.FindProperty("annotatedText");
        rubyFontScale = serializedObject.FindProperty("rubyFontScale");
        verticalOffset = serializedObject.FindProperty("verticalOffset");
        rubyColor = serializedObject.FindProperty("rubyColor");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "文章はTMPのText欄ではなく、下の『ふりがな付き文章』で編集します。\n" +
            "例：[顔|かお]をわくに[合|あ]わせてね",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(annotatedText);
        EditorGUILayout.PropertyField(rubyFontScale, new GUIContent("ふりがなの大きさ"));
        EditorGUILayout.PropertyField(verticalOffset, new GUIContent("漢字からの上下距離"));
        EditorGUILayout.PropertyField(rubyColor, new GUIContent("ふりがなの色"));

        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            foreach (Object item in targets)
            {
                FuriganaText furigana = (FuriganaText)item;
                furigana.RefreshPreview();
                EditorUtility.SetDirty(furigana);
            }
        }
        else
        {
            serializedObject.ApplyModifiedProperties();
        }

        if (GUILayout.Button("ふりがな表示を更新"))
        {
            foreach (Object item in targets)
                ((FuriganaText)item).RefreshPreview();
        }
    }
}
#endif
