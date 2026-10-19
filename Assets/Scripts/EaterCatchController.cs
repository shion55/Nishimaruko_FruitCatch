using UnityEngine;

public class EaterCatchController : MonoBehaviour
{
    [SerializeField] private MicrophoneMouthController microphone;
    [SerializeField] private GameManager gameManager;

    private void Awake()
    {
        if (microphone == null)
            microphone = GetComponentInParent<MicrophoneMouthController>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryCatch(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryCatch(other);
    }

    private void TryCatch(Collider2D other)
    {
        if (microphone == null || gameManager == null)
            return;

        // ê∫ÇèoÇµÇƒÇ¢Ç»Ç¢Ç∆êHÇ◊ÇÁÇÍÇ»Ç¢
        if (!microphone.IsMouthOpen)
            return;

        CatchableItem item = other.GetComponent<CatchableItem>();

        if (item == null)
            return;

        if (item.TryConsume())
        {
            gameManager.AddScore(item.ScoreDelta);
        }
    }
}
