using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("World/World Object")]
public class WorldObject : MonoBehaviour
{
    [SerializeField] private string objectName = "World Object";

    public string DisplayName => string.IsNullOrWhiteSpace(objectName) ? gameObject.name : objectName;

    private void Reset()
    {
        objectName = gameObject.name;
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(objectName))
            objectName = gameObject.name;
    }
}
