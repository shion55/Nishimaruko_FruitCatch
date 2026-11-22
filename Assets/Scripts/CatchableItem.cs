
using UnityEngine;

public readonly struct ItemConsumption
{
    public ItemKind Kind { get; }
    public ItemCategory Category { get; }
    public string ItemId { get; }
    public int ScoreDelta { get; }
    public ItemEffectType Effect { get; }
    public Vector3 WorldPosition { get; }

    public ItemConsumption(ItemDefinition definition, Vector3 worldPosition)
    {
        Kind = definition.Kind;
        Category = definition.Category;
        ItemId = definition.ItemId;
        ScoreDelta = definition.ScoreDelta;
        Effect = definition.Effect;
        WorldPosition = worldPosition;
    }
}

public class CatchableItem : MonoBehaviour
{
    [SerializeField] private ItemDefinition definition;

    public ItemDefinition Definition => definition;
    public ItemKind Kind => definition != null ? definition.Kind : ItemKind.Fruit;
    public ItemCategory Category => definition != null ? definition.Category : ItemCategory.Fruit;
    public string ItemId => definition != null ? definition.ItemId : string.Empty;
    public int ScoreDelta => definition != null ? definition.ScoreDelta : 0;
    public bool IsConsumed => consumed;

    private bool consumed = false;

    public bool TryConsume()
    {
        return TryConsume(out _);
    }

    public bool TryConsume(out ItemConsumption consumption)
    {
        consumption = default;
        if (consumed || definition == null)
            return false;

        consumed = true;
        consumption = new ItemConsumption(definition, transform.position);
        Destroy(gameObject);
        return true;
    }
}
