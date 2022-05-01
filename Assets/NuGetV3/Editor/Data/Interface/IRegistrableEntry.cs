using NU.Core.Models.Response;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assets.NuGetV3.Editor.Data.Interface
{
    public interface IRegistrableEntry
    {
        NugetRegistrationResponseModel Registration { get; set; }

        NugetQueryPackageModel Package { get; set; }
    }
}
