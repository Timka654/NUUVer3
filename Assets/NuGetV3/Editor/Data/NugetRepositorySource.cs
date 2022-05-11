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
    public class NugetRepositorySource
    {
        [SerializeField] public string Name;

        [SerializeField] public string Value;

        public NugetRepositorySource Clone() => base.MemberwiseClone() as NugetRepositorySource;
    }
}

#endif