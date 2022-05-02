#if UNITY_EDITOR

using NU.Core.Models.Response;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGetV3.Data
{
    public class RepositoryPackageViewModel
    {
        public NugetQueryPackageModel Package { get; set; }

        public NugetRegistrationResponseModel Registration { get; set; }

        public NugetRegistrationCatalogEntryModel VersionCatalog { get; set; }

        public List<string> Versions { get; set; }

        public string SelectedVersion { get; set; }

        public DateTime? VersionsReceived { get; set; }

        public InstalledPackageData Installed { get; set; }
    }
}

#endif