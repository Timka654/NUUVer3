#if UNITY_EDITOR

using NU.Core.Models.Response;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NuGetV3.Data
{
    public class RepositoryPackageViewModel
    {
        public NugetQueryPackageModel PackageQueryInfo { get; set; }

        public NugetRegistrationResponseModel Registration { get; set; }

        public NugetRegistrationCatalogEntryModel SelectedVersionCatalog { get; set; }

        public List<NuGetVersion> Versions { get; private set; }

        public NuGetVersion SelectedVersion => NuGetVersion.Parse(SelectedVersionCatalog?.Version);

        public DateTime? VersionsReceived { get; set; }

        public virtual bool HasUpdates { get; protected set; }

        public string Name => PackageQueryInfo.Id;

        public void SetPackageVersions(IEnumerable<string> versions)
        {
            SetPackageVersions(versions.Distinct().Select(x => new NuGetVersion(x)));
        }

        public virtual void SetPackageVersions(IEnumerable<NuGetVersion> versions)
        {
            Versions = versions.Where(x => !x.IsPrerelease).OrderByDescending(x => x).ToList();

            VersionsReceived = DateTime.UtcNow;
        }
    }

}

#endif