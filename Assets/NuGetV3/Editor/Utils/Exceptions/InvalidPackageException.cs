#if UNITY_EDITOR

using NuGetV3.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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