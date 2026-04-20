using UnityEngine;

public class IgnoreLimbCollisions : MonoBehaviour
{
    void Start()
    {
        // Only ignore collisions within THIS player's own colliders.
        // Cross-player collisions must stay active so punches and physics contact register.
        var colliders = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
            for (int k = i + 1; k < colliders.Length; k++)
                Physics2D.IgnoreCollision(colliders[i], colliders[k]);
    }
}
