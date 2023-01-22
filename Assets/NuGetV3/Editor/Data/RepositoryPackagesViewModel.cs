#if UNITY_EDITOR

using System.Collections.Generic;

namespace NuGetV3.Data
{
    public class RepositoryPackagesViewModel
    {
        public int TotalHits { get; set; }

        public List<RepositoryPackageViewModel> Packages { get; set; }
    }
}

#endif