using UnityEngine;

public class GlueGizmoWorldObjectModule : MonoBehaviour
{
    public Vector2Int LocalGlueBottomOffset
    {
        get
        {
            var bottom = -transform.up;
            return new Vector2Int(Mathf.RoundToInt(bottom.x), Mathf.RoundToInt(bottom.y));
        }
    }

    public Vector2Int LocalGlueTopOffset => Vector2Int.zero;
}