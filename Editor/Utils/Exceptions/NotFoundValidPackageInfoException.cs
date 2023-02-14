#if UNITY_EDITOR

using NuGetV3.Data;
using System;

namespace NuGetV3.Utils.Exceptions
{
    class NotFoundValidPackageInfoException : Exception
    {
        public PackageInstallProcessData InvalidPackageInfo { get; }

        public NotFoundValidPackageInfoException(string message, PackageInstallProcessData package) : base(message)
        {
            InvalidPackageInfo = package;
        }
    }
}

#endif