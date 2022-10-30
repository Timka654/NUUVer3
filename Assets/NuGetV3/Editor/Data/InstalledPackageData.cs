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
    public class InstalledPackageData : RepositoryPackageViewModel
    {
        public NugetRegistrationCatalogEntryModel InstalledVersionCatalog { get; set; }

        public NuGetVersion InstalledVersion => NuGetVersion.Parse(InstalledVersionCatalog?.Version);

        public NugetRegistrationCatalogDepedencyGroupModel SelectedFrameworkDeps { get; set; }

        public string SelectedFramework => SelectedFrameworkDeps?.TargetFramework;

        public override bool HasUpdates { get => base.HasUpdates; protected set => base.HasUpdates = value; }

        public override void SetPackageVersions(IEnumerable<NuGetVersion> versions)
        {
            base.SetPackageVersions(versions);

            if (InstalledVersionCatalog != null && Versions.Any())
            {
                SelectedVersionCatalog = InstalledVersionCatalog;

                HasUpdates = InstalledVersion.OriginalVersion != Versions[0].OriginalVersion;
            }
            else if (!Versions.Any())
                HasUpdates = false;
        }

    }
}

#endif