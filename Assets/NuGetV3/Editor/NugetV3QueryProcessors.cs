#if UNITY_EDITOR

using NuGetV3.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NuGetV3
{
    internal abstract class INugetQueryProcessor
    {
        protected readonly NugetV3Window window;
        protected readonly NugetV3Local local;

        public INugetQueryProcessor(NugetV3Window window, NugetV3Local local)
        {
            this.window = window;
            this.local = local;
        }

        public abstract void Query(CancellationToken cancellationToken, string query, bool clear = false);
    }

    internal class NugetBrowseQueryProcessor : INugetQueryProcessor
    {

        private Dictionary<string, RepositoryPackagesViewModel> nugetQueryMap = new Dictionary<string, RepositoryPackagesViewModel>();

        public NugetBrowseQueryProcessor(NugetV3Window window, NugetV3Local local) : base(window, local)
        {
        }

        public override void Query(CancellationToken cancellationToken, string query, bool clear = false)
        {
            if (clear)
                nugetQueryMap.Clear();

            local.GetIndexResourcesRequestAsync(() =>
                local.QueryRequest(query, 15, () =>
                {
                    if (cancellationToken != CancellationToken.None)
                        window.CancelRefreshProcessState();

                    List<RepositoryPackageViewModel> newPackageList = new List<RepositoryPackageViewModel>();

                    foreach (var item in nugetQueryMap)
                    {
                        newPackageList.AddRange(item.Value.Packages);
                    }

                    window.SetBrowsePackageViewList(newPackageList);

                }, nugetQueryMap, cancellationToken)
            );
        }
    }

    internal class NugetInstalledPackageRepository : INugetQueryProcessor
    {
        public NugetInstalledPackageRepository(NugetV3Window window, NugetV3Local local) : base(window, local)
        {
        }

        public override void Query(CancellationToken cancellationToken, string query, bool clear = false)
        {
            IEnumerable<InstalledPackageData> result = local.GetInstalledPackages();

            if (string.IsNullOrWhiteSpace(query) == false)
                result = result.Where(x => x.SelectedVersionCatalog.Id.Contains(query, StringComparison.OrdinalIgnoreCase));

            window.SetInstalledPackageViewList(result
                .Cast<RepositoryPackageViewModel>()
                .ToList());

            if (cancellationToken != CancellationToken.None)
                window.CancelRefreshProcessState();
        }
    }

    internal class NugetUpdatesPackageRepository : INugetQueryProcessor
    {
        public NugetUpdatesPackageRepository(NugetV3Window window, NugetV3Local local) : base(window, local)
        {
        }

        public override void Query(CancellationToken cancellationToken, string query, bool clear = false)
        {
            IEnumerable<InstalledPackageData> result = local.GetInstalledPackages();

            if (string.IsNullOrWhiteSpace(query) == false)
                result = result.Where(x => x.SelectedVersionCatalog.Id.Contains(query, StringComparison.OrdinalIgnoreCase));

            local.GetIndexResourcesRequestAsync(async () =>
            {
                await Task.WhenAll(result
                    .Select(async x => await local.PackageVersionsRequest(x, (updated, result) =>
                    {
                        if (updated)
                            x.SetPackageVersions(result);
                    }, cancellationToken)).ToArray());

                result = result
                    .Where(x => x.Versions != null)
                    .Where(x => x.HasUpdates);

                window.SetUpdatesPackageViewList(result
                    .Cast<RepositoryPackageViewModel>()
                    .ToList());

                if (cancellationToken != CancellationToken.None)
                    window.CancelRefreshProcessState();

                local.UpdateUpdateTab();

            });

        }
    }
}

#endif