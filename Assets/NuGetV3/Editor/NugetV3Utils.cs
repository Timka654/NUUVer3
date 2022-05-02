#if UNITY_EDITOR

using NuGetV3.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;

internal class NugetV3Utils
{

    internal readonly static JsonSerializerOptions JsonOptions = new JsonSerializerOptions()
    {
        IncludeFields = true,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    #region Log

    internal static void LogError(NugetSettings settings, string message)
    {
        if (!settings.ConsoleOutput)
            return;

        Debug.LogError($"NUGET ERROR! {message}");
    }

    internal static void LogInfo(NugetSettings settings, string message)
    {
        if (!settings.ConsoleOutput)
            return;

        Debug.Log($"NUGET INFO! {message}");
    }

    internal static void LogDebug(NugetSettings settings, string message)
    {
        if (!settings.ConsoleOutput)
            return;

        Debug.Log($"<color=blue>NUGET DEBUG! {message}");
    }

    #endregion
}

#endif