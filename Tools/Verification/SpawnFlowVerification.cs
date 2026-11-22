using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Unity.Pipeline.Scripting;

// 再生中にCLIから実行。検証用オブジェクトはfinallyで片付ける。
public static class SpawnFlowVerification
{
    private sealed class SpawnRecord { public float time; public ItemKind kind; public Vector3 position; }
    private static readonly List<string> checks = new List<string>();

    public static async Task<object> Run()
    {
        Require(Application.isPlaying, "MainGameを再生してください。");
        var source = UnityEngine.Object.FindAnyObjectByType<FallingItemSpawner>();
        Require(source != null, "Spawnerが見つかりません。");
        checks.Clear();
        var randomState = UnityEngine.Random.state;
        try
        {
            VerifySchedule(source);
            VerifyRetryAndCap(source);
            await VerifyEffects(source);
            return new { passed = checks.Count, checks };
        }
        finally { UnityEngine.Random.state = randomState; }
    }

    public static async Task<object> VerifySession()
    {
        Require(Application.isPlaying, "MainGameを再生してください。");
        var session = UnityEngine.Object.FindAnyObjectByType<FruitCatchGameSession>();
        var spawner = UnityEngine.Object.FindAnyObjectByType<FallingItemSpawner>();
        var eater = UnityEngine.Object.FindAnyObjectByType<EaterCatchController>();
        Require(session != null && spawner != null, "MainGameが開かれていません。");
        // 観測開始を本編の先頭にそろえる。実際のセッション処理を再実行する。
        session.StopAllCoroutines();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(FruitCatchGameSession).GetMethod("EndGameplay", flags).Invoke(session, null);
        typeof(FruitCatchGameSession).GetMethod("Awake", flags).Invoke(session, null);
        session.StartCoroutine((System.Collections.IEnumerator)typeof(FruitCatchGameSession).GetMethod("Start", flags).Invoke(session, null));
        var phases = new List<object>();
        int lastPhase = -1;
        int feverFruits = 0;
        int peak = 0;
        float at41 = -1f;
        Action<CatchableItem> onSpawn = item => { if (item.Kind == ItemKind.FeverFruit) feverFruits++; };
        spawner.ItemSpawned += onSpawn;
        bool wasEnabled = eater.enabled;
        try
        {
            await Wait.Until(() => session.IsPlaying, timeoutMs: 5000);
            eater.enabled = false;
            await Wait.Until(() =>
            {
                peak = Mathf.Max(peak, spawner.ActiveItemCount);
                if (spawner.CurrentPhaseIndex != lastPhase)
                {
                    lastPhase = spawner.CurrentPhaseIndex;
                    phases.Add(new { time = session.ElapsedTime, phase = lastPhase, rate = spawner.CurrentFruitRate });
                }
                if (session.ElapsedTime >= 41f && at41 < 0f)
                {
                    at41 = session.ElapsedTime;
                    Require(!spawner.IsFever, "未取得なのにフィーバーになりました。");
                }
                return !session.IsPlaying;
            }, timeoutMs: 55000);
            Require(session.ElapsedTime == 50f && !spawner.SpawningEnabled && !spawner.IsFever &&
                spawner.ActiveItemCount == 0 && peak <= spawner.MaxActiveItems, "50秒の終了状態が不正です。");
            Require(phases.Count == 5 && feverFruits >= 1, "フェーズまたはフィーバー果物が不足しています: phases=" + phases.Count + ", fever=" + feverFruits);
            var movement = UnityEngine.Object.FindAnyObjectByType<CharacterPairController>();
            var effects = UnityEngine.Object.FindAnyObjectByType<ItemEffectController>();
            Require(!movement.IsSliding && !movement.IsChiliActive && !effects.HasBananaPeel, "終了後に特殊効果が残りました。");
            var results = (GameObject)new SerializedObject(session).FindProperty("resultsRoot").objectReferenceValue;
            await Wait.Until(() => results.activeSelf, timeoutMs: 5000);
            return new { duration = session.ElapsedTime, phases, at41, feverFruits, peak, results = results.activeSelf };
        }
        finally
        {
            if (spawner != null) spawner.ItemSpawned -= onSpawn;
            if (eater != null && session != null && session.IsPlaying) eater.enabled = wasEnabled;
        }
    }

