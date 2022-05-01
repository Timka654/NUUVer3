using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
public class NugetSettings
{
    [SerializeField] public bool ConsoleOutput;

    [SerializeField] public string RelativePackagePath;

    public NugetSettings Clone() => base.MemberwiseClone() as NugetSettings;
}
