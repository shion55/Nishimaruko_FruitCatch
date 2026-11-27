using UnityEngine;

public enum ItemCategory
{
    Fruit,
    Bug
}

public enum ItemKind
{
    Fruit,
    Bug,
    Banana,
    Chili,
    FeverFruit
}

public enum ItemEffectType
{
    None,
    BananaPeel,
    Chili,
    Fever
}

[CreateAssetMenu(fileName = "ItemDefinition", menuName = "FruitCatch/Item Definition")]
public sealed class ItemDefinition : ScriptableObject
{
    [SerializeField, Tooltip("フルーツを識別するID。種類ごとに異なる値を設定します。")] private string itemId = "apple";
    [SerializeField] private ItemKind kind = ItemKind.Fruit;
    [SerializeField] private int scoreDelta = 10; // 種類ごとの加減点。Assets/Data/Itemsの定義で調整。

    [Header("Normal Fruit")]
    [SerializeField] private string displayName;
    [SerializeField, Tooltip("未設定の場合はPrefabのSpriteを使います。")] private Sprite sprite;
    [SerializeField, Min(0f)] private float normalSpawnWeight = 1f;
    [SerializeField, Min(0f)] private float feverSpawnWeight = 1f;
    [SerializeField] private bool rareVisual;

    public string ItemId => kind == ItemKind.Fruit && string.IsNullOrWhiteSpace(itemId) ? name : itemId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? ItemId : displayName;
    public Sprite Sprite => sprite;
    public ItemKind Kind => kind;
    public ItemCategory Category => kind == ItemKind.Bug ? ItemCategory.Bug : ItemCategory.Fruit;
    public int ScoreDelta => scoreDelta;
    public bool RareVisual => rareVisual;

    public float GetSpawnWeight(bool fever) => Mathf.Max(0f, fever ? feverSpawnWeight : normalSpawnWeight);

    public ItemEffectType Effect => kind switch
    {
        ItemKind.Banana => ItemEffectType.BananaPeel,
        ItemKind.Chili => ItemEffectType.Chili,
        ItemKind.FeverFruit => ItemEffectType.Fever,
        _ => ItemEffectType.None
    };
}
