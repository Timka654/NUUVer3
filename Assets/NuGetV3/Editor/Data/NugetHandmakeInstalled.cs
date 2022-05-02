using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
public class NugetHandmakeInstalled
{
    [SerializeField] public string Package;

    [SerializeField] public string Version;

    public NugetHandmakeInstalled Clone() => base.MemberwiseClone() as NugetHandmakeInstalled;
}
