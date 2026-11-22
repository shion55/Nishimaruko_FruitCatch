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
    [SerializeField] private string itemId = "apple";
    [SerializeField] private ItemKind kind = ItemKind.Fruit;
    [SerializeField] private int scoreDelta = 10;
    [Tooltip("拡大縮小前の横幅。生成されたアイテムの横スケールを掛けて取得判定に使います。")]
    [SerializeField, Min(0.01f)] private float localCatchWidth = 1f;

    public string ItemId => itemId;
    public ItemKind Kind => kind;
    public ItemCategory Category => kind == ItemKind.Bug ? ItemCategory.Bug : ItemCategory.Fruit;
    public int ScoreDelta => scoreDelta;
    public float LocalCatchWidth => Mathf.Max(0.01f, localCatchWidth);

    public ItemEffectType Effect => kind switch
    {
        ItemKind.Banana => ItemEffectType.BananaPeel,
        ItemKind.Chili => ItemEffectType.Chili,
        ItemKind.FeverFruit => ItemEffectType.Fever,
        _ => ItemEffectType.None
    };
}
