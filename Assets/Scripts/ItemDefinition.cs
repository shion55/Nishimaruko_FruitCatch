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
    // 皮の着地先は画面幅の35%／65%付近を想定（投擲は未実装）。
    BananaPeel,
    Chili,
    Fever
}

[CreateAssetMenu(fileName = "ItemDefinition", menuName = "FruitCatch/Item Definition")]
public sealed class ItemDefinition : ScriptableObject
{
    [SerializeField] private string itemId = "apple";
    [SerializeField] private ItemKind kind = ItemKind.Fruit;
    [SerializeField] private int scoreDelta = 10; // 種類ごとの加減点。Assets/Data/Itemsの定義で調整。

    public string ItemId => itemId;
    public ItemKind Kind => kind;
    public ItemCategory Category => kind == ItemKind.Bug ? ItemCategory.Bug : ItemCategory.Fruit;
    public int ScoreDelta => scoreDelta;

    public ItemEffectType Effect => kind switch
    {
        ItemKind.Banana => ItemEffectType.BananaPeel,
        ItemKind.Chili => ItemEffectType.Chili,
        ItemKind.FeverFruit => ItemEffectType.Fever,
        _ => ItemEffectType.None
    };
}
