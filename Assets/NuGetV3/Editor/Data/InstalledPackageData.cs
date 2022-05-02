#if UNITY_EDITOR

using NU.Core.Models.Response;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGetV3.Data
{
    public class InstalledPackageData : RepositoryPackageViewModel
    {
        public NugetRegistrationCatalogDepedencyGroupModel SelectedFrameworkDeps { get; set; }

        public string SelectedFramework => SelectedFrameworkDeps?.TargetFramework;
    }
}

#endif