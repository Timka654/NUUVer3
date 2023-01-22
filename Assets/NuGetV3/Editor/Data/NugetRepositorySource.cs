#if UNITY_EDITOR

using System;
using UnityEngine;

namespace NuGetV3.Data
{
    [Serializable]
    public class NugetRepositorySource
    {
        [SerializeField] public string Name;

        [SerializeField] public string Value;

        public NugetRepositorySource Clone() => base.MemberwiseClone() as NugetRepositorySource;
    }
}

#endif