using Assets.NuGetV3.Editor.Data.Interface;
using NU.Core;
using NU.Core.Models.Response;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.ServiceModel.Channels;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using NUtils = NugetV3Utils;

internal class NugetV3Local
{

    //private const string SupportedFramework = ".NETStandard2.0";

    private const string PackagesInstalledSubDir = "Installed";

    private const string DepsInstalledSubDir = "InstalledDep";

    private string GetNugetDir() => Path.Combine(Application.dataPath, "..", "PackagesSubDir");

    private string GetNugetInstalledDir() => Path.Combine(Application.dataPath, settings.RelativePackagePath);

    private string DefaultPackagesNugetPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

    private NugetV3Window window;

    public NugetV3Local(NugetV3Window nugetV3Window)
    {
        this.window = nugetV3Window;
    }

    #region Handle

    CancellationTokenSource refresh_cts = new CancellationTokenSource();

    CancellationTokenSource details_cts = new CancellationTokenSource();

    internal void UpdateSettings(List<NugetRepositorySource> nugetRepositorySources, NugetSettings nugetSettings)
    {
        repositories = nugetRepositorySources;

        settings = nugetSettings;

        SerializeRepositoryes();

        SerializeSettings();

        GetIndexResourcesRequest();
    }

    public void OnSelectPackageButtonClick(NugetV3TabEnum tab, RepositoryPackageViewModel package)
    {
        details_cts.Cancel();

        details_cts = new CancellationTokenSource();

        if (package == default || (package.VersionsReceived.HasValue && package.VersionsReceived > DateTime.UtcNow.AddMinutes(-10)))
        {
            window.SetPackageDetails(tab, package);
            return;
        }

        var token = details_cts.Token;

        PackageVersionsRequestAsync(package.Package.Id, (versions) =>
        {
            if (!versions.Any())
                return;

            PackageRegistrationRequestAsync(package, () =>
            {
                if (package.Registration.Items.Any() == false)
                    return;

                package.Versions = versions.SelectMany(x => x.Item2).Distinct().Reverse().ToArray();
                package.VersionsReceived = DateTime.UtcNow;

                var selectedVersion = package.Versions.Last();

                foreach (var regPage in package.Registration.Items)
                {
                    foreach (var reg in regPage.Items)
                    {
                        if (reg.CatalogEntry.DependencyGroups == null)
                            reg.CatalogEntry.DependencyGroups = new List<NugetRegistrationCatalogDepedencyGroupModel>();

                        foreach (var group in reg.CatalogEntry.DependencyGroups)
                        {
                            if (group.Dependencies == null)
                                group.Dependencies = new List<NugetRegistrationCatalogDepedencyModel>();
                        }
                    }
                }

                window.SetPackageDetails(tab, package);
            }, token);

        }, token);
    }

    public void Query(string query, bool clear = false)
    {
        if (clear)
            nugetQueryMap.Clear();

        refresh_cts.Cancel();

        refresh_cts = new CancellationTokenSource();

        var token = refresh_cts.Token;

        GetIndexResourcesRequest(() =>
        {
            QueryRequest(query, 75, () =>
            {
                window.CancelRefreshProcessState();

                List<RepositoryPackageViewModel> newPackageList = new List<RepositoryPackageViewModel>();

                foreach (var item in nugetQueryMap)
                {
                    newPackageList.AddRange(item.Value.Packages);
                }

                window.SetBrowsePackageViewList(newPackageList);

            }, token);
        });
    }

    public async void OnInstallUninstallButtonClick(RepositoryPackageViewModel package)
    {
        var install = InstalledPackages.FirstOrDefault(x => x.Package.Id == package.Package.Id);

        if (install == null)
            await InstallPackage(package);
        else
            await RemovePackage(install);
    }

    #endregion

    #region Nuget

    private NugetSettings settings;

    private List<NugetRepositorySource> repositories = new List<NugetRepositorySource>();

    private Dictionary<string, NugetIndexResponseModel> nuGetIndexMap = new Dictionary<string, NugetIndexResponseModel>();

