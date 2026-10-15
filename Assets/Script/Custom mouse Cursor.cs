using UnityEngine;

public class CustomMouseCursor : MonoBehaviour
{
    public Texture2D mouseCursor;
    private Vector2 hotSpot;

    void Start()
    {
        // Center the hotspot (adjust width/height by 2)
        if (mouseCursor != null)
        {
            hotSpot = new Vector2(mouseCursor.width / 2f, mouseCursor.height / 2f);
            Cursor.SetCursor(mouseCursor, hotSpot, CursorMode.Auto);
        }
    }
}