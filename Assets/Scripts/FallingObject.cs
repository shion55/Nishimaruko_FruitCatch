using UnityEngine;

public class FallingObject : MonoBehaviour
{
    [SerializeField] private float fallSpeed = 3f;

    private Camera mainCamera;

    void Start()
    {
        mainCamera = Camera.main;
    }

    void Update()
    {
        transform.position += Vector3.down * fallSpeed * Time.deltaTime;

        if (mainCamera != null)
        {
            float bottom = mainCamera.transform.position.y
                           - mainCamera.orthographicSize - 1f;

            if (transform.position.y < bottom)
            {
                Destroy(gameObject);
            }
        }
    }

    public void SetSpeed(float speed)
    {
        fallSpeed = speed;
    }
}