    private Dictionary<string, RepositoryPackagesViewModel> nugetQueryMap = new Dictionary<string, RepositoryPackagesViewModel>();

    private void SerializeRepositoryes() => File.WriteAllText(Path.Combine(GetNugetDir(), "Sources.json"), JsonSerializer.Serialize(repositories, NUtils.JsonOptions));

    private void SerializeSettings() => File.WriteAllText(Path.Combine(GetNugetDir(), "Settings.json"), JsonSerializer.Serialize(settings, NUtils.JsonOptions));

    private void InitializeNuGetDir()
    {
        var dir = new DirectoryInfo(GetNugetDir());

        if (!dir.Exists)
            dir.Create();

        var sourceFileInfo = new FileInfo(Path.Combine(dir.FullName, "Sources.json"));

        if (!sourceFileInfo.Exists)
        {
            if (!repositories.Any())
                repositories = new List<NugetRepositorySource>()
                {
                    //{ "nuget.org", "https://api.nuget.org/v3/index.json" } // -- default source
                    new NugetRepositorySource()
                    {
                        Name = "tp_workload",
                        Value= "http://nuget.twicepricegroup.com/api/Package/94708437-6e0c-4a85-a8d2-f808a3947fb0-bf74b521-e71c-4d8e-97fa-e7fc5998255f-f12b95b5-0618-41d4-a4f5-d898d8b2ebc8/v3/index.json"
                    }
                };

            SerializeRepositoryes();
        }
        else
            repositories = JsonSerializer.Deserialize<List<NugetRepositorySource>>(File.ReadAllText(sourceFileInfo.FullName), NUtils.JsonOptions);

        window.UpdateEditableRepositories(repositories.Select(x => x.Clone()).ToList());

        var settingsFileInfo = new FileInfo(Path.Combine(dir.FullName, "Settings.json"));

        if (!settingsFileInfo.Exists)
        {
            if (settings == null)
                settings = new NugetSettings()
                {
                    RelativePackagePath = "Plugins/Nuget/",
                    ConsoleOutput = true
                };

            SerializeSettings();
        }
        else
            settings = JsonSerializer.Deserialize<NugetSettings>(File.ReadAllText(settingsFileInfo.FullName), NUtils.JsonOptions);

        window.editableSettings = settings.Clone();



        if (!Directory.Exists(DefaultPackagesNugetPath))
            Directory.CreateDirectory(DefaultPackagesNugetPath);
    }

    internal void InitializeNuGet()
    {
        InitializeNuGetDir();


    }

    #endregion


    #region Requests

    private SemaphoreSlim threadOperationLocker = new SemaphoreSlim(1);

