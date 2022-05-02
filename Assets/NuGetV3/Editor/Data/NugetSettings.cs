#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace NuGetV3.Data
{
    [Serializable]
    public class NugetSettings
    {
        [SerializeField] public bool ConsoleOutput;

        [SerializeField] public string RelativePackagePath;

        public NugetSettings Clone() => base.MemberwiseClone() as NugetSettings;
    }
}

#endif