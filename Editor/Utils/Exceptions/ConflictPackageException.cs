#if UNITY_EDITOR

using NuGetV3.Data;
using System;

namespace NuGetV3.Utils.Exceptions
{
    class ConflictPackageException : Exception
    {
        public PackageInstallProcessData InvalidPackageInfo { get; }

        public ConflictPackageException(string message, PackageInstallProcessData package) : base(message)
        {
            InvalidPackageInfo = package;
        }
    }

    class ConflictDepPackageException : Exception
    {
        public PackageInstallProcessData InvalidPackageInfo { get; }

        public ConflictDepPackageException(string message, PackageInstallProcessData package) : base(message)
        {
            InvalidPackageInfo = package;
        }
    }
}

#endif