using UnityEngine;
using Photon.Pun;
using UnityEngine.AI;
using System.Collections.Generic;

public static class EnemySceneBootstrap
{
    private const string EnemyObjectName = "Enemy";
    private const string BeholderObjectName = "Beholder";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapGameplayEnemies()
    {
        if (!ShouldBootstrapForActiveScene())
            return;

        HashSet<GameObject> processedEnemies = new HashSet<GameObject>();
        EnemySetup[] authoredEnemies = Object.FindObjectsByType<EnemySetup>(FindObjectsInactive.Include);
        for (int i = 0; i < authoredEnemies.Length; i++)
        {
            EnemySetup enemySetup = authoredEnemies[i];
            if (enemySetup == null)
                continue;

            EnsureEnemyComponents(enemySetup.gameObject, processedEnemies);
        }

        Transform[] sceneTransforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (int i = 0; i < sceneTransforms.Length; i++)
        {
            Transform sceneTransform = sceneTransforms[i];
            if (sceneTransform == null || !IsEnemyRootCandidate(sceneTransform))
                continue;

            EnsureEnemyComponents(sceneTransform.gameObject, processedEnemies);
        }
    }

    private static bool ShouldBootstrapForActiveScene()
    {
        return GameplaySceneRoot.TryGetActiveSceneRoot(createIfMissing: true) != null;
    }

    private static void EnsureEnemyComponents(GameObject enemyObject, HashSet<GameObject> processedEnemies)
    {
        if (enemyObject == null || processedEnemies == null)
            return;

        for (Transform parent = enemyObject.transform.parent; parent != null; parent = parent.parent)
        {
            if (parent.GetComponent<EnemySetup>() != null)
                return;
        }

        if (!processedEnemies.Add(enemyObject))
            return;

        EnsureEnemyComponents(enemyObject);
    }

    private static void EnsureEnemyComponents(GameObject enemyObject)
    {
        if (enemyObject == null)
            return;

        if (enemyObject.GetComponent<CapsuleCollider>() == null)
            enemyObject.AddComponent<CapsuleCollider>();

        Rigidbody rb = enemyObject.GetComponent<Rigidbody>();
        if (rb == null)
            rb = enemyObject.AddComponent<Rigidbody>();

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        if (enemyObject.GetComponent<EnemyHealth>() == null)
            enemyObject.AddComponent<EnemyHealth>();

        if (enemyObject.GetComponent<EnemyKickReaction>() == null)
            enemyObject.AddComponent<EnemyKickReaction>();

        if (enemyObject.GetComponent<EnemySetup>() == null)
            enemyObject.AddComponent<EnemySetup>();

        if (enemyObject.GetComponent<EnemyMotor>() == null)
            enemyObject.AddComponent<EnemyMotor>();

        if (enemyObject.GetComponent<NavMeshAgent>() == null)
            enemyObject.AddComponent<NavMeshAgent>();

        if (enemyObject.GetComponent<EnemyNavigation>() == null)
            enemyObject.AddComponent<EnemyNavigation>();

        if (enemyObject.GetComponent<EnemyVision>() == null)
            enemyObject.AddComponent<EnemyVision>();

        if (enemyObject.GetComponent<EnemyHearing>() == null)
            enemyObject.AddComponent<EnemyHearing>();

        if (enemyObject.GetComponent<EnemyPerception>() == null)
            enemyObject.AddComponent<EnemyPerception>();

        if (enemyObject.GetComponent<EnemyAttack>() == null)
            enemyObject.AddComponent<EnemyAttack>();

        if (enemyObject.GetComponent<EnemyBrain>() == null)
            enemyObject.AddComponent<EnemyBrain>();

        if (enemyObject.GetComponent<EnemyNetworkSync>() == null)
            enemyObject.AddComponent<EnemyNetworkSync>();

        if (enemyObject.GetComponent<EnemyAnimationController>() == null)
            enemyObject.AddComponent<EnemyAnimationController>();

        PhotonView photonView = enemyObject.GetComponent<PhotonView>();
        EnemyNetworkSync networkSync = enemyObject.GetComponent<EnemyNetworkSync>();
        if (photonView != null && networkSync != null)
            photonView.FindObservables(true);

        EnemySetup enemySetup = enemyObject.GetComponent<EnemySetup>();
        if (enemySetup != null)
            enemySetup.RefreshReferences();
    }

    private static bool IsEnemyRootCandidate(Transform sceneTransform)
    {
        if (sceneTransform == null)
            return false;

        if (IsKnownEnemyRootName(sceneTransform.name))
            return true;

        return sceneTransform.GetComponent<EnemySetup>() != null
            || sceneTransform.GetComponent<EnemyBrain>() != null
            || sceneTransform.GetComponent<EnemyHealth>() != null
            || sceneTransform.GetComponent<EnemyNetworkSync>() != null;
    }

    private static bool IsKnownEnemyRootName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return false;

        return string.Equals(objectName, EnemyObjectName, System.StringComparison.Ordinal)
            || objectName.StartsWith(EnemyObjectName + " ", System.StringComparison.Ordinal)
            || objectName.StartsWith(EnemyObjectName + "(", System.StringComparison.Ordinal)
            || string.Equals(objectName, BeholderObjectName, System.StringComparison.Ordinal)
            || objectName.StartsWith(BeholderObjectName + " ", System.StringComparison.Ordinal)
            || objectName.StartsWith(BeholderObjectName + "(", System.StringComparison.Ordinal);
    }
}
