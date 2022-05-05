#if UNITY_EDITOR

using NU.Core.Models.Response;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGetV3.Data
{
    class PackageInstallProcessData
    {
        public RepositoryPackageViewModel RepositoryPackage { get; set; }

        public NuGetVersion SelectedVersion => RepositoryPackage.SelectedVersion;

        public string PackageName => RepositoryPackage.PackageQueryInfo.Id;

        public NugetRegistrationResponseModel Registration => RepositoryPackage.Registration;

        public NugetRegistrationCatalogEntryModel VersionCatalog => RepositoryPackage.SelectedVersionCatalog;

        public List<PackageInstallProcessData> DependecyList { get; } = new List<PackageInstallProcessData>();

        public string BuildDir { get; set; }

        public NugetRegistrationCatalogDepedencyGroupModel SelectedFrameworkDeps { get; set; }

        public string SelectedFramework => SelectedFrameworkDeps?.TargetFramework;

        public PackageInstallProcessData Clone()
        {
            var clone = new PackageInstallProcessData()
            {
                RepositoryPackage = new RepositoryPackageViewModel()
                {
                    PackageQueryInfo = RepositoryPackage.PackageQueryInfo,
                    Registration = RepositoryPackage.Registration,
                }
            };

            clone.RepositoryPackage.SetPackageVersions(RepositoryPackage.Versions);

            return clone;

        }

        public List<PackageInstallProcessData> IgnoringPackageList { get; } = new List<PackageInstallProcessData>();

        public List<PackageInstallProcessData> InstallList { get; } = new List<PackageInstallProcessData>();

        public InstalledPackageData InstalledPackage { get; set; }

        public List<InstalledPackageData> RemovePackageList { get; } = new List<InstalledPackageData>();

        public List<Exception> ProcessExceptions { get; } = new List<Exception>();
    }
}

#endif