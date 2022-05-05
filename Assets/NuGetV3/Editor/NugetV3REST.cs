#if UNITY_EDITOR

using NU.Core.Models.Response;
using NuGetV3.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUtils = NuGetV3.NugetV3Utils;

namespace NuGetV3
{
    internal partial class NugetV3Local
    {
        public async void GetIndexResourcesRequestAsync(Func<Task> onFinished = null)
            => await GetIndexResourcesRequest(onFinished);

        public async Task GetIndexResourcesRequest(Func<Task> onFinished = null)
        {
            if (!repositories.Any())
            {
                nuGetIndexMap.Clear();

                NUtils.LogError(settings, "Repository list is empty");

                return;
            }

            await threadOperationLocker.WaitAsync();

            foreach (var item in repositories)
            {
                if (nuGetIndexMap.ContainsKey(item.Name))
                    continue;

                try
                {
                    using (HttpClient client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(10);

                        using (var response = await client.GetAsync(item.Value))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                NUtils.LogError(settings, $"Cannot receive from source {item.Name}({item.Value}) - {response.StatusCode}({Enum.GetName(typeof(HttpStatusCode), response.StatusCode)})");

                                continue;
                            }

                            var content = await response.Content.ReadAsStringAsync();

                            var entry = JsonSerializer.Deserialize<NugetIndexResponseModel>(content, NUtils.JsonOptions);

                            nuGetIndexMap.Add(item.Name, entry);
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    NUtils.LogError(settings, ex.ToString());
                }
            }

            threadOperationLocker.Release();

            if (onFinished != null)
                await onFinished();

        }

        public async void QueryRequestAsync(string query, int? take, Action onFinished, Dictionary<string, RepositoryPackagesViewModel> nugetQueryMap, CancellationToken cancellationToken)
            => await QueryRequest(query, take, onFinished, nugetQueryMap, cancellationToken);

        public async Task QueryRequest(string query, int? take, Action onFinished, Dictionary<string, RepositoryPackagesViewModel> nugetQueryMap, CancellationToken cancellationToken)
        {
            try { await threadOperationLocker.WaitAsync(cancellationToken); } catch { return; }

            foreach (var item in nuGetIndexMap)
            {
                var supporedSource = item.Value.Resources
                    .FirstOrDefault(x => NugetServiceTypes.SearchQueryService.Contains(x.Type));

                var repo = repositories.FirstOrDefault(x => x.Name == item.Key);

                if (supporedSource == null)
                {
                    NUtils.LogError(settings, $"Not found valid source in repository {repo.Name}({repo.Value})");
                    continue;
                }

                if (!nugetQueryMap.TryGetValue(repo.Name, out var exists))
                {
                    exists = new RepositoryPackagesViewModel() { Packages = new List<RepositoryPackageViewModel>() };
                    nugetQueryMap.Add(repo.Name, exists);
                }

                try
                {
                    using (HttpClient client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(10);

                        UriBuilder ub = new UriBuilder(supporedSource.Url);

                        List<string> pm = new List<string>();

                        if (!string.IsNullOrWhiteSpace(query))
                        {
                            pm.Add($"q={UrlEncoder.Default.Encode(query)}");
                        }

                        pm.Add($"skip={exists.Packages.Count}");

                        if (!take.HasValue)
                            take = 20;

                        pm.Add($"take={take.Value}");

                        if (pm.Any())
                            ub.Query = "?" + string.Join("&", pm);

                        using (var response = await client.GetAsync(ub.ToString()))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                NUtils.LogError(settings, $"Cannot receive from source {repo.Name}({supporedSource.Url}) - {response.StatusCode}({Enum.GetName(typeof(HttpStatusCode), response.StatusCode)})");

                                continue;
                            }

                            var content = await response.Content.ReadAsStringAsync();

                            var entry = JsonSerializer.Deserialize<NugetQueryResponseModel>(content, NUtils.JsonOptions);

                            exists.TotalHits = entry.TotalHits;

                            List<RepositoryPackageViewModel> result = new List<RepositoryPackageViewModel>();

                            foreach (var newPackage in entry.Data)
                            {
                                RepositoryPackageViewModel package = InstalledPackages.Find(x => x.PackageQueryInfo.Id.Equals(newPackage.Id));

                                if (package == null)
                                    package = new RepositoryPackageViewModel();

                                package.PackageQueryInfo = newPackage;

                                package.PackageQueryInfo.Description = ShrinkDescription(package.PackageQueryInfo.Description);

                                result.Add(package);
                            }

                            exists.Packages.AddRange(result);
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    NUtils.LogError(settings, ex.ToString());
                }
            }

            threadOperationLocker.Release();

            if (onFinished != null && !cancellationToken.IsCancellationRequested)
                onFinished();
        }

        public async void PackageRegistrationRequestAsync(PackageInstallProcessData package, Action onFinished, CancellationToken cancellationToken)
            => await PackageRegistrationRequest(package.RepositoryPackage, onFinished, cancellationToken);

