#if UNITY_EDITOR

using NU.Core.Models.Response;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NuGetV3.Data
{
    [Serializable]
    public class NugetHandmakeInstalled
    {
        [SerializeField] public string Package;

        [SerializeField] public string Version;

        [SerializeField] public string TargetFramework;

        public NugetHandmakeInstalled Clone() => base.MemberwiseClone() as NugetHandmakeInstalled;

        public InstalledPackageData CreatePackage()
        {
            return new InstalledPackageData()
            {
                PackageQueryInfo = new NugetQueryPackageModel()
                {
                    Id = Package,
                    Version = Version
                },
                Registration = new NugetRegistrationResponseModel()
                {
                    Items = new List<NugetRegistrationPageModel>()
                    {
                        new NugetRegistrationPageModel()
                        {
                            Items = new List<NugetRegistrationLeafModel>()
                            {
                                new NugetRegistrationLeafModel() {
                                    CatalogEntry = new NugetRegistrationCatalogEntryModel()
                                    {
                                        Id = Package,
                                        Version = Version,
                                        DependencyGroups = new List<NugetRegistrationCatalogDepedencyGroupModel>()
                                        {
                                            new NugetRegistrationCatalogDepedencyGroupModel()
                                            {
                                                Dependencies = new List<NugetRegistrationCatalogDepedencyModel>(),
                                                TargetFramework = TargetFramework
                                            }
                                        }
                                    }
                                }
                            },
                        }
                    }
                },
                InstalledVersionCatalog = new NugetRegistrationCatalogEntryModel()
                {
                    Id = Package,
                    Version = Version,
                }
            };
        }
    }
}

#endif