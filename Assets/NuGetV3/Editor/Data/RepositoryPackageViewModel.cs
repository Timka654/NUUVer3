using Assets.NuGetV3.Editor.Data.Interface;
using NU.Core.Models.Response;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class RepositoryPackageViewModel : IRegistrableEntry
{
    public NugetQueryPackageModel Package { get; set; }

    public NugetRegistrationResponseModel Registration { get; set; }

    public NugetRegistrationCatalogEntryModel VersionCatalog { get; set; }

    public string[] Versions { get; set; }

    public string SelectedVersion { get; set; }

    public DateTime? VersionsReceived { get; set; }

    public RepositoryPackageViewModel Installed { get; set; }
}
