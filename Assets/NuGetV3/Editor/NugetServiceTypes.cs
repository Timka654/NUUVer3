#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGetV3
{
    /// <summary>
    /// Credits original source of NuGet(ServiceTypes.cs) in GitHub (https://github.com/NuGet/NuGet.Client)
    /// </summary>
    public static class NugetServiceTypes
    {
        public static readonly string Version200 = "/2.0.0";
        public static readonly string Version300beta = "/3.0.0-beta";
        public static readonly string Version300 = "/3.0.0";
        public static readonly string Version340 = "/3.4.0";
        public static readonly string Versioned = "/Versioned";
        public static readonly string Version470 = "/4.7.0";
        public static readonly string Version490 = "/4.9.0";
        public static readonly string Version500 = "/5.0.0";
        public static readonly string Version510 = "/5.1.0";

        public static readonly string[] SearchQueryService = { "SearchQueryService" + Versioned, "SearchQueryService" + Version340, "SearchQueryService" + Version300beta };
        public static readonly string[] RegistrationsBaseUrl = { "RegistrationsBaseUrl" + Versioned, "RegistrationsBaseUrl" + Version340, "RegistrationsBaseUrl" + Version300beta };
        public static readonly string[] SearchAutocompleteService = { "SearchAutocompleteService" + Versioned, "SearchAutocompleteService" + Version300beta };
        public static readonly string[] ReportAbuse = { "ReportAbuseUriTemplate" + Versioned, "ReportAbuseUriTemplate" + Version300 };
        public static readonly string[] PackageDetailsUriTemplate = { "PackageDetailsUriTemplate" + Version510 };
        public static readonly string[] PackagePublish = { "PackagePublish" + Versioned, "PackagePublish" + Version200 };
        public static readonly string[] PackageBaseAddress = { "PackageBaseAddress" + Versioned, "PackageBaseAddress" + Version300 };
        public static readonly string[] RepositorySignatures = { "RepositorySignatures" + Version500, "RepositorySignatures" + Version490, "RepositorySignatures" + Version470 };
        public static readonly string[] SymbolPackagePublish = { "SymbolPackagePublish" + Version490 };
    }
}

#endif