#if UNITY_EDITOR

using NU.Core.Models.Response;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGetV3.Data
{
    class PackageInstallProcessData
    {
        public RepositoryPackageViewModel Package { get; set; }

        public string Version => Package.SelectedVersion;

        public NugetRegistrationResponseModel Registration => Package.Registration;

        public NugetRegistrationCatalogEntryModel VersionCatalog => Package.SelectedVersionCatalog;

        public List<PackageInstallProcessData> DependecyList { get; } = new List<PackageInstallProcessData>();

        public string BuildDir { get; set; }

        public NugetRegistrationCatalogDepedencyGroupModel SelectedFrameworkDeps { get; set; }

        public string SelectedFramework => SelectedFrameworkDeps?.TargetFramework;

        public PackageInstallProcessData Clone()
        {
            return new PackageInstallProcessData()
            {
                Package = new RepositoryPackageViewModel()
                {
                    Package = Package.Package,
                    Registration = Package.Registration,
                    Versions = new List<string>(Package.Versions)

                }
            };
        }

        public List<PackageInstallProcessData> IgnoringPackageList { get; } = new List<PackageInstallProcessData>();

        public List<PackageInstallProcessData> InstallList { get; } = new List<PackageInstallProcessData>();

        public InstalledPackageData InstalledPackage { get; set; }

        public List<InstalledPackageData> RemovePackageList { get; } = new List<InstalledPackageData>();
    }
}

#endif