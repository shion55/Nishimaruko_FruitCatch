using UnityEngine;

public class CatchableItem : MonoBehaviour
{
    [SerializeField] private int scoreDelta = 10;

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
