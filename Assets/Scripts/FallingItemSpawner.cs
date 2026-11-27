using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public enum SpawnPlacement { Center, WideAlternating, AlternatingPairs, Easy, FullWidth }

[Serializable]
public sealed class SpawnPhaseSettings
{
    public string label;
    [Min(0f)] public float endTime; // 本編開始からの終了時刻（秒）。
    [Min(0.01f)] public float fruitsPerSecond;
    public SpawnPlacement placement;
    [Min(0)] public int bananaSlots;
    [Min(0)] public int chiliSlots;
    [Min(0)] public int bugSlots;

    public SpawnPhaseSettings(string label, float end, float rate, SpawnPlacement placement,
        int bananas = 0, int chilies = 0, int bugs = 0)
    {
        this.label = label;
        endTime = end;
        fruitsPerSecond = rate;
        this.placement = placement;
        bananaSlots = bananas;
        chiliSlots = chilies;
        bugSlots = bugs;
    }
}

public class FallingItemSpawner : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private GameObject[] fruitPrefabs;
    [SerializeField] private GameObject bugPrefab;
    [SerializeField] private GameObject bananaPrefab;
    [SerializeField] private GameObject chiliPrefab;
    [SerializeField] private GameObject feverItemPrefab;
    [SerializeField] private Transform itemParent;
    [SerializeField] private ItemEffectController itemEffects;

    [Header("50 Second Flow")]
    [SerializeField] private SpawnPhaseSettings[] phases =
    {
        new SpawnPhaseSettings("導入", 10f, 0.8f, SpawnPlacement.Center),
        new SpawnPhaseSettings("ハプニング", 24f, 1.1f, SpawnPlacement.WideAlternating, 1, 1, 3),
        new SpawnPhaseSettings("協力", 37f, 1.4f, SpawnPlacement.AlternatingPairs, 1, 1, 3),
        new SpawnPhaseSettings("フィーバー導入", 41f, 1f, SpawnPlacement.Easy),
        new SpawnPhaseSettings("フィナーレ", 50f, 1f, SpawnPlacement.Easy)
    };
    [SerializeField, Min(1)] private int maxActiveItems = 48; // 弾き上げ・追加落下を含む共通上限（個）。
    [SerializeField] private Vector2 effectItemSpacing = new Vector2(4f, 5f); // 効果アイテムの生成間隔（秒）。
    [SerializeField, Min(0f)] private float specialItemSpacing = 1f; // 虫を含む特殊枠の最小間隔（秒）。
    [SerializeField] private Vector2 firstFeverWindow = new Vector2(37f, 38f); // 初回フィーバー果物の予定時刻（秒）。
    [SerializeField, Min(0.01f)] private float feverFruitsPerSecond = 10f; // フィーバー中の平均生成数（個/秒）。
    [SerializeField] private Vector2 intervalJitter = new Vector2(0.8f, 1.2f); // 平均間隔に掛けるランダム倍率。
    [SerializeField] private float minFallSpeed = 2f; // 下降速度（units/秒）。
    [SerializeField] private float maxFallSpeed = 4f;

    private sealed class Slot
    {
        public ItemKind kind;
        public float due;
        public float deadline;
    }

    private readonly List<Slot> slots = new List<Slot>();
    private readonly List<CatchableItem> activeItems = new List<CatchableItem>();
    private readonly List<CatchableItem> normalFruitPrefabs = new List<CatchableItem>();
    private Camera mainCamera;
    private float runDuration = 50f;
    private float nextFruitTime;
    private float nextEffectTime;
    private float nextSpecialTime;
    private float nextFeverTime;
    private bool fever;
    private bool feverUsed;
    private float[] phaseEnds;
    private int placementIndex;
    private float previousX;

    public bool SpawningEnabled { get; private set; }
    public float ElapsedTime { get; private set; }
    public int CurrentPhaseIndex { get; private set; }
    public bool IsFever => SpawningEnabled && fever;
    public float FeverRemainingTime => IsFever ? Mathf.Max(0f, runDuration - ElapsedTime) : 0f;
    public float CurrentFruitRate => IsFever ? feverFruitsPerSecond : CurrentPhase.fruitsPerSecond;
    public int ActiveItemCount { get { PruneItems(); return activeItems.Count; } }
    public int MaxActiveItems => maxActiveItems;
    internal IReadOnlyList<CatchableItem> NormalFruitPrefabs => normalFruitPrefabs;
    internal SpawnPhaseSettings[] OperatorPhases => phases;

    internal void BindOperatorItems()
    {
        if (fruitPrefabs != null)
            foreach (GameObject prefab in fruitPrefabs)
                Bind(prefab);
        Bind(bugPrefab);
        Bind(bananaPrefab);
        Bind(chiliPrefab);
        Bind(feverItemPrefab);

        static void Bind(GameObject prefab)
        {
            if (prefab != null && prefab.TryGetComponent(out CatchableItem item))
                _ = item.Definition;
        }
    }
    public event Action<CatchableItem> ItemSpawned;
    public event Action FeverStarted;

    private SpawnPhaseSettings CurrentPhase => phases[CurrentPhaseIndex];

    internal float GetAveragePitchSlowdown()
    {
        float sum = 0f;
        int count = 0;
        foreach (CatchableItem item in activeItems)
        {
            if (item == null || item.IsConsumed ||
                !item.TryGetComponent(out FallingObject falling) || !falling.isActiveAndEnabled || falling.IsRising)
                continue;
            sum += falling.RelativePitchSlowdown;
            count++;
        }
        return count > 0 ? sum / count : 0f;
    }

    public void BeginRun(float duration)
    {
        SetSpawningEnabled(false);
        mainCamera = Camera.main;
        runDuration = Mathf.Max(0f, duration);
        ElapsedTime = 0f;
        CurrentPhaseIndex = 0;
        placementIndex = 0;
        previousX = 0f;
        nextEffectTime = nextSpecialTime = 0f;
        float latestSpawn = Mathf.Max(0, runDuration - .05f);
        nextFeverTime = Random.Range(Mathf.Min(firstFeverWindow.x, latestSpawn),
            Mathf.Min(firstFeverWindow.y, latestSpawn));
        phaseEnds = new float[phases.Length];
        for (int i = 0; i < phases.Length; i++)
            phaseEnds[i] = Mathf.Clamp(phases[i].endTime, i == 0 ? 0f : phaseEnds[i - 1], runDuration);
        normalFruitPrefabs.Clear();
        if (fruitPrefabs != null)
            foreach (GameObject prefab in fruitPrefabs)
                if (prefab != null && prefab.TryGetComponent(out CatchableItem item) &&
                    item.Definition != null && item.Kind == ItemKind.Fruit)
                    normalFruitPrefabs.Add(item);
        BuildSlots();
        SpawningEnabled = runDuration > 0f;
        nextFruitTime = GetFruitInterval();
    }

    // セッションの時計で進行する。落下の減速や得点から独立させる。
    public void AdvanceTo(float elapsed)
    {
        if (!SpawningEnabled)
            return;

        ElapsedTime = Mathf.Clamp(elapsed, ElapsedTime, runDuration);
        if (ElapsedTime >= runDuration)
        {
            SetSpawningEnabled(false);
            return;
        }

        int phase = 0;
        while (phase < phases.Length - 1 && ElapsedTime >= phaseEnds[phase])
            phase++;
        if (CurrentPhaseIndex != phase)
        {
            CurrentPhaseIndex = phase;
            placementIndex = 0;
            nextFruitTime = ElapsedTime + GetFruitInterval();
        }

        PruneItems();
        TrySpawnSpecial();
        if (ElapsedTime >= nextFruitTime)
        {
            SpawnItem(RandomFruit(), IsFever ? SpawnPlacement.FullWidth : CurrentPhase.placement);
            // 上限やフレーム遅延で見送った分は蓄積しない。
            nextFruitTime = ElapsedTime + GetFruitInterval();
        }
    }

    private void BuildSlots()
    {
        slots.Clear();
        float start = 0f;
        for (int iPhase = 0; iPhase < phases.Length; iPhase++)
        {
            SpawnPhaseSettings phase = phases[iPhase];
            float end = phaseEnds[iPhase];
            if (end <= start) continue;
            var kinds = new List<ItemKind>();
            for (int i = 0; i < phase.bananaSlots; i++) kinds.Add(ItemKind.Banana);
            for (int i = 0; i < phase.chiliSlots; i++) kinds.Add(ItemKind.Chili);
            for (int i = kinds.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
            }
            float gap = RandomRange(effectItemSpacing);
            float latestFirst = end - 1f - gap * Mathf.Max(0, kinds.Count - 1);
            float time = Random.Range(start + 0.5f, Mathf.Max(start + 0.5f, latestFirst));
            foreach (ItemKind kind in kinds)
            {
                slots.Add(new Slot { kind = kind, due = time, deadline = end });
                time += gap;
            }
            for (int i = 0; i < phase.bugSlots; i++)
            {
                float fraction = (i + Random.Range(0.2f, 0.8f)) / phase.bugSlots;
                slots.Add(new Slot { kind = ItemKind.Bug,
                    due = Mathf.Lerp(start + 0.5f, end - 1f, fraction), deadline = end });
            }
            start = end;
        }
        slots.Sort((a, b) => a.due.CompareTo(b.due));
    }

    private void TrySpawnSpecial()
    {
        slots.RemoveAll(s => ElapsedTime >= s.deadline);
        if (IsFever || ElapsedTime < nextSpecialTime || ActiveItemCount >= maxActiveItems)
            return;

        // フィーバー果物の枠は取得まで存続し、常に1個だけを落とす。
        if (!feverUsed && ElapsedTime >= nextFeverTime && ElapsedTime >= nextEffectTime && !HasItem(ItemKind.FeverFruit))
        {
            if (SpawnItem(feverItemPrefab, SpawnPlacement.Easy) != null)
                RecordSpecial(ItemKind.FeverFruit);
            return;
        }

        if (itemEffects != null && itemEffects.IsChiliActive)
            return;
        for (int i = 0; i < slots.Count; i++)
        {
            Slot slot = slots[i];
            if (slot.due > ElapsedTime)
                break;
            if (slot.kind != ItemKind.Bug && ElapsedTime < nextEffectTime)
                continue;
            if (slot.kind == ItemKind.Banana &&
                (HasItem(ItemKind.Banana) || (itemEffects != null && itemEffects.HasBananaPeel)))
                continue;
            GameObject prefab = slot.kind == ItemKind.Bug ? bugPrefab :
                slot.kind == ItemKind.Banana ? bananaPrefab : chiliPrefab;
            if (SpawnItem(prefab, CurrentPhase.placement) == null)
                continue;
            slots.RemoveAt(i);
            RecordSpecial(slot.kind);
            break;
        }
    }

    private void RecordSpecial(ItemKind kind)
    {
        nextSpecialTime = ElapsedTime + Mathf.Max(0f, specialItemSpacing);
        if (kind != ItemKind.Bug)
            nextEffectTime = ElapsedTime + RandomRange(effectItemSpacing);
        if (kind == ItemKind.FeverFruit)
            nextFeverTime = nextEffectTime;
    }

    private bool HasItem(ItemKind kind)
    {
        foreach (CatchableItem item in activeItems)
            if (item != null && !item.IsConsumed && item.Kind == kind)
                return true;
        return false;
    }

    private void PruneItems()
    {
        activeItems.RemoveAll(i => i == null || i.IsConsumed || !i.gameObject.activeInHierarchy);
    }

    private float GetFruitInterval()
    {
        return Mathf.Max(0.05f, RandomRange(intervalJitter) / Mathf.Max(0.01f, CurrentFruitRate));
    }

    private static float RandomRange(Vector2 range)
    {
        return Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
    }

    private GameObject RandomFruit()
    {
        if (normalFruitPrefabs.Count == 0)
            return null;

        bool isFever = IsFever;
        double total = 0;
        foreach (CatchableItem fruit in normalFruitPrefabs)
            total += fruit.Definition.GetSpawnWeight(isFever);
        // If all weights are zero, keep the previous uniform distribution and spawn cadence.
        if (total <= 0)
            return normalFruitPrefabs[Random.Range(0, normalFruitPrefabs.Count)].gameObject;

        double draw = Random.value * total;
        CatchableItem lastEligible = null;
        foreach (CatchableItem fruit in normalFruitPrefabs)
        {
            float weight = fruit.Definition.GetSpawnWeight(isFever);
            if (weight <= 0)
                continue;
            lastEligible = fruit;
            draw -= weight;
            if (draw < 0)
                return fruit.gameObject;
        }
        return lastEligible.gameObject;
    }

    private CatchableItem SpawnItem(GameObject prefab, SpawnPlacement placement)
    {
        if (!SpawningEnabled || prefab == null || ActiveItemCount >= maxActiveItems)
            return null;
        if (mainCamera == null)
            mainCamera = Camera.main;
        if (mainCamera == null)
            return null;

        Sprite sprite = prefab.TryGetComponent(out CatchableItem template) ? template.DisplaySprite : null;
        float margin = sprite != null
            ? Mathf.Max(0.5f, sprite.bounds.extents.x * Mathf.Abs(prefab.transform.localScale.x)) : 0.5f;
        float limit = Mathf.Max(0f, mainCamera.orthographicSize * mainCamera.aspect - margin);
        float x;
        switch (placement)
        {
            case SpawnPlacement.Center:
                x = Random.Range(-limit * 0.35f, limit * 0.35f);
                break;
            case SpawnPlacement.WideAlternating:
                x = (placementIndex % 2 == 0 ? -1f : 1f) * Random.Range(limit * 0.3f, limit * 0.75f);
                break;
            case SpawnPlacement.AlternatingPairs:
                x = placementIndex % 2 == 0
                    ? (placementIndex % 4 == 0 ? -1f : 1f) * Random.Range(limit * 0.3f, limit * 0.7f)
                    : Mathf.Clamp(previousX + Random.Range(-0.8f, 0.8f), -limit, limit);
                break;
            case SpawnPlacement.FullWidth:
                x = Random.Range(-limit, limit);
                break;
            default:
                float target = itemEffects != null ? itemEffects.CharacterX - mainCamera.transform.position.x : 0f;
                x = Mathf.Clamp(target + Random.Range(-limit * 0.2f, limit * 0.2f), -limit, limit);
                break;
        }
        previousX = x;
        placementIndex++;
        Vector3 position = new Vector3(mainCamera.transform.position.x + x,
            mainCamera.transform.position.y + mainCamera.orthographicSize + 1f, 0f);
        GameObject go = Instantiate(prefab, position, Quaternion.identity, itemParent);
        var item = go.GetComponent<CatchableItem>();
        if (item == null)
        {
            Destroy(go);
            return null;
        }
        activeItems.Add(item);
        var falling = go.GetComponent<FallingObject>();
        if (falling != null)
            falling.SetSpeed(Random.Range(Mathf.Min(minFallSpeed, maxFallSpeed), Mathf.Max(minFallSpeed, maxFallSpeed)));
        ItemSpawned?.Invoke(item);
        return item;
    }

    public int SpawnBananaBurst(int count)
    {
        int spawned = 0;
        for (int i = 0; i < count && SpawningEnabled && ActiveItemCount < maxActiveItems; i++)
            if (SpawnItem(RandomFruit(), SpawnPlacement.FullWidth) != null) spawned++;
        return spawned;
    }

    public void StartFever(bool force = false)
    {
        if (!SpawningEnabled || fever || (!force && feverUsed) || ElapsedTime >= runDuration)
            return;
        fever = feverUsed = true;
        nextFruitTime = ElapsedTime;
        FeverStarted?.Invoke();
    }

    public void EndFever()
    {
        if (!IsFever)
            return;
        fever = false;
        nextFruitTime = ElapsedTime + GetFruitInterval();
    }

    public void SetSpawningEnabled(bool value)
    {
        if (value) { BeginRun(runDuration); return; }
        SpawningEnabled = false;
        fever = feverUsed = false;
        slots.Clear();
        foreach (CatchableItem item in activeItems)
        {
            if (item == null) continue;
            item.gameObject.SetActive(false);
            Destroy(item.gameObject);
        }
        activeItems.Clear();
    }

    private void OnDisable()
    {
        SetSpawningEnabled(false);
    }
}
