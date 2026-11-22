using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Unity.Pipeline.Scripting;

// 実際のFixedUpdateとColliderの重なりを、画面外の独立した配置で確認します。
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
        var items = new List<GameObject>();
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
            mouth.size = new Vector2(0.6f, 0.2f);
            var eater = mouthObject.AddComponent<EaterCatchController>();
            var eaterSettings = new SerializedObject(eater);
            eaterSettings.FindProperty("microphone").objectReferenceValue = microphone;
            eaterSettings.FindProperty("gameManager").objectReferenceValue = manager;
            eaterSettings.FindProperty("maxScale").floatValue = 1.5f;
            var openSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Image/honbansozai/mochi-shomenopen.png");
            var closedSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Image/honbansozai/mochi-shomen.png");
            eaterSettings.FindProperty("EatingImage").objectReferenceValue = openSprite;
            eaterSettings.FindProperty("ClosingImage").objectReferenceValue = closedSprite;
            var sensors = new List<BoxCollider2D>();
            foreach (int side in new[] { -1, 1 })
            {
                var sensorObject = new GameObject(side < 0 ? "LeftSensor" : "RightSensor", typeof(BoxCollider2D));
                sensorObject.transform.SetParent(mouthObject.transform, false);
                sensorObject.transform.localPosition = new Vector3(side * 0.375f, -0.2f);
                var sensor = sensorObject.GetComponent<BoxCollider2D>();
                sensor.size = new Vector2(0.15f, 0.2f);
                sensor.isTrigger = true;
                sensors.Add(sensor);
                eaterSettings.FindProperty(side < 0 ? "leftBounceSensor" : "rightBounceSensor").objectReferenceValue = sensor;
            }
            eaterSettings.ApplyModifiedPropertiesWithoutUndo();
            int notifications = 0;
            eater.ItemConsumed += _ => notifications++;
            fixture.SetActive(true);

            SetGain(microphone, 0.25f);
            await Advance(0.8f);
            Require(mouthObject.GetComponent<SpriteRenderer>().sprite == openSprite &&
                    Mathf.Abs(mouth.bounds.size.x - 0.9f) < 0.01f &&
                    Mathf.Abs(mouth.bounds.size.y - 0.2f) < 0.001f &&
                    Mathf.Abs(sensors[0].bounds.max.x - mouth.bounds.min.x) < 0.001f &&
                    Mathf.Abs(sensors[1].bounds.min.x - mouth.bounds.max.x) < 0.001f &&
                    sensors[0].bounds.max.y <= mouth.bounds.min.y + 0.001f,
                "最大開口幅・固定高さ・外側センサーの追従が不正です。");
            SetGain(microphone, 0f);
            await Until(() => Mathf.Abs(mouth.bounds.size.x - 0.6f) < 0.01f, 2f);
            Require(mouthObject.GetComponent<SpriteRenderer>().sprite == closedSprite &&
                    Mathf.Abs(mouth.bounds.size.x - 0.6f) < 0.01f &&
                    Mathf.Abs(mouth.bounds.size.y - 0.2f) < 0.001f,
                "閉口時の幅と高さが不正です。");
            checks.Add("元の一枚絵を開閉で切り替え、口幅0.6～0.9・高さ0.2と左右センサーの追従を保つ");
            eaterSettings.Update();
            eaterSettings.FindProperty("stiffness").floatValue = 0f;
            eaterSettings.ApplyModifiedPropertiesWithoutUndo();

            FallingObject CreateItem(float x, float y, string name = "Fruit")
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/" + name + ".prefab");
                var go = UnityEngine.Object.Instantiate(prefab,
                    fixture.transform.position + new Vector3(x, y), Quaternion.identity, fixture.transform);
                items.Add(go);
                var falling = go.GetComponent<FallingObject>();
                var settings = new SerializedObject(falling);
                settings.FindProperty("gameplayCamera").objectReferenceValue = camera;
                settings.ApplyModifiedPropertiesWithoutUndo();
                falling.SetSpeed(3f);
                Physics2D.SyncTransforms();
                return falling;
            }

            var closed = CreateItem(0f, 1.6f);
            var closedSide = CreateItem(1.2f, 1.6f);
            await Advance(0.7f);
            Require(closed != null && closedSide != null && !closed.IsRising && !closedSide.IsRising &&
                    manager.Score == 0 && notifications == 0,
                "閉口中に取得か弾き上げが発生しました。");
            UnityEngine.Object.DestroyImmediate(closed.gameObject);
            UnityEngine.Object.DestroyImmediate(closedSide.gameObject);
            checks.Add("閉口中は口・外側センサーのどちらでも取得・弾き上げを行わない");

            SetGain(microphone, 0.2f);
            var large = CreateItem(0f, 1.6f);
            Require(large.GetComponent<Collider2D>().bounds.size.x > mouth.bounds.size.x,
                "通常果物が口より大きい状態になっていません。");
            await Until(() => notifications == 1, 2f);
            Require(manager.Score == 10 && manager.FruitCount == 1, "口より大きい果物を取得できません。");
            checks.Add("口より幅の大きい通常果物も、接触すると取得する");

            var shared = CreateItem(0f, 0f);
            shared.SetSpeed(0f);
            bool bouncedBeforeCatch = true;
            void OnSharedCatch(ItemConsumption _) { bouncedBeforeCatch = shared.IsRising; }
            eater.ItemConsumed += OnSharedCatch;
            await Until(() => notifications == 2, 2f);
            eater.ItemConsumed -= OnSharedCatch;
            Require(!bouncedBeforeCatch && manager.Score == 20,
                "口と左右センサーへ同時に重なった果物で、取得が優先されませんでした。");
            checks.Add("口と左右センサーへ同時に触れる果物は、弾き上げず取得を優先する");

            foreach (int side in new[] { -1, 1 })
            {
                int before = notifications;
                var fruit = CreateItem(side * 1.2f, 1.6f);
                float originalX = fruit.GetComponent<Rigidbody2D>().position.x;
                for (int bounce = 0; bounce < 3; bounce++)
                {
                    await Until(() => fruit != null && fruit.IsRising, 2f);
                    Require(notifications == before && fruit.GetComponent<Rigidbody2D>().position.x == originalX,
                        "外側だけの接触で取得されたか、弾き上げで横移動しました。");
                    float speed = fruit.VerticalSpeed;
                    Require(!fruit.TryBounce(5f) && fruit.VerticalSpeed == speed,
                        "上昇中に初速が繰り返し設定されました。");
                    await Until(() => fruit != null && !fruit.IsRising, 2f);
                }
                mouthBody.position = fixture.transform.position + new Vector3(side * 1.2f, 0f);
                Physics2D.SyncTransforms();
                await Until(() => notifications == before + 1, 2f);
                mouthBody.position = fixture.transform.position;
                Physics2D.SyncTransforms();
            }
            checks.Add("左右とも真上へ繰り返し弾き、同じ果物を3回救済した後に再取得できる");
            checks.Add("上昇中の再弾き上げを抑え、下降後には回数制限なしで弾き直す");

            var rising = CreateItem(0f, 0.3f);
            Require(rising.TryBounce(5f), "上昇中の取得確認を開始できません。");
            await Until(() => notifications == 5, 2f);
            checks.Add("上昇中でも開いた口へ接触すれば取得できる");

            var bug = CreateItem(0.8f, 1.4f, "Bug");
            await Advance(0.8f);
            Require(bug != null && !bug.IsRising && manager.BugCount == 0 && notifications == 5,
                "外側センサーが虫を弾くか、虫を取得しました。");
            UnityEngine.Object.DestroyImmediate(bug.gameObject);
            checks.Add("虫は外側センサーでは取得・弾き上げを行わない");

            var pitched = CreateItem(12f, 2.5f);
            FallingObject.FruitMultiplier = 0.25f;
            Vector2 originalPosition = pitched.GetComponent<Rigidbody2D>().position;
            Require(pitched.TryBounce(5f), "音程確認用の弾き上げを開始できません。");
            await Advance(0.2f);
            Require(pitched.IsRising && pitched.GetComponent<Rigidbody2D>().position.y - originalPosition.y > 0.65f &&
                    pitched.VerticalSpeed > 2.4f && pitched.GetComponent<Rigidbody2D>().position.x == originalPosition.x,
                "上昇中の音程除外か、横位置の維持が不正です。");
            await Until(() => !pitched.IsRising, 2f);
            Require(Mathf.Abs(pitched.VerticalSpeed + 0.75f) < 0.001f, "下降時に音程制御へ戻りません。");
            FallingObject.FruitMultiplier = 1f;
            await Advance(0.06f);
            Require(Mathf.Abs(pitched.VerticalSpeed + 3f) < 0.001f, "下降中の音程変更が反映されません。");
            checks.Add("音程は上昇へ影響せず、下降時には通常の速度制御へ戻る");
            checks.Add("画面端への横位置補正を行わず、果物の横位置を維持する");

            pitched.enabled = false;
            Vector2 frozen = pitched.GetComponent<Rigidbody2D>().position;
            await Advance(0.1f);
            Require(pitched.GetComponent<Rigidbody2D>().position == frozen && pitched.VerticalSpeed == 0f,
                "落下停止後も移動しました。");
            checks.Add("ゲーム終了時の停止処理で弾き上げ・下降を停止する");

            var missed = CreateItem(4f, -5.9f);
            await Until(() => missed == null, 2f);
            checks.Add("取りこぼして画面下へ落ちた果物は削除する");
            return new { passed = checks.Count, checks, score = manager.Score, notifications };
        }
        finally
        {
            foreach (var go in items)
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