    private static FallingItemSpawner Fixture(FallingItemSpawner source, out GameObject root)
    {
        root = new GameObject("SpawnFlowVerification");
        var spawner = root.AddComponent<FallingItemSpawner>();
        EditorUtility.CopySerialized(source, spawner);
        Set(spawner, "itemParent", root.transform);
        Set(spawner, "itemEffects", null);
        return spawner;
    }

    private static void VerifySchedule(FallingItemSpawner source)
    {
        for (int seed = 0; seed < 40; seed++)
        {
            UnityEngine.Random.InitState(seed);
            var spawner = Fixture(source, out GameObject root);
            try
            {
                var records = new List<SpawnRecord>();
                spawner.ItemSpawned += item =>
                {
                    records.Add(new SpawnRecord { time = spawner.ElapsedTime, kind = item.Kind, position = item.transform.position });
                    item.gameObject.SetActive(false);
                };
                spawner.BeginRun(50f);
                for (int i = 0; i <= 1000; i++) spawner.AdvanceTo(i * 0.05f);
                Require(records.Where(r => r.time < 10f).All(r => r.kind == ItemKind.Fruit), "導入に特殊枠が出ました。");
                foreach (var span in new[] { new Vector2(10f, 24f), new Vector2(24f, 37f) })
                {
                    var phase = records.Where(r => r.time >= span.x && r.time < span.y).ToArray();
                    Require(phase.Count(r => r.kind == ItemKind.Banana) <= 1 && phase.Count(r => r.kind == ItemKind.Chili) <= 1 &&
                        phase.Count(r => r.kind == ItemKind.Bug) <= 3, "特殊枠が予定数を超えました。");
                }
                var effects = records.Where(r => r.kind == ItemKind.Banana || r.kind == ItemKind.Chili || r.kind == ItemKind.FeverFruit).ToArray();
                for (int i = 1; i < effects.Length; i++)
                    Require(effects[i].time - effects[i - 1].time >= 3.999f, "効果アイテムの間隔が不足しています。");
                Require(records.Where(r => r.kind == ItemKind.FeverFruit).All(r => r.time >= 37f), "フィーバー果物の出現が早すぎます。");
                Require(!spawner.IsFever && !spawner.SpawningEnabled && spawner.ActiveItemCount == 0, "未取得の終了処理が不正です。");
                float limit = Camera.main.orthographicSize * Camera.main.aspect;
                Require(records.All(r => Mathf.Abs(r.position.x - Camera.main.transform.position.x) < limit), "画面外に生成しました。");
                Require(records.Count(r => r.kind == ItemKind.Fruit && r.time < 10f) >= 5, "通常果物が生成されていません。");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        checks.Add("40通りの乱数で導入・中盤の枠・効果間隔・画面内生成・未取得終了を確認");

        var boundary = Fixture(source, out GameObject boundaryRoot);
        try
        {
            boundary.BeginRun(50f);
            foreach (var pair in new[] { (0f, 0), (9.99f, 0), (10f, 1), (24f, 2), (37f, 3), (41f, 4) })
            {
                boundary.AdvanceTo(pair.Item1);
                Require(boundary.CurrentPhaseIndex == pair.Item2, "フェーズ境界が不正です。");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(boundaryRoot); }
        checks.Add("10・24・37・41秒の境界でフェーズが切り替わる");
    }

    private static void VerifyRetryAndCap(FallingItemSpawner source)
    {
        var spawner = Fixture(source, out GameObject root);
        try
        {
            CatchableItem fever = null;
            int feverCount = 0;
            spawner.ItemSpawned += item =>
            {
                if (item.Kind == ItemKind.FeverFruit) { fever = item; feverCount++; }
                else item.gameObject.SetActive(false);
            };
            spawner.BeginRun(50f);
            for (int i = 0; i <= 900; i++) spawner.AdvanceTo(i * 0.05f);
            Require(feverCount == 1 && fever != null && !spawner.IsFever, "フィーバー果物が重複するか、未取得で効果が出ました。");
            fever.gameObject.SetActive(false);
            for (int i = 901; i <= 920; i++) spawner.AdvanceTo(i * 0.05f);
            Require(feverCount == 2, "取り逃したフィーバー果物が再出現しません。");
            Require(fever.TryConsume(), "フィーバー果物を取得できません。");
            spawner.StartFever();
            Require(Mathf.Approximately(spawner.FeverRemainingTime, 4f), "46秒取得の残り時間が不正です。");
            spawner.AdvanceTo(48f);
            spawner.StartFever();
            Require(Mathf.Approximately(spawner.FeverRemainingTime, 2f) && feverCount == 2 &&
                Mathf.Approximately(spawner.CurrentFruitRate, 2.8f), "フィーバーが延長されるか、果物の頻度が変わりません。");
            spawner.AdvanceTo(50f);
            Require(!spawner.IsFever && spawner.SpawnBananaBurst(5) == 0 && spawner.ActiveItemCount == 0, "終了後に生成・効果が残ります。");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        checks.Add("未取得のまま45秒まで効果なし・同時1個・取り逃し後の再出現");
        checks.Add("46秒取得で残り4秒、再発動で延長せず50秒で生成と効果を終了");

        spawner = Fixture(source, out root);
        try
        {
            var settings = new SerializedObject(spawner);
            settings.FindProperty("maxActiveItems").intValue = 3;
            settings.ApplyModifiedPropertiesWithoutUndo();
            spawner.BeginRun(50f);
            spawner.AdvanceTo(2f);
            int burst = spawner.SpawnBananaBurst(100);
            Require(burst == 2 && spawner.ActiveItemCount == 3, "追加落下が空き枠を超えました。");
            spawner.StartFever();
            for (int i = 1; i < 900; i++) spawner.AdvanceTo(2f + i * 0.05f);
            Require(spawner.ActiveItemCount == 3 && spawner.SpawnBananaBurst(5) == 0, "減速・フィーバー中の共通上限を超えました。");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        checks.Add("滞留・フィーバー・大量追加落下にも共通上限を適用");
    }

    private static async Task VerifyEffects(FallingItemSpawner source)
    {
        var spawner = Fixture(source, out GameObject root);
        root.SetActive(false);
        var movement = root.AddComponent<CharacterPairController>();
        var carrierObject = new GameObject("TestCarrier", typeof(BoxCollider2D), typeof(Rigidbody2D));
        carrierObject.transform.SetParent(root.transform, false);
        carrierObject.transform.localPosition = new Vector3(0f, -4f, 0f);
        var carrier = carrierObject.GetComponent<BoxCollider2D>();
        carrier.size = new Vector2(0.8f, 1.5f);
        carrierObject.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        var eaterObject = new GameObject("TestEater", typeof(SpriteRenderer), typeof(CapsuleCollider2D));
        eaterObject.transform.SetParent(root.transform, false);
        eaterObject.transform.position = new Vector3(1000f, 1000f, 0f);
        var mic = eaterObject.AddComponent<MicrophoneAnalysis>();
        mic.enabled = false;
        var eater = eaterObject.AddComponent<EaterCatchController>();
        var manager = root.AddComponent<GameManager>();
        Set(eater, "microphone", mic);
        Set(eater, "gameManager", manager);
        Set(eater, "fallingItemSpawner", spawner);
        var effects = root.AddComponent<ItemEffectController>();
        Set(effects, "movement", movement);
        Set(effects, "carrier", carrier);
        Set(effects, "eater", eater);
        Set(effects, "spawner", spawner);
        Set(effects, "gameplayCamera", Camera.main);
        Set(effects, "peelPrefab", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/BananaPeel.prefab").GetComponent<BananaPeel>());
        Set(spawner, "itemEffects", effects);
        var spawned = new List<CatchableItem>();
        spawner.ItemSpawned += spawned.Add;
        try
        {
            root.SetActive(true);
            effects.BeginRun();
            spawner.BeginRun(50f);
            movement.SetMovementEnabled(true);
            typeof(MicrophoneAnalysis).GetProperty("Gain").SetValue(mic, 0.25f);
            var chili = SpawnAtMouth("Chili", eater.transform.position, root.transform);
            await Advance(0.1f);
            Require(chili == null || chili.IsConsumed, "唐辛子を口で取得できません。");
            Require(effects.IsChiliActive && manager.FruitCount == 1, "唐辛子の効果が発動しません。");
            float y = movement.transform.position.y;
            float x = movement.transform.position.x;
            await Advance(0.17f);
            Require(Mathf.Abs(movement.transform.position.x - x) > 0.01f && movement.transform.position.y == y,
                "自動揺れがないか、移動の高さが変化しました。");
            spawner.StartFever();
            Require(spawner.IsFever && effects.IsChiliActive, "唐辛子とフィーバーが併存しません。");
            checks.Add("口への接触で唐辛子が発動し、高さを保って自動揺れ・フィーバーと併存");

            var banana = SpawnAtMouth("Banana", eater.transform.position, root.transform);
            await Advance(0.1f);
            Require(banana == null || banana.IsConsumed, "バナナを取得できません。");
            Require(effects.HasBananaPeel, "皮が投げられません。");
            var peel = UnityEngine.Object.FindObjectsByType<BananaPeel>().Single();
            await Wait.Until(() => peel.HasLanded, timeoutMs: 3000);
            float landedX = peel.transform.position.x;
            Require(Mathf.Abs(landedX - Camera.main.transform.position.x) > 1f, "皮が左右に着地しません。");
            Vector3 p = movement.transform.position;
            p.x = landedX;
            movement.transform.position = p;
            Physics2D.SyncTransforms();
            await Wait.Until(() => movement.IsSliding, timeoutMs: 3000);
            Require(!effects.HasBananaPeel && effects.IsChiliActive, "踏んだ皮の消費または唐辛子タイマーの維持が不正です。");
            int before = spawned.Count;
            await Wait.Until(() => !movement.IsSliding, timeoutMs: 3000);
            Require(spawned.Count - before == 5, "壁到達時の追加生成が一度だけ発生しません。");
            Require(Mathf.Sign(movement.transform.position.x - Camera.main.transform.position.x) == Mathf.Sign(landedX - Camera.main.transform.position.x),
                "皮と反対側の壁へ滑りました。");
            Require(effects.IsChiliActive, "滑走後に唐辛子の残り時間が失われました。");
            await Advance(0.2f);
            Require(spawned.Count - before == 5, "壁の追加落下が繰り返されました。");
            checks.Add("皮の投擲・着地・踏み判定・皮側の壁への滑走・一度だけの追加落下");
            checks.Add("滑走中も唐辛子の時間が進み、滑走終了後に残り効果へ復帰");
            effects.StopEffects();
            movement.SetMovementEnabled(false);
            spawner.SetSpawningEnabled(false);
            await Advance(0.1f);
            Require(!effects.HasBananaPeel && !movement.IsSliding && !movement.IsChiliActive &&
                !spawner.IsFever && spawner.ActiveItemCount == 0, "停止後に効果や落下物が残りました。");
            checks.Add("終了処理で皮・滑走・唐辛子・フィーバー・落下物を片付ける");
        }
        finally
        {
            effects.StopEffects();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static CatchableItem SpawnAtMouth(string name, Vector3 position, Transform parent)
    {
        var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/" + name + ".prefab"), position, Quaternion.identity, parent);
        go.GetComponent<FallingObject>().enabled = false;
        return go.GetComponent<CatchableItem>();
    }

    private static async Task Advance(float seconds)
    {
        Physics2D.SyncTransforms();
        float end = Time.fixedTime + seconds;
        await Wait.Until(() => Time.fixedTime >= end, timeoutMs: 4000);
    }

    private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
    {
        var settings = new SerializedObject(target);
        settings.FindProperty(field).objectReferenceValue = value;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
