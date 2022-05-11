#if UNITY_EDITOR

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NuGetV3.Utils
{
    public class GLayoutUtils
    {
        public static void HorizontalControlGroup(Action inGroup)
        {
            GUILayout.BeginHorizontal();

            inGroup();

            GUILayout.EndHorizontal();
        }

        public static void HorizontalControlGroup(Action inGroup, GUIStyle style)
        {
            GUILayout.BeginHorizontal(style);

            inGroup();

            GUILayout.EndHorizontal();
        }

        public static void HorizontalControlGroup(Action inGroup, params GUILayoutOption[] options)
        {
            GUILayout.BeginHorizontal(options);

            inGroup();

            GUILayout.EndHorizontal();
        }

        public static void HorizontalControlGroup(Action inGroup, GUIStyle style, params GUILayoutOption[] options)
        {
            GUILayout.BeginHorizontal(style, options);

            inGroup();

            GUILayout.EndHorizontal();
        }

        public static void VerticalControlGroup(Action inGroup, params GUILayoutOption[] options)
        {
            GUILayout.BeginVertical(options);

            inGroup();

            GUILayout.EndVertical();
        }

        public static Vector2 ScrollViewGroup(Vector2 currentPosition, Action inGroup)
        {
            currentPosition = GUILayout.BeginScrollView(currentPosition);

            inGroup();

            GUILayout.EndScrollView();

            return currentPosition;
        }
    }
}

#endif