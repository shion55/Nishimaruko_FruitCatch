
using UnityEngine;
using TMPro;
using System.Collections;

public class GameManager : MonoBehaviour
{
    [SerializeField] private TMP_Text scoreText;

    [SerializeField] private GameObject FinishPanel;
    [SerializeField] private TMP_Text FinalScoreText;

    [SerializeField] private TMP_Text ReturnCountDown;

    private int score = 0;
    [SerializeField] private int GameDuration = 60;

    public FaceMovementController facemovecontroller;
    public FallingItemSpawner spawner;
    private void Start()
    {
        UpdateScoreText();
        if(FinishPanel != null)
        {
            FinishPanel.SetActive(false);
        }
        if(ReturnCountDown != null)
        {
            ReturnCountDown.enabled = false;
        }

         StartCoroutine(FinishPlay());

        facemovecontroller.movefacetoggle = true;
        spawner.isFall = true;
    }

    public void AddScore(int amount)
    {
        score += amount;
        UpdateScoreText();
    }

    private void UpdateScoreText()
    {
        if (scoreText != null)
            scoreText.text = $"Score: {score}";
    }
     IEnumerator FinishPlay()
    {
        yield return new WaitForSeconds(GameDuration);
        FinishPanel.SetActive(true);
        facemovecontroller.movefacetoggle = false;
        spawner.isFall = false;
        if (FinalScoreText != null)
            FinalScoreText.text = $"Score: {score}";
        StartCoroutine(ReturnMenu());
    }

    IEnumerator ReturnMenu()
    {
        yield return new WaitForSeconds (5f);

        ReturnCountDown.enabled = true;

        for (int i = 5; i < 1; i--)
        {
            //yield return new WaitForSeconds(1f);
            Debug.Log(i);
            ReturnCountDown.text = i.ToString();
        }
    }
}
