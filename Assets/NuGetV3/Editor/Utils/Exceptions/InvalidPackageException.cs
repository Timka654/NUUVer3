#if UNITY_EDITOR

using NuGetV3.Data;
using System;

namespace NuGetV3.Utils.Exceptions
{
    class InvalidPackageException : Exception
    {
        public PackageInstallProcessData InvalidPackageInfo { get; }

        public InvalidPackageException(string message, PackageInstallProcessData package) : base(message)
        {
            InvalidPackageInfo = package;
        }
    }
}

#endif