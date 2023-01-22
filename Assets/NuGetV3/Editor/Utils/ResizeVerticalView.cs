#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

namespace NuGetV3.Utils
{
    public class ResizeVerticalView
    {
        private readonly EditorWindow window;
        bool resize = false;

        float currentScrollViewHeight = 0;

        public ResizeVerticalView(EditorWindow window)
        {
            this.window = window;
        }

        public float Process()
        {
            Rect cursorChangeRect = new Rect(0, currentScrollViewHeight, window.position.width, 5f);

            GUI.DrawTexture(cursorChangeRect, EditorGUIUtility.whiteTexture);
            EditorGUIUtility.AddCursorRect(cursorChangeRect, MouseCursor.ResizeVertical);

            if (Event.current.type == EventType.MouseDown && cursorChangeRect.Contains(Event.current.mousePosition))
            {
                resize = true;
            }

            if (resize)
            {
                currentScrollViewHeight = Event.current.mousePosition.y;
                cursorChangeRect.Set(cursorChangeRect.x, currentScrollViewHeight, cursorChangeRect.width, cursorChangeRect.height);
            }

            if (Event.current.type == EventType.MouseUp)
                resize = false;

            return currentScrollViewHeight;
        }
    }
}

#endif