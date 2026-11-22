using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Unity.Pipeline.Scripting;

// Unity CLIのscript runから、再生中に呼び出す共通基盤の確認用です。
// Assets外に置き、ゲームのビルドには含めません。
public static class ItemFoundationVerification
{
    public static async Task<object> Run()
    {
        Require(Application.isPlaying, "MainGameを再生してから実行してください。");
        Require(!MouseDebugInput.Enabled, "F2でマウスデバッグをOFFにしてから実行してください。");

        var checks = new List<string>();
        VerifyTimers();
        checks.Add("一時効果は再発動で残り時間を更新し、加算しない");
        checks.Add("効果ごとのタイマーは独立し、失効・解除できる");

        GameObject fixture = new GameObject("ItemFoundationVerification");
        fixture.SetActive(false);
        fixture.transform.position = new Vector3(1000f, 1000f);
        try
        {
            var microphone = fixture.AddComponent<MicrophoneAnalysis>();
            microphone.enabled = false;
            var manager = fixture.AddComponent<GameManager>();
            var spawner = fixture.AddComponent<FallingItemSpawner>();
            spawner.enabled = false;
            spawner.SetSpawningEnabled(true);

            var eaterObject = new GameObject("TestEater", typeof(SpriteRenderer), typeof(CapsuleCollider2D));
            eaterObject.transform.SetParent(fixture.transform, false);
            var mouth = eaterObject.GetComponent<CapsuleCollider2D>();
            mouth.direction = CapsuleDirection2D.Horizontal;
            mouth.size = new Vector2(4f / 3f, 0.5f);
            var eater = eaterObject.AddComponent<EaterCatchController>();
            var eaterSettings = new SerializedObject(eater);
            eaterSettings.FindProperty("microphone").objectReferenceValue = microphone;
            eaterSettings.FindProperty("gameManager").objectReferenceValue = manager;
            eaterSettings.FindProperty("fallingItemSpawner").objectReferenceValue = spawner;
            eaterSettings.ApplyModifiedPropertiesWithoutUndo();

            var consumptions = new List<ItemConsumption>();
            eater.ItemConsumed += consumptions.Add;
            fixture.SetActive(true);
            eaterObject.transform.localScale = new Vector3(1.5f, 1f, 1f);
            Physics2D.SyncTransforms();

            var fruit = CreateItem("Fruit", fixture.transform, ItemKind.Fruit, 10);
            SetGain(microphone, 0f);
            await Advance();
            Require(!fruit.IsConsumed && manager.Score == 0 && consumptions.Count == 0,
                "閉口中に取得・得点・通知が発生しました。");
            checks.Add("閉口中は取得しない");

            SetGain(microphone, 0.2f);
            await Advance();
            Require(fruit.IsConsumed && manager.Score == 10 && manager.FruitCount == 1 && consumptions.Count == 1,
                "同じ果物の取得が重複しました。");
            Require(!fruit.TryConsume(), "消費済みのアイテムを再消費できました。");
            Require(consumptions[0].Kind == ItemKind.Fruit && consumptions[0].ItemId == "apple" &&
                    consumptions[0].ScoreDelta == 10 && consumptions[0].WorldPosition.x == 1000f,
                "取得結果に消費時点の情報が保存されていません。");
            checks.Add("取得・得点・通知は1アイテムにつき1回で、取得結果を保存する");

            var bug = CreateItem("Bug", fixture.transform, ItemKind.Bug, -5);
            float fruitSpeed = FallingObject.FruitMultiplier;
            float bugSpeed = FallingObject.BugMultiplier;
            await Advance();
            Require(manager.Score == 5 && manager.BugCount == 1 && consumptions.Count == 2 &&
                    consumptions[1].Effect == ItemEffectType.None && !spawner.IsFever &&
                    FallingObject.FruitMultiplier == fruitSpeed && FallingObject.BugMultiplier == bugSpeed,
                "虫の減点以外の状態が変化しました。");
            checks.Add("虫は1回だけ減点し、落下速度と効果を変更しない");

            var fever = CreateItem("FeverFruit", fixture.transform, ItemKind.FeverFruit, 10);
            await Advance();
            Require(manager.Score == 15 && manager.FruitCount == 2 && consumptions.Count == 3 &&
                    consumptions[2].Effect == ItemEffectType.Fever && spawner.IsFever &&
                    Mathf.Approximately(spawner.FeverRemainingTime, 8f),
                "フィーバー果物の取得結果が正しくありません。");
            spawner.StartFever();
            Require(Mathf.Approximately(spawner.FeverRemainingTime, 8f), "フィーバー時間が重複加算されました。");
            spawner.SetSpawningEnabled(false);
            spawner.StartFever();
            Require(!spawner.IsFever && spawner.FeverRemainingTime == 0f,
                "生成停止後にフィーバーが残るか、再開しました。");
            checks.Add("フィーバーは定義から発動し、再取得で更新、生成停止で解除する");

            var ignored = CreateItem("Fruit", fixture.transform, ItemKind.Fruit, 10);
            eater.enabled = false;
            await Advance();
            Require(!ignored.IsConsumed && manager.Score == 15 && consumptions.Count == 3,
                "無効な取得コントローラーで得点が変化しました。");
            eater.enabled = true;
            await Advance();
            Require(ignored.IsConsumed && manager.Score == 25 && manager.FruitCount == 3 && consumptions.Count == 4,
                "取得コントローラー再開後に取得できませんでした。");
            checks.Add("取得停止中は得点を変えず、再開後は取得できる");

            var undefinedObject = new GameObject("UndefinedItem", typeof(BoxCollider2D), typeof(CatchableItem));
            undefinedObject.transform.SetParent(fixture.transform, false);
            await Advance();
            Require(manager.Score == 25 && consumptions.Count == 4,
                "定義のないアイテムを取得できました。");
            checks.Add("定義のないアイテムを取得・加点しない");

            return new { passed = checks.Count, checks, score = manager.Score,
                fruits = manager.FruitCount, bugs = manager.BugCount, notifications = consumptions.Count };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static CatchableItem CreateItem(string name, Transform parent, ItemKind kind, int score)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/" + name + ".prefab");
        Require(prefab != null, "Prefabが見つかりません: " + name);
        GameObject instance = UnityEngine.Object.Instantiate(prefab, new Vector3(1000f, 1000f), Quaternion.identity, parent);
        var item = instance.GetComponent<CatchableItem>();
        Require(item != null && item.Definition != null && item.Kind == kind && item.ScoreDelta == score,
            "Prefabと定義の対応が不正です: " + name);
        instance.GetComponent<FallingObject>().enabled = false;
        return item;
    }

    private static async Task Advance()
    {
        Physics2D.SyncTransforms();
        float end = Time.fixedTime + 0.06f;
        await Wait.Until(() => Time.fixedTime >= end, timeoutMs: 3000);
    }

    private static void SetGain(MicrophoneAnalysis microphone, float gain)
    {
        // 無効化した計測器の入力だけを差し替え、実機マイクを起動しない。
        typeof(MicrophoneAnalysis).GetProperty(nameof(MicrophoneAnalysis.Gain),
            BindingFlags.Instance | BindingFlags.Public).SetValue(microphone, gain);
    }

    private static void VerifyTimers()
    {
        var chili = new TemporaryEffectTimer();
        var fever = new TemporaryEffectTimer();
        chili.Refresh(5f);
        chili.Tick(2f);
        fever.Refresh(8f);
        Require(chili.Remaining == 3f && fever.Remaining == 8f, "タイマーが独立していません。");
        chili.Refresh(5f);
        Require(chili.Remaining == 5f && fever.Remaining == 8f, "再発動で時間が加算されました。");
        chili.Tick(-1f);
        Require(chili.Remaining == 5f, "負の経過時間で効果時間が増えました。");
        chili.Tick(6f);
        Require(!chili.IsActive && chili.Remaining == 0f && fever.IsActive, "失効判定が不正です。");
        fever.Clear();
        Require(!fever.IsActive && fever.Remaining == 0f, "効果を解除できません。");
        chili.Refresh(-1f);
        Require(!chili.IsActive && chili.Remaining == 0f, "負の効果時間を受け付けました。");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