        public async void PackageRegistrationRequestAsync(RepositoryPackageViewModel package, Action onFinished, CancellationToken cancellationToken)
            => await PackageRegistrationRequest(package, onFinished, cancellationToken);

        public async Task PackageRegistrationRequest(RepositoryPackageViewModel package, Action onFinished, CancellationToken cancellationToken)
        {
            try { await threadOperationLocker.WaitAsync(cancellationToken); } catch { return; }

            if (package.Registration == null)
                package.Registration = new NugetRegistrationResponseModel() { Items = new List<NugetRegistrationPageModel>() };

            foreach (var item in nuGetIndexMap)
            {
                var supporedSource = item.Value.Resources
                    .FirstOrDefault(x => NugetServiceTypes.RegistrationsBaseUrl.Contains(x.Type));

                var repo = repositories.FirstOrDefault(x => x.Name == item.Key);

                if (supporedSource == null)
                {
                    NUtils.LogError(settings, $"Not found valid source in repository {repo.Name}({repo.Value})");
                    continue;
                }

                try
                {
                    using (HttpClient client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(10);

                        using (var response = await client.GetAsync(supporedSource.Url.TrimEnd('/') + $"/{package.PackageQueryInfo.Id.ToLower()}/index.json"))
                        {
                            if (!response.IsSuccessStatusCode)
                                continue;

                            var content = await response.Content.ReadAsStringAsync();

                            var entry = JsonSerializer.Deserialize<NugetRegistrationResponseModel>(content, NUtils.JsonOptions);

                            package.Registration.Items.AddRange(entry.Items);
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    NUtils.LogError(settings, ex.ToString());
                }
            }

            threadOperationLocker.Release();

            if (onFinished != null && !cancellationToken.IsCancellationRequested)
                onFinished();
        }

        public async void PackageVersionsRequestAsync(string name, Action<bool, IEnumerable<string>> onSuccess, CancellationToken cancellationToken)
            => await PackageVersionsRequest(name, onSuccess, cancellationToken);

        public async void PackageVersionsRequestAsync(RepositoryPackageViewModel package, Action<bool, IEnumerable<string>> onSuccess, CancellationToken cancellationToken)
            => await PackageVersionsRequest(package, onSuccess, cancellationToken);

        public Task PackageVersionsRequest(RepositoryPackageViewModel package, Action<bool, IEnumerable<string>> onSuccess, CancellationToken cancellationToken)
        {
            if (package.VersionsReceived.HasValue && package.VersionsReceived.Value.AddMinutes(10) > DateTime.UtcNow)
            {

                onSuccess(false, package.Versions.Select(x=>x.ToString()));
                return Task.CompletedTask;
            }

            return PackageVersionsRequest(package.PackageQueryInfo.Id, onSuccess, cancellationToken);
        }

        public async Task PackageVersionsRequest(string name, Action<bool, IEnumerable<string>> onSuccess, CancellationToken cancellationToken)
        {
            try { await threadOperationLocker.WaitAsync(cancellationToken); } catch { return; }

            List<(string, string[])> result = new List<(string, string[])>();

            foreach (var item in nuGetIndexMap)
            {
                var supporedSource = item.Value.Resources
                    .FirstOrDefault(x => NugetServiceTypes.PackageBaseAddress.Contains(x.Type));

                var repo = repositories.FirstOrDefault(x => x.Name == item.Key);

                if (supporedSource == null)
                {
                    NUtils.LogError(settings, $"Not found valid source in repository {repo.Name}({repo.Value})");
                    continue;
                }

                try
                {
                    using (HttpClient client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(10);

                        using (var response = await client.GetAsync(supporedSource.Url.TrimEnd('/') + $"/{name.ToLower()}/index.json"))
                        {
                            if (!response.IsSuccessStatusCode)
                                continue;

                            var content = await response.Content.ReadAsStringAsync();

                            var entry = JsonSerializer.Deserialize<NugetFlatPackageVersionsResponseModel>(content, NUtils.JsonOptions);

                            result.Add((item.Key, entry.Versions));
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    NUtils.LogError(settings, ex.ToString());
                }
            }

            threadOperationLocker.Release();

            if (onSuccess != null && !cancellationToken.IsCancellationRequested)
                onSuccess(true, result.SelectMany(x => x.Item2).ToList());
        }

        public async Task<MemoryStream> DownloadRequest(string url)
        {
            await threadOperationLocker.WaitAsync();

            MemoryStream ms = default;

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);

                    using (var response = await client.GetAsync(url))
                    {
                        if (!response.IsSuccessStatusCode)
                            return null;

                        ms = new MemoryStream();

                        await response.Content.CopyToAsync(ms);
                    }
                }
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception ex)
            {
                NUtils.LogError(settings, ex.ToString());
            }

            threadOperationLocker.Release();

            return ms;
        }
    }
}

#endif