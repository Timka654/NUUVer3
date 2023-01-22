#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

namespace NuGetV3.Utils
{
    public class ResizeHorizontalView
    {
        private readonly EditorWindow window;

        bool resize = false;

        float currentScrollViewWidth = 0;
        Rect cursorChangeRect;

        Texture2D transparentColor = new Texture2D(1, 1);

        public ResizeHorizontalView(EditorWindow window, float initialWidth, float y, float height)
        {
            this.window = window;
            this.currentScrollViewWidth = initialWidth;
            transparentColor.SetPixel(0, 0, new Color(0, 0, 0, 0));

            cursorChangeRect = new Rect(currentScrollViewWidth, y, 5f, height);
        }

        public void SetVertical(float y, float height)
        {
            cursorChangeRect.y = y;
            cursorChangeRect.height = height;
        }

        public float Process(float min, float max)
        {

            //GUI.DrawTexture(cursorChangeRect, transparentColor);
            EditorGUIUtility.AddCursorRect(cursorChangeRect, MouseCursor.ResizeHorizontal);

            Vector2 pos = Event.current.mousePosition;

            if (Event.current.type == EventType.MouseDown && cursorChangeRect.Contains(pos))
            {
                resize = true;
            }

            if (resize)
            {
                currentScrollViewWidth = pos.x;

                if (currentScrollViewWidth < min)
                    currentScrollViewWidth = min;

                if (currentScrollViewWidth > max)
                    currentScrollViewWidth = max;

                cursorChangeRect.Set(currentScrollViewWidth, cursorChangeRect.y, cursorChangeRect.width, cursorChangeRect.height);
            }

            if (Event.current.type == EventType.MouseUp)
                resize = false;

            return currentScrollViewWidth;
        }
    }
}

#endif