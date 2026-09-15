using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class FootstepSurfaceResolverTests
{
    [Test]
    public void ResolveCue_UsesMarkedFootstepSurfaceForRequestedGait()
    {
        AudioCue fallbackCue = CreateCue("Fallback");
        AudioCue defaultWalkCue = CreateCue("DefaultWalk");
        AudioCue stoneWalkCue = CreateCue("StoneWalk");
        AudioCue stoneEnemyCue = CreateCue("StoneEnemyWalk");

        FootstepSurfaceDefinition defaultSurface = CreateSurface(defaultWalkCue);
        FootstepSurfaceDefinition stoneSurface = CreateSurface(stoneWalkCue, enemyWalk: stoneEnemyCue);
        FootstepSurfaceResolver resolver = CreateResolver(defaultSurface);

        GameObject floor = CreateFloor("StoneFloor", Vector3.zero);
        SetObjectReference(floor.AddComponent<FootstepSurface>(), "surface", stoneSurface);
        Physics.SyncTransforms();

        try
        {
            Assert.That(Physics.Raycast(Vector3.up, Vector3.down, out RaycastHit hit, 2f), Is.True);

            AudioCue resolvedCue = resolver.ResolveCue(hit, FootstepGait.EnemyWalk, fallbackCue);

            Assert.That(resolvedCue, Is.SameAs(stoneEnemyCue));
        }
        finally
        {
            DestroyObjects(floor, resolver, defaultSurface, stoneSurface, fallbackCue, defaultWalkCue, stoneWalkCue, stoneEnemyCue);
        }
    }

    [Test]
    public void ResolveCue_UsesRendererMaterialWhenNoSurfaceMarkerExists()
    {
        AudioCue fallbackCue = CreateCue("Fallback");
        AudioCue defaultWalkCue = CreateCue("DefaultWalk");
        AudioCue woodWalkCue = CreateCue("WoodWalk");
        Material woodMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));

        FootstepSurfaceDefinition defaultSurface = CreateSurface(defaultWalkCue);
        FootstepSurfaceDefinition woodSurface = CreateSurface(woodWalkCue);
        FootstepSurfaceResolver resolver = CreateResolver(defaultSurface);
        AddRendererMaterialSurface(resolver, woodMaterial, woodSurface);

        GameObject floor = CreateFloor("WoodFloor", Vector3.zero);
        MeshRenderer renderer = floor.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = woodMaterial;
        Physics.SyncTransforms();

        try
        {
            Assert.That(Physics.Raycast(Vector3.up, Vector3.down, out RaycastHit hit, 2f), Is.True);

            AudioCue resolvedCue = resolver.ResolveCue(hit, FootstepGait.Walk, fallbackCue);

            Assert.That(resolvedCue, Is.SameAs(woodWalkCue));
        }
        finally
        {
            DestroyObjects(floor, resolver, defaultSurface, woodSurface, fallbackCue, defaultWalkCue, woodWalkCue, woodMaterial);
        }
    }

    [Test]
    public void TryFindGroundHit_IgnoresCharacterColliderAndFindsFloor()
    {
        GameObject floor = CreateFloor("Floor", Vector3.zero);
        GameObject actor = new GameObject("Actor");
        actor.transform.position = new Vector3(0f, 0.8f, 0f);
        Rigidbody actorRigidbody = actor.AddComponent<Rigidbody>();
        actorRigidbody.useGravity = false;
        CapsuleCollider actorCollider = actor.AddComponent<CapsuleCollider>();
        actorCollider.height = 1.6f;
        actorCollider.radius = 0.35f;
        actorCollider.center = new Vector3(0f, 0.8f, 0f);
        Physics.SyncTransforms();

        try
        {
            bool foundHit = FootstepSurfaceUtility.TryFindGroundHit(
                actor.transform.position,
                Physics.DefaultRaycastLayers,
                0.5f,
                2f,
                actor.transform,
                actorRigidbody,
                actorCollider,
                new RaycastHit[8],
                out RaycastHit hit);

            Assert.That(foundHit, Is.True);
            Assert.That(hit.collider, Is.EqualTo(floor.GetComponent<BoxCollider>()));
        }
        finally
        {
            DestroyObjects(floor, actor);
        }
    }

    [Test]
    public void CharacterProfile_GetCue_UsesGaitSpecificCueAndFallbacks()
    {
        AudioCue bootWalkCue = CreateCue("BootWalk");
        AudioCue bootSprintCue = CreateCue("BootSprint");
        AudioCue armorLandCue = CreateCue("ArmorLand");
        FootstepCharacterAudioProfile profile = ScriptableObject.CreateInstance<FootstepCharacterAudioProfile>();
        SetObjectReference(profile, "walk", bootWalkCue);
        SetObjectReference(profile, "sprint", bootSprintCue);
        SetObjectReference(profile, "land", armorLandCue);

        try
        {
            Assert.That(profile.GetCue(FootstepGait.Sprint), Is.SameAs(bootSprintCue));
            Assert.That(profile.GetCue(FootstepGait.Land), Is.SameAs(armorLandCue));
            Assert.That(profile.GetCue(FootstepGait.EnemyWalk), Is.SameAs(bootWalkCue));
        }
        finally
        {
            DestroyObjects(profile, bootWalkCue, bootSprintCue, armorLandCue);
        }
    }

    [Test]
    public void PlayerAndEnemyPrefabs_UseSharedFootstepPlaybackController()
    {
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player.prefab");
        GameObject enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Enemy.prefab");

        Assert.That(playerPrefab, Is.Not.Null);
        Assert.That(enemyPrefab, Is.Not.Null);

        FootstepPlaybackController playerPlayback = playerPrefab.GetComponent<FootstepPlaybackController>();
        FootstepPlaybackController enemyPlayback = enemyPrefab.GetComponent<FootstepPlaybackController>();
        PlayerAudioController playerAudio = playerPrefab.GetComponent<PlayerAudioController>();
        EnemyFootstepAudioController enemyFootsteps = enemyPrefab.GetComponent<EnemyFootstepAudioController>();

        Assert.That(playerPlayback, Is.Not.Null);
        Assert.That(enemyPlayback, Is.Not.Null);
        Assert.That(playerAudio, Is.Not.Null);
        Assert.That(enemyFootsteps, Is.Not.Null);
        AssertObjectReference(playerAudio, "footstepPlayback", playerPlayback);
        AssertObjectReference(enemyFootsteps, "footstepPlayback", enemyPlayback);
    }

    private static GameObject CreateFloor(string name, Vector3 position)
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = name;
        floor.transform.position = position;
        floor.transform.localScale = new Vector3(4f, 0.1f, 4f);
        Object.DestroyImmediate(floor.GetComponent<MeshRenderer>());
        Object.DestroyImmediate(floor.GetComponent<MeshFilter>());
        return floor;
    }

    private static AudioCue CreateCue(string name)
    {
        AudioCue cue = ScriptableObject.CreateInstance<AudioCue>();
        cue.name = name;
        return cue;
    }

    private static FootstepSurfaceDefinition CreateSurface(
        AudioCue walk,
        AudioCue sprint = null,
        AudioCue crouch = null,
        AudioCue enemyWalk = null,
        AudioCue jump = null,
        AudioCue land = null)
    {
        FootstepSurfaceDefinition surface = ScriptableObject.CreateInstance<FootstepSurfaceDefinition>();
        SetObjectReference(surface, "defaultCue", walk);
        SetObjectReference(surface, "walk", walk);
        SetObjectReference(surface, "sprint", sprint);
        SetObjectReference(surface, "crouch", crouch);
        SetObjectReference(surface, "enemyWalk", enemyWalk);
        SetObjectReference(surface, "jump", jump);
        SetObjectReference(surface, "land", land);
        return surface;
    }

    private static FootstepSurfaceResolver CreateResolver(FootstepSurfaceDefinition defaultSurface)
    {
        FootstepSurfaceResolver resolver = ScriptableObject.CreateInstance<FootstepSurfaceResolver>();
        SetObjectReference(resolver, "defaultSurface", defaultSurface);
        SetBool(resolver, "searchParentSurface", true);
        return resolver;
    }

    private static void AddRendererMaterialSurface(
        FootstepSurfaceResolver resolver,
        Material material,
        FootstepSurfaceDefinition surface)
    {
        SerializedObject serializedObject = new SerializedObject(resolver);
        SerializedProperty entries = serializedObject.FindProperty("rendererMaterialSurfaces");
        entries.arraySize = 1;
        SerializedProperty entry = entries.GetArrayElementAtIndex(0);
        entry.FindPropertyRelative("material").objectReferenceValue = material;
        entry.FindPropertyRelative("surface").objectReferenceValue = surface;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetObjectReference(Object target, string propertyName, Object value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        property.objectReferenceValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetBool(Object target, string propertyName, bool value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        property.boolValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AssertObjectReference(Object target, string propertyName, Object expectedValue)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);

        Assert.That(property, Is.Not.Null, $"{target.name} deve ter a propriedade serializada '{propertyName}'.");
        Assert.That(property.objectReferenceValue, Is.SameAs(expectedValue));
    }

    private static void DestroyObjects(params Object[] objects)
    {
        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] != null)
                Object.DestroyImmediate(objects[i]);
        }
    }
}
