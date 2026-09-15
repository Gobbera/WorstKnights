using UnityEngine;

[DisallowMultipleComponent]
public class FootstepSurface : MonoBehaviour
{
    [SerializeField] private FootstepSurfaceDefinition surface;

    public FootstepSurfaceDefinition Surface => surface;
}
