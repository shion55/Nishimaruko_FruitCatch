using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FruitCatch.Preparation
{
    internal sealed class PreparationCrossfade : MonoBehaviour
    {
        private static PreparationCrossfade active;
        private Texture2D frame;
        private RawImage image;
        private string destination;
        private float sceneFadeDuration;
        private bool destinationLoaded;

        internal static bool IsSceneTransitionActive => active != null && active.destination != null;

        internal static PreparationCrossfade Create()
        {
            CancelActive();
            var root = new GameObject("PreparationCrossfade", typeof(RectTransform));
            active = root.AddComponent<PreparationCrossfade>();
            return active;
        }

        internal static void CancelActive()
        {
            if (active != null)
                active.Cancel();
        }

        internal IEnumerator Capture()
        {
            // Batch mode may never resume an end-of-frame coroutine.
            if (Application.isBatchMode)
                yield break;

            yield return new WaitForEndOfFrame();
            if (Screen.width <= 0 || Screen.height <= 0)
                yield break;

            // Use the game-view screenshot's orientation for the UI overlay.
            frame = ScreenCapture.CaptureScreenshotAsTexture();
            if (frame == null)
                yield break;

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            gameObject.AddComponent<GraphicRaycaster>();
            image = FruitCatchUi.Rect("PreviousFrame", transform, Vector2.zero, Vector2.one)
                .gameObject.AddComponent<RawImage>();
            image.texture = frame;
        }

        internal IEnumerator FadeOut(float duration)
        {
            if (image != null && duration > 0f)
            {
                float startedAt = Time.unscaledTime;
                while (Time.unscaledTime - startedAt < duration)
                {
                    float progress = (Time.unscaledTime - startedAt) / duration;
                    image.color = new Color(1f, 1f, 1f, 1f - Mathf.SmoothStep(0f, 1f, progress));
                    yield return null;
                }
            }
            Cancel();
        }

        internal void FadeIntoScene(string sceneName, float duration)
        {
            if (image == null)
            {
                Cancel();
                return;
            }

            destination = sceneName;
            sceneFadeDuration = duration;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single)
                return;
            bool isDestination = scene.name == destination || scene.path == destination ||
                scene.path == destination + ".unity";
            if (destinationLoaded || !isDestination)
            {
                Cancel();
                return;
            }

            destinationLoaded = true;
            StartCoroutine(RevealScene());
        }

        private IEnumerator RevealScene()
        {
            yield return null;
            yield return FadeOut(sceneFadeDuration);
        }

        internal void Cancel()
        {
            if (active == this)
                active = null;
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            if (active == this)
                active = null;
            if (frame != null)
                Destroy(frame);
        }
    }
}
