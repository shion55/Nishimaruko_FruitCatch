using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace FruitCatch.Preparation
{
    /// <summary>
    /// [漢字|かんじ] の形式で指定した読みを、本文の漢字の真上へ表示します。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(TMP_Text))]
    public sealed class FuriganaText : MonoBehaviour
    {
        [SerializeField, TextArea(2, 10), InspectorName("ふりがな付き文章 [漢字|かんじ]")]
        private string annotatedText;
        [SerializeField, Range(0.3f, 0.8f)] private float rubyFontScale = 0.5f;
        [SerializeField] private float verticalOffset = 3f;
        [SerializeField] private Color rubyColor = Color.black;

        private readonly List<TMP_Text> rubyObjects = new List<TMP_Text>();
        private TMP_Text baseText;
        private Coroutine rebuildCoroutine;
        private bool rebuildQueued;
        private bool isRebuilding;

        private struct RubyRange
        {
            public int Start;
            public int Length;
            public string Reading;
        }

        public string AnnotatedText
        {
            get => annotatedText;
            set
            {
                annotatedText = value;
                QueueRebuild();
            }
        }

        private void OnEnable()
        {
            baseText = GetComponent<TMP_Text>();
            QueueRebuild();
        }

        private void OnDisable()
        {
            ClearRubyObjects();
        }

        private void OnValidate()
        {
            QueueRebuild();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (isRebuilding)
                return;
            QueueRebuild();
        }

        private void Update()
        {
            // Edit ModeではOnValidate中に階層を変更できないため、
            // 次のEditor更新まで実際の生成を遅らせる。
            if (!Application.isPlaying && rebuildQueued && !isRebuilding)
                Rebuild();
        }

        public void SetAnnotatedText(string value)
        {
            AnnotatedText = value;
        }

        public void RefreshPreview()
        {
            QueueRebuild();
        }

        private void QueueRebuild()
        {
            if (!isActiveAndEnabled || rebuildQueued)
                return;

            rebuildQueued = true;
            if (Application.isPlaying)
            {
                if (rebuildCoroutine != null)
                    StopCoroutine(rebuildCoroutine);
                rebuildCoroutine = StartCoroutine(RebuildNextFrame());
            }
        }

        private IEnumerator RebuildNextFrame()
        {
            yield return null;
            Rebuild();
            rebuildCoroutine = null;
        }

        private void Rebuild()
        {
            if (isRebuilding)
                return;

            isRebuilding = true;
            rebuildQueued = false;
            try
            {
                if (baseText == null)
                    baseText = GetComponent<TMP_Text>();
                if (baseText == null)
                    return;

                ClearRubyObjects();
                ParseAnnotatedText(annotatedText, out string displayText, out List<RubyRange> ranges);
                baseText.text = displayText;
                baseText.ForceMeshUpdate(true, true);

                TMP_TextInfo info = baseText.textInfo;
                foreach (RubyRange range in ranges)
                    CreateRubyObject(range, info);
            }
            finally
            {
                isRebuilding = false;
            }
        }

        private void CreateRubyObject(RubyRange range, TMP_TextInfo info)
        {
            if (range.Start < 0 || range.Start >= info.characterCount || range.Length <= 0)
                return;

            int end = Mathf.Min(info.characterCount, range.Start + range.Length);
            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            bool found = false;

            for (int i = range.Start; i < end; i++)
            {
                TMP_CharacterInfo character = info.characterInfo[i];
                if (!character.isVisible)
                    continue;

                minX = Mathf.Min(minX, character.bottomLeft.x);
                maxX = Mathf.Max(maxX, character.topRight.x);
                maxY = Mathf.Max(maxY, character.ascender);
                found = true;
            }

            if (!found)
                return;

            GameObject rubyObject = new GameObject("Furigana_" + range.Reading, typeof(RectTransform));
            rubyObject.hideFlags = HideFlags.DontSaveInEditor;
            RectTransform rect = rubyObject.GetComponent<RectTransform>();
            rect.SetParent(baseText.transform, false);
            rect.anchorMin = baseText.rectTransform.pivot;
            rect.anchorMax = baseText.rectTransform.pivot;
            rect.pivot = new Vector2(0.5f, 0.5f);

            float rubySize = Mathf.Max(8f, baseText.fontSize * rubyFontScale);
            rect.sizeDelta = new Vector2(Mathf.Max(maxX - minX + rubySize, rubySize * range.Reading.Length), rubySize * 1.5f);
            rect.anchoredPosition = new Vector2((minX + maxX) * 0.5f, maxY + rubySize * 0.52f + verticalOffset);

            TextMeshProUGUI ruby = rubyObject.AddComponent<TextMeshProUGUI>();
            ruby.text = range.Reading;
            ruby.font = baseText.font;
            ruby.fontSize = rubySize;
            ruby.alignment = TextAlignmentOptions.Center;
            ruby.color = rubyColor;
            ruby.raycastTarget = false;
            ruby.enableWordWrapping = false;
            ruby.overflowMode = TextOverflowModes.Overflow;
            rubyObjects.Add(ruby);
        }

        private void ClearRubyObjects()
        {
            for (int i = rubyObjects.Count - 1; i >= 0; i--)
            {
                if (rubyObjects[i] == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(rubyObjects[i].gameObject);
                else
                    DestroyImmediate(rubyObjects[i].gameObject);
            }
            rubyObjects.Clear();
        }

        private static void ParseAnnotatedText(string source, out string displayText, out List<RubyRange> ranges)
        {
            ranges = new List<RubyRange>();
            var builder = new StringBuilder();

            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] == '[')
                {
                    int separator = source.IndexOf('|', i + 1);
                    int close = separator >= 0 ? source.IndexOf(']', separator + 1) : -1;
                    if (separator > i + 1 && close > separator + 1)
                    {
                        string baseCharacters = source.Substring(i + 1, separator - i - 1);
                        string reading = source.Substring(separator + 1, close - separator - 1);
                        ranges.Add(new RubyRange
                        {
                            Start = builder.Length,
                            Length = baseCharacters.Length,
                            Reading = reading
                        });
                        builder.Append(baseCharacters);
                        i = close;
                        continue;
                    }
                }

                builder.Append(source[i]);
            }

            displayText = builder.ToString();
        }
    }
}
