using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Unity.Pipeline.Scripting;

// 実際のFixedUpdateとTriggerを使い、画面外の独立した配置で確認します。
public static class FruitBounceVerification
{
    public static async Task<object> Run()
    {
        Require(Application.isPlaying && !MouseDebugInput.Enabled,
            "MainGameを再生し、マウスデバッグをOFFにしてください。");
        var pitch = UnityEngine.Object.FindAnyObjectByType<PitchFallSpeedController>();
        bool pitchEnabled = pitch != null && pitch.enabled;
        float oldFruitMultiplier = FallingObject.FruitMultiplier;
        float oldBugMultiplier = FallingObject.BugMultiplier;
        float oldFruitMax = FallingObject.FruitMaxSpeed;
        float oldBugMax = FallingObject.BugMaxSpeed;
        if (pitchEnabled) pitch.enabled = false;
        FallingObject.FruitMultiplier = 1f;
        FallingObject.FruitMaxSpeed = 4f;

        var fixture = new GameObject("FruitBounceVerification");
        fixture.SetActive(false);
        fixture.transform.position = new Vector3(1000f, 1000f);
        var fruitObjects = new List<GameObject>();
        var checks = new List<string>();
        try
        {
            var cameraObject = new GameObject("TestCamera", typeof(Camera));
            cameraObject.transform.SetParent(fixture.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.aspect = 16f / 9f;

            var microphone = fixture.AddComponent<MicrophoneAnalysis>();
            microphone.enabled = false;
            var manager = fixture.AddComponent<GameManager>();
            var mouthObject = new GameObject("TestMouth", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CapsuleCollider2D));
            mouthObject.transform.SetParent(fixture.transform, false);
            var mouthBody = mouthObject.GetComponent<Rigidbody2D>();
            mouthBody.bodyType = RigidbodyType2D.Kinematic;
            mouthBody.gravityScale = 0f;
            var mouth = mouthObject.GetComponent<CapsuleCollider2D>();
            mouth.isTrigger = true;
            mouth.direction = CapsuleDirection2D.Horizontal;
            mouth.size = new Vector2(0.8f, 0.5f);
            var eater = mouthObject.AddComponent<EaterCatchController>();
            var eaterSettings = new SerializedObject(eater);
            eaterSettings.FindProperty("microphone").objectReferenceValue = microphone;
            eaterSettings.FindProperty("gameManager").objectReferenceValue = manager;
            eaterSettings.FindProperty("stiffness").floatValue = 0f;
            eaterSettings.FindProperty("damping").floatValue = 0f;
            eaterSettings.ApplyModifiedPropertiesWithoutUndo();
            int notifications = 0;
            eater.ItemConsumed += _ => notifications++;
            fixture.SetActive(true);
            SetGain(microphone, 0f);
            Physics2D.SyncTransforms();

            var widthObject = new GameObject("WidthOnlyMouth", typeof(SpriteRenderer), typeof(CapsuleCollider2D));
            widthObject.SetActive(false);
            widthObject.transform.SetParent(fixture.transform, false);
            widthObject.transform.localPosition = new Vector3(0f, 5f);
            var widthCollider = widthObject.GetComponent<CapsuleCollider2D>();
            widthCollider.direction = CapsuleDirection2D.Horizontal;
            widthCollider.size = new Vector2(4f / 3f, 0.5f);
            widthCollider.isTrigger = true;
            var widthEater = widthObject.AddComponent<EaterCatchController>();
            var widthSettings = new SerializedObject(widthEater);
            widthSettings.FindProperty("microphone").objectReferenceValue = microphone;
            widthSettings.FindProperty("maxScale").floatValue = 1.5f;
            widthSettings.ApplyModifiedPropertiesWithoutUndo();
            widthObject.SetActive(true);
            Physics2D.SyncTransforms();
            float initialHeight = widthCollider.bounds.size.y;
            SetGain(microphone, 0.25f);
            await Advance(0.5f);
            Require(widthCollider.direction == CapsuleDirection2D.Horizontal &&
                    widthEater.CurrentMouthWidth > 1.9f &&
                    Mathf.Abs(widthCollider.bounds.size.y - initialHeight) < 0.0001f &&
                    widthObject.transform.localScale.y == 1f,
                "口を開いたときにカプセルの縦幅が変化しました。");
            SetGain(microphone, 0f);
            await Advance(0.5f);
            Require(widthEater.CurrentMouthWidth < 1.4f &&
                    Mathf.Abs(widthCollider.bounds.size.y - initialHeight) < 0.0001f,
                "口を閉じたときに横幅だけが戻りませんでした。");
            UnityEngine.Object.DestroyImmediate(widthObject);
            checks.Add("横向きカプセルは開閉で横幅だけが変化し、縦幅を維持する");

            FallingObject CreateFruit(float x, float y)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Fruit.prefab");
                var go = UnityEngine.Object.Instantiate(prefab,
                    fixture.transform.position + new Vector3(x, y), Quaternion.identity, fixture.transform);
                fruitObjects.Add(go);
                var falling = go.GetComponent<FallingObject>();
                var settings = new SerializedObject(falling);
                settings.FindProperty("gameplayCamera").objectReferenceValue = camera;
                settings.ApplyModifiedPropertiesWithoutUndo();
                falling.SetSpeed(3f);
                Physics2D.SyncTransforms();
                return falling;
            }

            var closed = CreateFruit(0f, 1.6f);
            await Advance(0.35f);
            Require(closed != null && !closed.IsRising && closed.Velocity.y < 0f && manager.Score == 0,
                "閉口中に弾き上げ・取得が発生しました。");
            UnityEngine.Object.DestroyImmediate(closed.gameObject);
            checks.Add("閉口中は果物を弾き上げず、取得もしない");

            SetGain(microphone, 0.2f);
            foreach (float offset in new[] { -0.4f, 0.4f, 0f })
            {
                mouth.size = new Vector2(0.8f, 0.5f);
                Physics2D.SyncTransforms();
                var fruit = CreateFruit(offset, 1.7f);
                await Until(() => fruit != null && fruit.IsRising, 2f);
                Require(manager.Score == 0 && notifications == 0, "反射時に得点や取得通知が発生しました。");
                Require(fruit.Velocity.y > 0f && Mathf.Abs(fruit.Velocity.x) <= 3.54f,
                    "反射の初速が最大45度の設定を超えました。");
                Require(offset == 0f ? Mathf.Abs(fruit.Velocity.x) < 0.001f : fruit.Velocity.x * offset > 0f,
                    "接触位置と反射方向が一致しません。");
                Vector2 velocity = fruit.Velocity;
                Require(!fruit.TryBounce(new Vector2(0f, 5f), mouth) && fruit.Velocity == velocity,
                    "上昇中に反射が再発動しました。");

                mouth.size = new Vector2(2.4f, 0.5f);
                Physics2D.SyncTransforms();
                await Advance(0.1f);
                Require(fruit != null && fruit.IsRising && manager.Score == 0,
                    "口を広げた際、上昇中の果物を取得しました。");
                if (offset == 0f)
                {
                    await Until(() => notifications == 1, 3f);
                    Require(manager.Score == 10 && manager.FruitCount == 1,
                        "反射後の再落下で一度だけ取得できませんでした。");
                }
                else
                    UnityEngine.Object.DestroyImmediate(fruit.gameObject);
            }
            checks.Add("口の左・右・中央で、左上・右上・真上へ反射する");
            checks.Add("反射で消費せず、上昇中の重複反射と取得を抑止する");
            checks.Add("口を広げると再落下した果物を一度だけ取得できる");

            mouth.size = new Vector2(0.8f, 0.5f);
            Physics2D.SyncTransforms();
            var pitched = CreateFruit(0f, 2.5f);
            FallingObject.FruitMultiplier = 0.25f;
            float originalY = pitched.GetComponent<Rigidbody2D>().position.y;
            Require(pitched.TryBounce(new Vector2(0f, 5f), mouth), "音程確認用の反射を開始できません。");
            await Advance(0.2f);
            Require(pitched.IsRising && pitched.GetComponent<Rigidbody2D>().position.y - originalY > 0.65f &&
                    pitched.Velocity.y > 2.4f,
                "高音の減速が上昇中の軌道に適用されました。");
            await Until(() => !pitched.IsRising, 2f);
            Require(Mathf.Abs(pitched.Velocity.y + 0.75f) < 0.001f,
                "下降時に音程の速度制御へ戻りませんでした。");
            FallingObject.FruitMultiplier = 1f;
            await Advance(0.06f);
            Require(Mathf.Abs(pitched.Velocity.y + 3f) < 0.001f,
                "下降中の音程変更が反映されませんでした。");
            checks.Add("上昇は音程に影響されず、下降へ転じると音程制御へ戻る");

            pitched.enabled = false;
            Vector2 frozen = pitched.GetComponent<Rigidbody2D>().position;
            await Advance(0.1f);
            Require(Vector2.Distance(frozen, pitched.GetComponent<Rigidbody2D>().position) < 0.001f,
                "落下停止後もRigidbody2Dが移動しました。");
            UnityEngine.Object.DestroyImmediate(pitched.gameObject);
            checks.Add("ゲーム終了時の停止処理で反射中・下降中の移動も停止する");

            foreach (float direction in new[] { -1f, 1f })
            {
                float halfItemWidth = 0.84f;
                float edge = direction * (camera.orthographicSize * camera.aspect - halfItemWidth);
                var wallFruit = CreateFruit(edge - direction * 0.02f, 0f);
                Require(wallFruit.TryBounce(new Vector2(direction * 4f, 4f), mouth), "壁確認の反射を開始できません。");
                await Advance(0.12f);
                var body = wallFruit.GetComponent<Rigidbody2D>();
                Require(Mathf.Abs(body.position.x - (1000f + edge)) < 0.01f && wallFruit.Velocity.x == 0f,
                    "画面端で横速度を止められませんでした。");
                await Until(() => !wallFruit.IsRising, 2f);
                float previousY = body.position.y;
                await Advance(0.08f);
                Require(body.position.y < previousY && Mathf.Abs(body.position.x - (1000f + edge)) < 0.01f,
                    "画面端に沿って下降しませんでした。");
                var collider = wallFruit.GetComponent<Collider2D>();
                Require(LayerMask.LayerToName(wallFruit.gameObject.layer) == "FallingItem" &&
                        (collider.excludeLayers.value & (1 << wallFruit.gameObject.layer)) != 0,
                    "落下物同士の接触が無効化されていません。");
                UnityEngine.Object.DestroyImmediate(wallFruit.gameObject);
            }
            checks.Add("左右の画面端で横速度を止め、画面内で壁に沿って下降する");
            checks.Add("落下物の専用レイヤーで、落下物同士の接触を無効化する");
            return new { passed = checks.Count, checks, score = manager.Score, notifications };
        }
        finally
        {
            foreach (var go in fruitObjects)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(fixture);
            if (pitchEnabled && pitch != null) pitch.enabled = true;
            FallingObject.FruitMultiplier = oldFruitMultiplier;
            FallingObject.BugMultiplier = oldBugMultiplier;
            FallingObject.FruitMaxSpeed = oldFruitMax;
            FallingObject.BugMaxSpeed = oldBugMax;
        }
    }

    private static async Task Advance(float seconds)
    {
        float end = Time.fixedTime + seconds;
        await Until(() => Time.fixedTime >= end, seconds + 2f);
    }

    private static async Task Until(Func<bool> condition, float timeout)
    {
        float deadline = Time.realtimeSinceStartup + timeout;
        await Wait.Until(() => condition() || Time.realtimeSinceStartup >= deadline);
        Require(condition(), "物理動作の確認がタイムアウトしました。");
    }

    private static void SetGain(MicrophoneAnalysis microphone, float gain)
    {
        typeof(MicrophoneAnalysis).GetProperty(nameof(MicrophoneAnalysis.Gain)).SetValue(microphone, gain);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
