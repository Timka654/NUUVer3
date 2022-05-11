#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGetV3.Data
{
    public class RepositoryPackagesViewModel
    {
        public int TotalHits { get; set; }

        public List<RepositoryPackageViewModel> Packages { get; set; }
    }
}

#endif