    private async void GetIndexResourcesRequest(Action onFinished = null)
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
            onFinished();

    }

    private async void QueryRequest(string query, int? take, Action onFinished, CancellationToken cancellationToken)
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

                        exists.Packages.AddRange(entry.Data.Select(x => new RepositoryPackageViewModel() { Package = x }));
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

    private async void PackageRegistrationRequestAsync(RepositoryPackageViewModel package, Action onFinished, CancellationToken cancellationToken)
        => await PackageRegistrationRequest(package, onFinished, cancellationToken);

    private async Task PackageRegistrationRequest(IRegistrableEntry package, Action onFinished, CancellationToken cancellationToken)
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

                    using (var response = await client.GetAsync(supporedSource.Url.TrimEnd('/') + $"/{package.Package.Id.ToLower()}/index.json"))
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

    private async void PackageVersionsRequestAsync(string name, Action<List<(string, string[])>> onSuccess, CancellationToken cancellationToken)
        => await PackageVersionsRequest(name, onSuccess, cancellationToken);

    private async Task PackageVersionsRequest(string name, Action<List<(string, string[])>> onSuccess, CancellationToken cancellationToken)
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
            onSuccess(result);
    }

    private async Task<MemoryStream> Download(string url)
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

    #endregion

    #region Compatible

    internal struct ApiCompatibilityLevelInfo
    {
        public string Name { get; private set; }

        public VersionRange Range { get; private set; }

        public ApiCompatibilityLevelInfo(string name, string range)
        {
            Name = name;
            Range = VersionRange.Parse(range);
        }
    }

    private static Dictionary<ApiCompatibilityLevel, List<ApiCompatibilityLevelInfo>> SupportedApiVersion = new Dictionary<ApiCompatibilityLevel, List<ApiCompatibilityLevelInfo>>()
    {
#if UNITY_2021_3_OR_NEWER
        { ApiCompatibilityLevel.NET_Standard, new List<ApiCompatibilityLevelInfo> {
            new ApiCompatibilityLevelInfo(".NETStandard", "(,2.1]")
        } },
        { ApiCompatibilityLevel.NET_Unity_4_8, new List<ApiCompatibilityLevelInfo> {
            new ApiCompatibilityLevelInfo(".NETStandard", "(,2.1]"),
            new ApiCompatibilityLevelInfo(".NETFramework", "(,4.8]")
        } },

#else
        { ApiCompatibilityLevel.NET_Standard_2_0, new List<ApiCompatibilityLevelInfo> {
            new ApiCompatibilityLevelInfo(".NETStandard", "(,2.0]")
        } },
        { ApiCompatibilityLevel.NET_4_6, new List<ApiCompatibilityLevelInfo> {
            new ApiCompatibilityLevelInfo(".NETStandard", "(,2.0]"),
            new ApiCompatibilityLevelInfo(".NETFramework", "(,4.6]")
        } }
#endif
    };

    private List<string> OrderFramework = new List<string>()
    {
#if UNITY_2021_3_OR_NEWER
        ".NETStandard2.1",
#endif
        ".NETStandard2.0",
        ".NETStandard1.6",
        ".NETStandard1.5",
        ".NETStandard1.4",
        ".NETStandard1.3",
        ".NETStandard1.2",
        ".NETStandard1.1",
        ".NETStandard1.0",
#if UNITY_2021_3_OR_NEWER
        ".NETFramework4.8",
        ".NETFramework4.7.2",
        ".NETFramework4.7.1",
        ".NETFramework4.7",
        ".NETFramework4.6.2",
        ".NETFramework4.6.1",
#endif
        ".NETFramework4.6",
        ".NETFramework4.5.2",
        ".NETFramework4.5.1",
        ".NETFramework4.5",
        ".NETFramework4",
        ".NETFramework3.5",
        ".NETFramework3.0",
        ".NETFramework2.0",
        ".NETFramework1.1",
        ".NETFramework1.0",
    };

    private ApiCompatibilityLevel GetCompatibilityLevel()
        => PlayerSettings.GetApiCompatibilityLevel(EditorUserBuildSettings.selectedBuildTargetGroup);

    private ReadOnlyCollection<ApiCompatibilityLevelInfo> GetCompatibilityLevelInfo()
        => GetCompatibilityLevelInfo(GetCompatibilityLevel());

    private ReadOnlyCollection<ApiCompatibilityLevelInfo> GetCompatibilityLevelInfo(ApiCompatibilityLevel level)
    {
        if (SupportedApiVersion.TryGetValue(level, out var levelInfo))
            return new ReadOnlyCollection<ApiCompatibilityLevelInfo>(levelInfo);

        return default;
    }

    #endregion

    public RepositoryPackageViewModel GetInstalledPackage(string name)
        => InstalledPackages.FirstOrDefault(x => x.Package.Id == name);

    public bool HasInstalled(string name)
        => GetInstalledPackage(name) != null;

    private List<RepositoryPackageViewModel> InstalledPackages = new List<RepositoryPackageViewModel>();

    private List<RepositoryPackageViewModel> InstalledDepPackages = new List<RepositoryPackageViewModel>();

    private async Task InstallPackage(RepositoryPackageViewModel package)
    {
        var process = new PackageInstallProcessData()
        {
            Package = package,
            BuildDir = GetNewPackageTempDir()
        };

        if (await InstallPackage(process) && await ResolveDepedencyList(process))
        {
            try
            {
#if DEBUG
                window.CancelInstallProcessState();
                return;
#endif
                var installDir = GetNugetInstalledDir();

                if (!Directory.Exists(installDir))
                    Directory.CreateDirectory(installDir);


                Directory.Move(process.BuildDir, installDir);

            }
            catch (Exception ex)
            {
                NUtils.LogDebug(settings, ex.ToString());
            }
        }

        window.CancelInstallProcessState();

#if DEBUG
        return;
#endif

        Directory.Delete(process.BuildDir, true);
    }

    private async Task<bool> ResolveDepedencyList(PackageInstallProcessData package)
    {


        return false;
    }


    private async Task<bool> InstallPackage(PackageInstallProcessData package)
    {
        var allowTarget = GetCompatibilityLevelInfo();

        var sorted = package.VersionCatalog.DependencyGroups
            .Where(x => OrderFramework.Contains(x.TargetFramework))
            .OrderBy(x => OrderFramework.IndexOf(x.TargetFramework))
            .ToArray();

        package.SelectedFrameworkDeps = sorted
            .FirstOrDefault(x => allowTarget.Any(z => z.Range.Satisfies(NuGetVersion.Parse(x.TargetFramework.Replace(z.Name, "")))));

        if (package.SelectedFrameworkDeps == null)
        {
            NUtils.LogDebug(settings, $"Cannot find target for {string.Join(",", allowTarget.Select(x => $"{x.Name}@{x.Range}"))} for {package.VersionCatalog.Id}@{package.Version}");

            return false;
        }


        if (!await DownloadPackageCatalog(package, false))
            return false;

        return await FoundPackageDepedencies(package, package.VersionCatalog, package.SelectedFrameworkDeps);
    }

    private async Task<bool> DownloadPackageCatalog(PackageInstallProcessData package, bool dep)
    {
        try
        {
            var nugetFileData = await Download(package.VersionCatalog.PackageContentUrl);

            if (nugetFileData == null)
                throw new Exception("todo");

            using var nugetFile = new NugetFile(nugetFileData);

            var packageDir = Path.Combine(package.BuildDir, dep ? DepsInstalledSubDir : PackagesInstalledSubDir, $"{nugetFile.Id}@{package.Version}");

            Directory.CreateDirectory(packageDir);

            nugetFile.NUSpecFile.Write(nugetFile.Id, packageDir);

            var contentPath = Path.Combine(packageDir, "content");

            Directory.CreateDirectory(contentPath);

            nugetFile.DumpFrameworkFiles(contentPath, package.SelectedFramework.TrimStart('.'));

            return true;
        }
        catch (Exception ex)
        {
            NUtils.LogDebug(settings, ex.ToString());

            return false;
        }
    }

    private async Task<bool> FoundPackageDepedencies(PackageInstallProcessData package, NugetRegistrationCatalogEntryModel catalog, NugetRegistrationCatalogDepedencyGroupModel deps)
    {
        var newDepList = await FoundPackageDepedency(catalog, deps, package.DependecyList);

        package.DependecyList.AddRange(newDepList);

        foreach (var item in newDepList)
        {
            if (!await ProcessPackageDepedency(item, package))
                return false;
        }

        return true;
    }

    private async Task<bool> ProcessPackageDepedency(FoundPackage dep, PackageInstallProcessData package)
    {
        await PackageRegistrationRequest(dep, () => { }, CancellationToken.None);

        bool any = false;

        NUtils.LogDebug(settings, $"ProcessPackageDepedency {dep.Package.Id}");

        foreach (var page in dep.Registration.Items)
        {
            foreach (var depCatalog in page.Items)
            {
                if (depCatalog.CatalogEntry.DependencyGroups == null)
                    continue;

                var tfRange = OrderFramework.Skip(OrderFramework.IndexOf(package.SelectedFramework)).ToList();

                var depGroup = depCatalog.CatalogEntry.DependencyGroups
                    .Where(x => OrderFramework.Contains(x.TargetFramework))
                    .OrderBy(x => x.TargetFramework.Equals(package.SelectedFramework))
                    .ThenBy(x => tfRange.IndexOf(x.TargetFramework))
                    .FirstOrDefault();

                if (depGroup == null)
                {
                    NUtils.LogDebug(settings, $"Cannot find target framework {package.SelectedFramework} for {depCatalog.CatalogEntry.Id}@{depCatalog.CatalogEntry.Version}");

                    continue;
                }

                NUtils.LogDebug(settings, $"ProcessPackageDepedency ProcessPackageDepedencies {dep.Package.Id}@{depCatalog.CatalogEntry.Version}");

                if (depGroup.Dependencies == null)
                    depGroup.Dependencies = new List<NugetRegistrationCatalogDepedencyModel>();

                if (depGroup.Dependencies.Any() == false ||
                    await FoundPackageDepedencies(package, depCatalog.CatalogEntry, depGroup))
                    any = true;
            }
        }

        return any;
    }

    private async Task<List<FoundPackage>> FoundPackageDepedency(
        NugetRegistrationCatalogEntryModel catalog,
        NugetRegistrationCatalogDepedencyGroupModel depGroup,
        List<FoundPackage> packages)
    {
        var result = new List<FoundPackage>();

        foreach (var dep in depGroup.Dependencies)
        {
            var pkg = packages.FirstOrDefault(x => x.Package?.Id.Equals(dep.Name, StringComparison.OrdinalIgnoreCase) == true);

            if (pkg == null)
            {
                pkg = new FoundPackage()
                {
                    Package = new NugetQueryPackageModel()
                    {
                        Id = dep.Name
                    }
                };

                await PackageVersionsRequest(dep.Name, (versions) =>
                {
                    pkg.Versions = versions.SelectMany(x => x.Item2.Select(ver => new FoundPackage.VersionInfo()
                    {
                        RepoName = x.Item1,
                        Version = ver,
                        NVersion = NuGetVersion.Parse(ver)
                    })).ToList();
                }, CancellationToken.None);

                result.Add(pkg);
            }

            var depVer = VersionRange.Parse(dep.Range);

            var newVerList = new List<FoundPackage.VersionInfo>();

            foreach (var item in pkg.Versions)
            {
                if (!depVer.Satisfies(item.NVersion))
                {
                    NUtils.LogDebug(settings, $"{dep.Name} - {depVer} no satisfies {item.NVersion}");
                    continue;
                }

                newVerList.Add(item);
            }

            if (newVerList.Any() == false)
                throw new Exception($"No found source with exists depedency {dep.Name} for {catalog.Id}@{catalog.Version}");

            pkg.Versions = newVerList;
        }

        return result;
    }

    private Task RemovePackage(RepositoryPackageViewModel package)
    {
        InstalledPackages.Remove(package);

        window.CancelInstallProcessState();

        return Task.CompletedTask;
    }

    private string GetNewPackageTempDir()
    {
        var dir = @"D:\Temp\testPackage"; // change to temp

        //if(!Directory.Exists(dir))
        //    Directory.CreateDirectory(dir);

        if (Directory.Exists(dir))
            Directory.Delete(dir, true);

        Directory.CreateDirectory(dir);

        return dir;
    }

    private class PackageInstallProcessData
    {
        public RepositoryPackageViewModel Package { get; set; }

        public string Version => Package.SelectedVersion;

        public NugetRegistrationResponseModel Registration => Package.Registration;

        public NugetRegistrationCatalogEntryModel VersionCatalog => Package.VersionCatalog;

        public List<FoundPackage> DependecyList { get; } = new List<FoundPackage>();

        public string BuildDir { get; set; }

        public NugetRegistrationCatalogDepedencyGroupModel SelectedFrameworkDeps { get; set; }

        public string SelectedFramework => SelectedFrameworkDeps.TargetFramework;
    }
}
