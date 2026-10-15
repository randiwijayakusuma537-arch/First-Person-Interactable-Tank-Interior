using UnityEngine;

/// <summary>
/// Small compatibility layer. Unity 6 renamed Rigidbody.velocity / drag / angularDrag
/// to linearVelocity / linearDamping / angularDamping. These helpers work on both.
/// </summary>
public static class RigidbodyCompat
{
    public static Vector3 GetLinearVelocity(this Rigidbody rb)
    {
#if UNITY_6000_0_OR_NEWER
        return rb.linearVelocity;
#else
        return rb.velocity;
#endif
    }

    public static void SetLinearVelocity(this Rigidbody rb, Vector3 value)
    {
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = value;
#else
        rb.velocity = value;
#endif
    }

    public static float GetLinearDamping(this Rigidbody rb)
    {
#if UNITY_6000_0_OR_NEWER
        return rb.linearDamping;
#else
        return rb.drag;
#endif
    }

    public static void SetLinearDamping(this Rigidbody rb, float value)
    {
#if UNITY_6000_0_OR_NEWER
        rb.linearDamping = value;
#else
        rb.drag = value;
#endif
    }

    public static float GetAngularDamping(this Rigidbody rb)
    {
#if UNITY_6000_0_OR_NEWER
        return rb.angularDamping;
#else
        return rb.angularDrag;
#endif
    }

    public static void SetAngularDamping(this Rigidbody rb, float value)
    {
#if UNITY_6000_0_OR_NEWER
        rb.angularDamping = value;
#else
        rb.angularDrag = value;
#endif
    }
}
