using UnityEngine;
using UnityEngine.EventSystems;

// TMP's numeric fields still ask BaseInput for IME state when using InputSystemUIInputModule.
internal sealed class FruitCatchOperatorTextInput : BaseInput
{
    public override string compositionString => string.Empty;
    public override IMECompositionMode imeCompositionMode { get => IMECompositionMode.Off; set { } }
    public override Vector2 compositionCursorPos { get => Vector2.zero; set { } }
}
