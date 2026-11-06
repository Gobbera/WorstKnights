using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.VFX;

public static class FlamethrowerTrapAuthoringUtility
{
    public const string GraphPath = "Assets/VFX/Flamethrower/Flamethrower.vfx";
    public const string PrefabPath = "Assets/Prefabs/Mechanisms/Dragon_Head_Flamethrower_Trap.prefab";
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Assembly VfxEditor => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Unity.VisualEffectGraph.Editor");
    static Type TypeOf(string name) => VfxEditor.GetType("UnityEditor.VFX." + name, true);

    [MenuItem("Tools/Hazards/Prepare Dragon Flamethrower Trap")]
    public static void PrepareAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Saia do Play Mode para preparar os assets.");
        PrepareGraph();
        PreparePrefab();
    }

    public static void PrepareGraph()
    {
        var asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(GraphPath);
        object resource = Extension("GetResource", asset);
        object graph = Extension("GetOrCreateGraph", resource);
        object length = Parameter(graph, "Jet Range", typeof(float), 10f);
        object width = Parameter(graph, "Jet Width", typeof(float), 1f);
        object opacity = Parameter(graph, "Jet Opacity", typeof(float), 1f);
        object boundsCenter = Parameter(graph, "Jet Bounds Center", typeof(Vector3), new Vector3(0f, 0f, 5f));
        object boundsSize = Parameter(graph, "Jet Bounds Size", typeof(Vector3), new Vector3(6f, 6f, 16f));
        object[] contexts = Children(graph).Where(c => TypeOf("VFXContext").IsInstanceOfType(c)).ToArray();
        foreach (object context in contexts)
        {
            if (TypeOf("VFXBasicInitialize").IsInstanceOfType(context))
            {
                object data = Invoke(context, "GetData");
                PropertyInfo space = data.GetType().GetProperty("space", Flags);
                space.SetValue(data, Enum.Parse(space.PropertyType, "Local"));
                FieldInfo boundsMode = data.GetType().GetField("boundsMode", Flags);
                Setting(data, "boundsMode", Enum.Parse(boundsMode.FieldType, "Manual"));
                object bounds = Slots(context, "inputSlots")[0];
                object[] children = Children(bounds);
                Link(Slots(boundsCenter, "outputSlots")[0], children.First(c => SlotName(c) == "center"));
                Link(Slots(boundsSize, "outputSlots")[0], children.First(c => SlotName(c) == "size"));
            }
            if (!TypeOf("VFXAbstractParticleOutput").IsInstanceOfType(context)) continue;
            object block = Children(context).FirstOrDefault(c => c.GetType() == TypeOf("Block.CustomHLSL")
                && Convert.ToString(Invoke(c, "GetSettingValue", "m_BlockName")) == "Trap Jet Shape");
            if (block == null)
            {
                block = ScriptableObject.CreateInstance(TypeOf("Block.CustomHLSL"));
                Setting(block, "m_BlockName", "Trap Jet Shape");
                Invoke(context, "AddChild", block, -1, true);
            }
            Setting(block, "m_HLSLCode", "void TrapJetShape(inout VFXAttributes attributes, in float jetRange, in float jetWidth, in float jetOpacity)\n"
                + "{\n  float progress = saturate(attributes.age / max(attributes.lifetime, 0.0001));\n"
                + "  attributes.position.z = progress * jetRange;\n  attributes.position.xy *= jetWidth;\n"
                + "  attributes.size *= jetWidth;\n  attributes.alpha *= saturate(jetOpacity);\n}\n");
            Invoke(block, "ResyncSlots", true);
            object[] inputs = Slots(block, "inputSlots");
            Link(Slots(length, "outputSlots")[0], inputs.First(s => SlotName(s) == "_jetRange"));
            Link(Slots(width, "outputSlots")[0], inputs.First(s => SlotName(s) == "_jetWidth"));
            Link(Slots(opacity, "outputSlots")[0], inputs.First(s => SlotName(s) == "_jetOpacity"));
        }
        Invoke(graph, "RecompileIfNeeded", false, false);
        Extension("WriteAssetWithSubAssets", resource);
        AssetDatabase.ImportAsset(GraphPath, ImportAssetOptions.ForceUpdate);
    }

    static object Parameter(object graph, string name, Type valueType, object value)
    {
        object parameter = Children(graph).FirstOrDefault(c => c.GetType() == TypeOf("VFXParameter")
            && Convert.ToString(Get(c, "exposedName")) == name);
        if (parameter != null) return parameter;
        parameter = ScriptableObject.CreateInstance(TypeOf("VFXParameter"));
        Invoke(parameter, "Init", valueType);
        Setting(parameter, "m_ExposedName", name);
        Setting(parameter, "m_Exposed", true);
        Setting(parameter, "m_Category", "Trap Controller");
        parameter.GetType().GetProperty("value", Flags).SetValue(parameter, value);
        Invoke(graph, "AddChild", parameter, -1, true);
        return parameter;
    }

    static void PreparePrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform pivot = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Flamethrower_Pivot");
            VisualEffect effect = root.GetComponentInChildren<VisualEffect>(true);
            if (effect == null)
            {
                var visual = new GameObject("Flamethrower VFX");
                visual.transform.SetParent(pivot, false);
                visual.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                effect = visual.AddComponent<VisualEffect>();
            }
            effect.initialEventName = "OnStop";
            effect.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(GraphPath);
            FlamethrowerTrapController controller = root.GetComponent<FlamethrowerTrapController>();
            if (controller == null) controller = root.AddComponent<FlamethrowerTrapController>();
            Transform lightTransform = root.transform.Find(FlamethrowerTrapController.EndLightObjectName);
            if (lightTransform == null)
            {
                lightTransform = new GameObject(FlamethrowerTrapController.EndLightObjectName).transform;
                lightTransform.SetParent(root.transform, false);
            }
            Light light = lightTransform.GetComponent<Light>();
            if (light == null)
            {
                light = lightTransform.gameObject.AddComponent<Light>();
                light.shadows = LightShadows.None;
                light.lightmapBakeType = LightmapBakeType.Realtime;
            }
            light.type = LightType.Point;
            light.enabled = false;
            light.intensity = 0f;
            var data = new SerializedObject(controller);
            data.FindProperty("flamethrowerPivot").objectReferenceValue = pivot;
            data.FindProperty("flameEffect").objectReferenceValue = effect;
            data.FindProperty("endLight").objectReferenceValue = light;
            data.ApplyModifiedPropertiesWithoutUndo();
            light.color = data.FindProperty("endLightColor").colorValue;
            light.range = data.FindProperty("endLightRange").floatValue;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    [MenuItem("Tools/Hazards/Connect Statue Puzzle Failure To Flamethrower")]
    public static void ConnectActiveScene()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var puzzles = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<StatuePuzzleController>(true)).ToArray();
        var traps = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FlamethrowerTrapController>(true)).ToArray();
        if (puzzles.Length != 1 || traps.Length != 1) throw new InvalidOperationException("Selecao ambigua: a cena precisa de um puzzle e uma armadilha para esta configuracao.");
        StatuePuzzleController puzzle = puzzles[0];
        Undo.RecordObject(puzzle, "Connect Statue Puzzle Fire Trap");
        bool alreadyConnected = Enumerable.Range(0, puzzle.OnPuzzleFailed.GetPersistentEventCount())
            .Any(i => puzzle.OnPuzzleFailed.GetPersistentTarget(i) == traps[0]
                && puzzle.OnPuzzleFailed.GetPersistentMethodName(i) == nameof(FlamethrowerTrapController.TriggerTrap));
        if (!alreadyConnected) UnityEventTools.AddPersistentListener(puzzle.OnPuzzleFailed, traps[0].TriggerTrap);
        EditorUtility.SetDirty(puzzle);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = traps[0].gameObject;
    }

    static object Get(object model, string property) => model.GetType().GetProperties(Flags).First(p => p.Name == property).GetValue(model);
    static object[] Children(object model) => ((IEnumerable)Get(model, "children")).Cast<object>().ToArray();
    static object[] Slots(object model, string property) => ((IEnumerable)Get(model, property)).Cast<object>().ToArray();
    static string SlotName(object slot) => Convert.ToString(Get(slot, "name"));
    static void Setting(object model, string name, object value) => Invoke(model, "SetSettingValue", name, value);
    static void Link(object output, object input)
    {
        if (!(bool)Invoke(input, "Link", output, true)) throw new InvalidOperationException("Falha ao conectar parametro do VFX.");
    }
    static object Invoke(object model, string name, params object[] args)
    {
        MethodInfo method = model.GetType().GetMethods(Flags).First(m => m.Name == name && !m.ContainsGenericParameters
            && m.GetParameters().Length == args.Length && m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(v => v));
        return method.Invoke(model, args);
    }
    static object Extension(string name, object argument)
    {
        MethodInfo method = VfxEditor.GetTypes().SelectMany(t => t.GetMethods(Flags))
            .First(m => m.IsStatic && m.Name == name && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType.IsInstanceOfType(argument));
        return method.Invoke(null, new[] { argument });
    }
}
