
using UnityEngine;

public enum ItemCategory
{
    Fruit,
    Bug
}

public class CatchableItem : MonoBehaviour
{
    [Header("Item Information")]
    [SerializeField] private ItemCategory category = ItemCategory.Fruit;
    [SerializeField] private string itemId = "apple";

    [Header("Score")]
    [SerializeField] private int scoreDelta = 10;

    public ItemCategory Category => category;
    public string ItemId => itemId;
    public int ScoreDelta => scoreDelta;

    private bool consumed = false;

    public bool TryConsume()
    {
        if (consumed) return false;

        consumed = true;
        Destroy(gameObject);
        return true;
    }
}
