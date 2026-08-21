using UnityEngine;
using Photon.Pun;

public static class EnemySceneBootstrap
{
    private const string EnemyObjectName = "Enemy";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapGameplayEnemies()
    {
        if (!ShouldBootstrapForActiveScene())
            return;

        Transform[] sceneTransforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (int i = 0; i < sceneTransforms.Length; i++)
        {
            Transform sceneTransform = sceneTransforms[i];
            if (sceneTransform == null || !string.Equals(sceneTransform.name, EnemyObjectName, System.StringComparison.Ordinal))
                continue;

            EnsureEnemyComponents(sceneTransform.gameObject);
        }
    }

    private static bool ShouldBootstrapForActiveScene()
    {
        return GameplaySceneRoot.TryGetActiveSceneRoot(createIfMissing: true) != null;
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
}
