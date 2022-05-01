using Assets.NuGetV3.Editor.Data.Interface;
using NU.Core.Models.Response;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class FoundPackage : IRegistrableEntry
{
    public NugetQueryPackageModel Package { get; set; }
    
    public List<VersionInfo> Versions { get; set; }

    public NugetRegistrationResponseModel Registration { get; set; }

    public class VersionInfo
    {
        public string RepoName { get; set; }

        public string Version { get; set; }

        public NuGetVersion NVersion { get; set; }
    }

    public override string ToString()
    {
        return Package?.Id ?? String.Empty;
    }
}