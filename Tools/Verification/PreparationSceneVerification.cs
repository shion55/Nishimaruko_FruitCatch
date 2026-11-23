using System;
using System.Threading.Tasks;
using FruitCatch.Preparation;
using UnityEngine;
using UnityEngine.UI;

// Run through Pipeline's `script run` in Play mode, starting at Preparation step 1.
public static class PreparationSceneVerification
{
    public static async Task<string> Run()
    {
        var flow = UnityEngine.Object.FindFirstObjectByType<PreparationFlowController>();
        if (flow == null || flow.CurrentStepIndex != 0)
            throw new Exception("Expected Preparation at step 1.");
        var cameraImage = GameObject.Find("CameraLayer").GetComponent<RawImage>();
        Rect cameraUv = cameraImage.uvRect;
        Vector3 cameraPosition = cameraImage.transform.position;
        Vector2 cameraSize = cameraImage.rectTransform.rect.size;
        flow.AdvanceForDebug();
        await Task.Delay(1000);
        flow.AdvanceForDebug();
        await Task.Delay(1000);
        var presentation = flow.CurrentPanel.GetComponentInChildren<PreparationFacePresentation>();
        if (presentation == null || flow.CurrentStepIndex != 2)
            throw new Exception("Face presentation was not initialized.");
        Canvas.ForceUpdateCanvases();
        Rect guideUv = presentation.GetGuideUv();
        if (Mathf.Abs(guideUv.center.x - 0.27f) > 0.002f || Mathf.Abs(guideUv.center.y - 0.42f) > 0.002f)
            throw new Exception("Guide coordinates do not match the visible oval: " + guideUv);
        if (GameObject.Find("OvalCameraCutout") == null || GameObject.Find("OvalOutline") == null)
            throw new Exception("Oval UI was not created.");
        foreach (var graphic in flow.CurrentPanel.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic.GetComponent<CanvasRenderer>() == null)
                throw new Exception("Missing CanvasRenderer on " + graphic.name);
        }
        Texture2D fallback = null;
        var frame = cameraImage.texture as Texture2D;
        if (frame == null)
        {
            fallback = new Texture2D(16, 16);
            var pixels = new Color[256];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.gray;
            fallback.SetPixels(pixels);
            fallback.Apply();
            frame = fallback;
        }
        presentation.Capture(frame);
        presentation.SetStatus("HOLD", 0.75f);
        Canvas.ForceUpdateCanvases();
        ScreenCapture.CaptureScreenshot("Temp/preparation-guide.png");
        await Task.Delay(500);
        flow.AdvanceForDebug();
        await Task.Delay(750);
        if (flow.CurrentStepIndex != 2 || !flow.IsTransitioning)
            throw new Exception("Flow did not wait for the transformation.");
        ScreenCapture.CaptureScreenshot("Temp/preparation-transform.png");
        await Task.Delay(3000);
        if (flow.CurrentStepIndex != 3)
            throw new Exception("Flow did not advance after the transformation.");
        if (cameraImage.uvRect != cameraUv || cameraImage.transform.position != cameraPosition ||
            cameraImage.rectTransform.rect.size != cameraSize)
            throw new Exception("Camera presentation changed.");
        flow.GoBackForDebug();
        await Task.Delay(1000);
        presentation = flow.CurrentPanel.GetComponentInChildren<PreparationFacePresentation>();
        if (presentation == null || !GameObject.Find("OvalOutline").activeInHierarchy)
            throw new Exception("Re-entry did not reset the presentation.");
        if (fallback != null) UnityEngine.Object.Destroy(fallback);
        return "PASS: oval geometry, snapshot, transformation gating, next step, re-entry, unchanged camera.";
    }
}
