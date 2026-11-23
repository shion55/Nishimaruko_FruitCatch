using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// CLIのscript runで実行する、出現フローのアセット・シーン設定。
public static class SpawnFlowSetup
{
    public static object Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Edit Modeで実行してください。");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/MainGame.unity" || scene.isDirty)
            throw new InvalidOperationException("保存済みのMainGameを開いて実行してください。");

        GameObject strawberry = MakeItem("Strawberry", "strawberry", ItemKind.Fruit, "fruit_strawberry.png");
        GameObject grape = MakeItem("Grape", "grape", ItemKind.Fruit, "fruit_grape.png");
        GameObject banana = MakeItem("Banana", "banana", ItemKind.Banana, "fruit_banana.png");
        GameObject chili = MakeItem("Chili", "chili", ItemKind.Chili, "tougarashi_redpepper.png");
        BananaPeel peel = MakePeel();
        ConfigureFeverFruit();

        var spawner = UnityEngine.Object.FindAnyObjectByType<FallingItemSpawner>();
        var session = UnityEngine.Object.FindAnyObjectByType<FruitCatchGameSession>();
        var movement = UnityEngine.Object.FindAnyObjectByType<CharacterPairController>();
        var eater = UnityEngine.Object.FindAnyObjectByType<EaterCatchController>();
        var effects = spawner.GetComponent<ItemEffectController>();
        if (effects == null) effects = spawner.gameObject.AddComponent<ItemEffectController>();
        var carrier = GameObject.Find("Carrier").GetComponent<Collider2D>();
        Set(effects, "movement", movement);
        Set(effects, "carrier", carrier);
        Set(effects, "eater", eater);
        Set(effects, "spawner", spawner);
        Set(effects, "peelPrefab", peel);
        Set(effects, "gameplayCamera", Camera.main);

        Set(spawner, "itemEffects", effects);
        Set(spawner, "bananaPrefab", banana);
        Set(spawner, "chiliPrefab", chili);
        var settings = new SerializedObject(spawner);
        var fruits = settings.FindProperty("fruitPrefabs");
        fruits.arraySize = 3;
        fruits.GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Fruit.prefab");
        fruits.GetArrayElementAtIndex(1).objectReferenceValue = strawberry;
        fruits.GetArrayElementAtIndex(2).objectReferenceValue = grape;
        settings.ApplyModifiedPropertiesWithoutUndo();
        Set(session, "characterMovement", movement);
        Set(session, "itemEffects", effects);
        Set(session, "itemSpawner", spawner);
        Set(session, "eaterController", eater);
        Set(session, "gameManager", UnityEngine.Object.FindAnyObjectByType<GameManager>());
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return new { scene = scene.path, fruits = fruits.arraySize, specialPrefabs = new[] { banana.name, chili.name, peel.name } };
    }

    private static ItemDefinition Definition(string name, string id, ItemKind kind)
    {
        string path = "Assets/Data/Items/" + name + ".asset";
        var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<ItemDefinition>();
            AssetDatabase.CreateAsset(definition, path);
        }
        var settings = new SerializedObject(definition);
        settings.FindProperty("itemId").stringValue = id;
        settings.FindProperty("kind").enumValueIndex = (int)kind;
        settings.FindProperty("scoreDelta").intValue = 10;
        settings.ApplyModifiedPropertiesWithoutUndo();
        return definition;
    }

    private static GameObject MakeItem(string name, string id, ItemKind kind, string spriteName)
    {
        var template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Fruit.prefab");
        var go = UnityEngine.Object.Instantiate(template);
        try
        {
            go.name = name;
            go.transform.position = Vector3.zero;
            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Image/karisozai/" + spriteName);
            renderer.color = Color.white;
            go.transform.localScale = Vector3.one * (1.6f / renderer.sprite.bounds.size.y);
            var collider = go.GetComponent<CircleCollider2D>();
            collider.radius = Mathf.Min(renderer.sprite.bounds.extents.x, renderer.sprite.bounds.extents.y) * 0.85f;
            Set(go.GetComponent<CatchableItem>(), "definition", Definition(name, id, kind));
            return PrefabUtility.SaveAsPrefabAsset(go, "Assets/Prefab/" + name + ".prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    private static BananaPeel MakePeel()
    {
        var go = new GameObject("BananaPeel", typeof(SpriteRenderer), typeof(BoxCollider2D), typeof(BananaPeel));
        try
        {
            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Image/karisozai/banana_kawa.png");
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Fruit.prefab").GetComponent<SpriteRenderer>().sharedMaterial;
            renderer.sortingOrder = 3;
            go.transform.localScale = Vector3.one * (0.9f / renderer.sprite.bounds.size.x);
            var collider = go.GetComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = new Vector2(renderer.sprite.bounds.size.x * 0.8f, renderer.sprite.bounds.size.y * 0.5f);
            return PrefabUtility.SaveAsPrefabAsset(go, "Assets/Prefab/BananaPeel.prefab").GetComponent<BananaPeel>();
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    private static void ConfigureFeverFruit()
    {
        const string path = "Assets/Prefab/FeverFruit.prefab";
        var go = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.color = new Color(1f, 0.8f, 0.15f);
            PrefabUtility.SaveAsPrefabAsset(go, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(go); }
    }

    private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
    {
        var settings = new SerializedObject(target);
        var property = settings.FindProperty(field);
        if (property == null) throw new InvalidOperationException(field);
        property.objectReferenceValue = value;
        settings.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }
}
