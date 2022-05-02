using NU.Core;
using NU.Core.Models.Response;
using NuGet.Versioning;
using PlasticPipe.PlasticProtocol.Server.Stubs;
using System;
using System.Collections.Concurrent;
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
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Configuration;
using UnityEditor;
using UnityEditor.Sprites;
using UnityEngine;
using NUtils = NugetV3Utils;

internal class NugetV3Local
{

    //private const string SupportedFramework = ".NETStandard2.0";

    private const string PackagesInstalledSubDir = "Installed";

    private const string DepsInstalledSubDir = "InstalledDep";

    private string GetNugetDir() => Path.Combine(Application.dataPath, "..", "Packages", "Nuget");

    private string GetNugetInstalledDir() => Path.Combine(Application.dataPath, settings.RelativePackagePath);

    private string GetNugetInstalledPackagesDir() => Path.Combine(GetNugetInstalledDir(), PackagesInstalledSubDir);

    private string GetNugetInstalledDepDir() => Path.Combine(GetNugetInstalledDir(), DepsInstalledSubDir);

    private string DefaultPackagesNugetPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

    private NugetV3Window window;

    public NugetV3Local(NugetV3Window nugetV3Window)
    {
        this.window = nugetV3Window;
    }

    #region Handle

    CancellationTokenSource refresh_cts = new CancellationTokenSource();

    CancellationTokenSource details_cts = new CancellationTokenSource();

    internal void UpdateSettings(List<NugetRepositorySource> nugetRepositorySources, List<NugetHandmakeInstalled> installed, NugetSettings nugetSettings)
    {
        repositories = nugetRepositorySources;

        handmadeInstalled = installed;

        settings = nugetSettings;

        SerializeRepositoryes();

        SerializeHandmadeInstalled();

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

                package.Versions = versions.SelectMany(x => x.Item2).Distinct().Reverse().ToList();
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
        var installed = GetInstalledPackage(package.Package.Id);

        if (installed == null)
            await InstallPackage(package);
        else
            await RemovePackage(installed);
    }

    #endregion

    #region Nuget

    private NugetSettings settings;

    private List<NugetRepositorySource> repositories = new List<NugetRepositorySource>();

    private List<NugetHandmakeInstalled> handmadeInstalled = new List<NugetHandmakeInstalled>();

    private Dictionary<string, NugetIndexResponseModel> nuGetIndexMap = new Dictionary<string, NugetIndexResponseModel>();

    private Dictionary<string, RepositoryPackagesViewModel> nugetQueryMap = new Dictionary<string, RepositoryPackagesViewModel>();

    private void SerializeRepositoryes() => File.WriteAllText(Path.Combine(GetNugetDir(), "Sources.json"), JsonSerializer.Serialize(repositories, NUtils.JsonOptions));

    private void SerializeHandmadeInstalled() => File.WriteAllText(Path.Combine(GetNugetDir(), "HandmadeInstalled.json"), JsonSerializer.Serialize(handmadeInstalled, NUtils.JsonOptions));

    private void SerializeSettings() => File.WriteAllText(Path.Combine(GetNugetDir(), "Settings.json"), JsonSerializer.Serialize(settings, NUtils.JsonOptions));

    private void LoadInstalled()
    {
        LoadInstalledPackages();
        LoadInstalledDepedency();
    }

    private List<InstalledPackage> LoadPackageCatalog(string catalog)
    {
        var result = new List<InstalledPackage>();

        var di = new DirectoryInfo(catalog);

        if (di.Exists)
        {
            foreach (var item in di.GetFiles("catalog.json", SearchOption.AllDirectories))
            {
                result.Add(JsonSerializer.Deserialize<InstalledPackage>(File.ReadAllText(item.FullName)));
            }
        }

        return result;
    }

    private void LoadInstalledPackages()
    {
        InstalledPackages = LoadPackageCatalog(GetNugetInstalledPackagesDir());
    }

    private void LoadInstalledDepedency()
    {
        InstalledDepPackages = LoadPackageCatalog(GetNugetInstalledDepDir());
    }

    private void InitializeNuGetDir()
    {
        var dir = new DirectoryInfo(GetNugetDir());

        if (!dir.Exists)
            dir.Create();

        var handmadeInstalledFileInfo = new FileInfo(Path.Combine(dir.FullName, "HandmadeInstalled.json"));

        if (handmadeInstalledFileInfo.Exists)
            handmadeInstalled = JsonSerializer.Deserialize<List<NugetHandmakeInstalled>>(File.ReadAllText(handmadeInstalledFileInfo.FullName), NUtils.JsonOptions);

        window.UpdateEditableHandmadeInstalled(handmadeInstalled.Select(x => x.Clone()).ToList());

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

        LoadInstalled();
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

    private async void PackageRegistrationRequestAsync(PackageInstallProcessData package, Action onFinished, CancellationToken cancellationToken)
        => await PackageRegistrationRequest(package.Package, onFinished, cancellationToken);

    private async void PackageRegistrationRequestAsync(RepositoryPackageViewModel package, Action onFinished, CancellationToken cancellationToken)
        => await PackageRegistrationRequest(package, onFinished, cancellationToken);

    private async Task PackageRegistrationRequest(RepositoryPackageViewModel package, Action onFinished, CancellationToken cancellationToken)
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

    private async Task<MemoryStream> DownloadRequest(string url)
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

    public InstalledPackage GetInstalledPackage(string name)
        => InstalledPackages.FirstOrDefault(x => name.Equals(x.VersionCatalog.Id));

    public InstalledPackage GetInstalledDepPackage(string name)
        => InstalledDepPackages.FirstOrDefault(x => name.Equals(x.VersionCatalog.Id));

    public NugetHandmakeInstalled GetHandMadeInstalledPackage(string name)
        => handmadeInstalled.FirstOrDefault(x => name.Equals(x.Package, StringComparison.OrdinalIgnoreCase));

    public bool HasInstalledPackage(string name)
        => GetInstalledPackage(name) != null || GetHandMadeInstalledPackage(name) != null;

    private List<InstalledPackage> InstalledPackages = new List<InstalledPackage>();

    private List<InstalledPackage> InstalledDepPackages = new List<InstalledPackage>();

    private ConcurrentBag<PackageInstallProcessData> PackageTemp { get; } = new ConcurrentBag<PackageInstallProcessData>();

    private async Task InstallPackage(RepositoryPackageViewModel package)
    {
        var process = new PackageInstallProcessData()
        {
            Package = package,
            BuildDir = GetNewPackageTempDir()
        };

        try
        {
            if (await LoadPackage(process) && await BuildTempDir(process))
            {
                var installDir = new DirectoryInfo(GetNugetInstalledDir());

                if (!installDir.Exists)
                    installDir.Create();

                MoveDir(process.BuildDir, installDir.FullName);

                InstalledPackages.Add(process.InstalledPackage);

                InstalledDepPackages.AddRange(process.InstallList.Select(x => x.InstalledPackage));

                AssetDatabase.Refresh();
            }
        }
        catch (Exception ex)
        {
            NUtils.LogDebug(settings, ex.ToString());
        }

        window.CancelInstallProcessState();

        Directory.Delete(process.BuildDir, true);
    }

    private void MoveDir(string sourceDir, string destDir)
    {
        var sourceDI = new DirectoryInfo(sourceDir);

        foreach (var item in sourceDI.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            var destFile = new FileInfo(Path.Combine(destDir, Path.GetRelativePath(sourceDI.FullName, item.FullName)));

            if (!destFile.Directory.Exists)
                destFile.Directory.Create();

            if (destFile.Exists)
                destFile.Delete();

            item.MoveTo(destFile.FullName);
        }
    }

    private async Task<bool> BuildTempDir(PackageInstallProcessData package)
    {
        if (!await BuildPackageContent(package, package, false))
            return false;

        foreach (var item in package.InstallList)
        {
            if (!await BuildPackageContent(package, item, true))
                return false;
        }

        return true;
    }

    private async Task<bool> BuildPackageContent(PackageInstallProcessData mainPackage, PackageInstallProcessData processPackage, bool dep)
    {
        try
        {
            var nugetFileData = await DownloadRequest(processPackage.VersionCatalog.PackageContentUrl);

            if (nugetFileData == null)
                throw new Exception("todo");

            using var nugetFile = new NugetFile(nugetFileData);

            var packageDir = Path.Combine(mainPackage.BuildDir, dep ? DepsInstalledSubDir : PackagesInstalledSubDir, $"{nugetFile.Id}@{nugetFile.Version}");

            Directory.CreateDirectory(packageDir);

            nugetFile.NUSpecFile.Write(nugetFile.Id, packageDir);

            var contentPath = Path.Combine(packageDir, "content");

            Directory.CreateDirectory(contentPath);

            nugetFile.DumpFrameworkFiles(contentPath, processPackage.SelectedFramework.TrimStart('.'));

            processPackage.InstalledPackage = new InstalledPackage
            {
                VersionCatalog = processPackage.VersionCatalog,
                SelectedFrameworkDeps = processPackage.SelectedFrameworkDeps,
                SelectedVersion = processPackage.Version
            };

            File.WriteAllText(Path.Combine(packageDir, "catalog.json"), JsonSerializer.Serialize(new
            {
                processPackage.InstalledPackage.VersionCatalog,
                processPackage.InstalledPackage.SelectedFrameworkDeps,
                processPackage.InstalledPackage.SelectedVersion
            }, NUtils.JsonOptions));


            return true;
        }
        catch (Exception ex)
        {
            NUtils.LogDebug(settings, ex.ToString());
        }

        return false;
    }

    private async Task<bool> LoadPackage(PackageInstallProcessData package)
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

        bool validFound = false;

        do
        {
            try
            {
                package.InstallList.Clear();

                var newDepList = await FoundPackageDepedencies(package, package);

                package.DependecyList.AddRange(newDepList);

                validFound = true;
            }
            catch (InvalidPackageException ex)
            {
                if (package.IgnoringPackage.Exists(x => x.VersionCatalog.Id == ex.InvalidPackageInfo.VersionCatalog.Id))
                    return false;

                package.IgnoringPackage.Add(ex.InvalidPackageInfo);

                NUtils.LogDebug(settings, ex.ToString());
            }
            catch (NotFoundValidPackageInfoException ex)
            {
                //todo
                NUtils.LogDebug(settings, ex.ToString());

                return false;
            }
            catch (Exception)
            {
                throw;
            }

        } while (!validFound);

        //if (!await DownloadPackageCatalog(package, false))
        //    return false;


        return true;
    }

    private async Task<List<PackageInstallProcessData>> FoundPackageDepedencies(PackageInstallProcessData mainPackage, PackageInstallProcessData processingPackage)
    {
        var newDepList = await FoundPackageDepedency(processingPackage);

        foreach (var item in newDepList)
        {
            if (!await ProcessPackageDepedency(item, mainPackage))
                throw new InvalidPackageException($"Not Found valid deps in {item.Package.Package.Id} package in {processingPackage.Package.Package.Id}", processingPackage);

        }

        return newDepList;
    }

    private async Task<bool> ProcessPackageDepedency(PackageInstallProcessData dep, PackageInstallProcessData mainPackage)
    {
        if (dep.Package.Registration == null)
            await PackageRegistrationRequest(dep.Package, () => { }, CancellationToken.None);

        NUtils.LogDebug(settings, $"ProcessPackageDepedency {dep.Package.Package.Id}");

        var validItems = dep.Registration.Items
            .SelectMany(x => x.Items)
            .Where(x => dep.Package.Versions.Contains(x.CatalogEntry.Version))
            .OrderBy(x => dep.Package.Versions.IndexOf(x.CatalogEntry.Version))
            .ToArray();

        foreach (var depCatalog in validItems)
        {
            if (depCatalog.CatalogEntry.DependencyGroups == null)
                continue;

            var tfRange = OrderFramework.Skip(OrderFramework.IndexOf(mainPackage.SelectedFramework)).ToList();

            dep.Package.VersionCatalog = depCatalog.CatalogEntry;

            var ta = depCatalog.CatalogEntry.DependencyGroups
                .Where(x =>
                OrderFramework.Contains(x.TargetFramework) &&
                !mainPackage.IgnoringPackage.Any(z =>
                z.VersionCatalog.Id == depCatalog.CatalogEntry.Id &&
                z.VersionCatalog.Version == depCatalog.CatalogEntry.Version &&
                (z.SelectedFramework == null || x.TargetFramework.Equals(z.SelectedFramework)))
                )
                .OrderByDescending(x => x.TargetFramework.Equals(mainPackage.SelectedFramework))
                .ThenBy(x => tfRange.IndexOf(x.TargetFramework))
                .ToArray();


            dep.SelectedFrameworkDeps = ta.FirstOrDefault();

            if (dep.SelectedFrameworkDeps == null)
            {
                NUtils.LogDebug(settings, $"Cannot find target framework {mainPackage.SelectedFramework} for {depCatalog.CatalogEntry.Id}@{depCatalog.CatalogEntry.Version}");

                continue;
            }

            var hmResult = CheckPackageHandmadeCompatible(dep, depCatalog.CatalogEntry);

            if (!hmResult.HasValue)
                mainPackage.InstallList.Add(dep);
            else if (hmResult == false)
                continue;
            else
                return true;

            dep.Package.VersionCatalog = depCatalog.CatalogEntry;
            dep.Package.SelectedVersion = dep.Package.VersionCatalog.Version;

            if (dep.SelectedFrameworkDeps.Dependencies == null)
                dep.SelectedFrameworkDeps.Dependencies = new List<NugetRegistrationCatalogDepedencyModel>();

            if (dep.SelectedFrameworkDeps.Dependencies.Any())
            {
                dep.DependecyList.AddRange(await FoundPackageDepedencies(mainPackage, dep));
            }

            return true;
        }

        return false;
    }

    private bool? CheckPackageHandmadeCompatible(PackageInstallProcessData package, NugetRegistrationCatalogEntryModel catalog)
    {
        var hm = handmadeInstalled.FirstOrDefault(x => x.Package == catalog.Id);

        if (hm == null)
            return null;

        return catalog.Version == hm.Version;
    }


    private async Task<List<PackageInstallProcessData>> FoundPackageDepedency(
        PackageInstallProcessData processingPackage)
    {
        //processingPackage.VersionCatalog, processingPackage.SelectedFrameworkDeps
        var result = new List<PackageInstallProcessData>();

        foreach (var dep in processingPackage.SelectedFrameworkDeps.Dependencies)
        {
            var pkg = PackageTemp.FirstOrDefault(x => x.Package.Package.Id.Equals(dep.Name, StringComparison.OrdinalIgnoreCase) == true);

            if (pkg == null)
            {
                pkg = new PackageInstallProcessData()
                {
                    Package = new RepositoryPackageViewModel()
                    {
                        Package = new NugetQueryPackageModel()
                        {
                            Id = dep.Name
                        }
                    }
                };

                await PackageVersionsRequest(dep.Name, (versions) =>
                {
                    pkg.Package.Versions = versions.SelectMany(x => x.Item2.Select(ver => ver)).Distinct().ToList();
                }, CancellationToken.None);

                PackageTemp.Add(pkg);
            }

            pkg = pkg.Clone();

            var depVer = VersionRange.Parse(dep.Range);

            var newVerList = new List<NuGetVersion>();

            var hmPackage = handmadeInstalled.FirstOrDefault(x => x.Package.Equals(pkg.Package.Package.Id));

            if (hmPackage == null)
            {
                foreach (var item in pkg.Package.Versions)
                {
                    var nuVer = NuGetVersion.Parse(item);

                    if (!depVer.Satisfies(nuVer))
                    {
                        NUtils.LogDebug(settings, $"{dep.Name} - {depVer} no satisfies {nuVer}");
                        continue;
                    }

                    NUtils.LogDebug(settings, $"{dep.Name} - {depVer} satisfies {nuVer}");

                    newVerList.Add(nuVer);
                }

                if (newVerList.Any() == false)
                    throw new NotFoundValidPackageInfoException($"No found source with exists depedency {dep.Name} for {processingPackage.VersionCatalog.Id}@{processingPackage.VersionCatalog.Version}", processingPackage);
            }
            else
            {
                newVerList.Add(NuGetVersion.Parse(hmPackage.Version));
            }

            pkg.Package.Versions = newVerList.OrderByDescending(x => x).Select(x => x.ToString()).ToList();

            result.Add(pkg);
        }

        return result;
    }

    private Task RemovePackage(InstalledPackage ex)
    {
        var installedPath = Path.Combine(GetNugetInstalledPackagesDir(), $"{ex.VersionCatalog.Id}@{ex.VersionCatalog.Version}");

        if (
            InstalledPackages.Exists(x => x.SelectedFrameworkDeps.Dependencies.Any(z => z.Name == ex.VersionCatalog.Id)) ||
            InstalledDepPackages.Exists(x => x.SelectedFrameworkDeps.Dependencies.Any(z => z.Name == ex.VersionCatalog.Id)))
        {

            var depsPath = Path.Combine(GetNugetInstalledDepDir(), $"{ex.VersionCatalog.Id}@{ex.VersionCatalog.Version}");

            MoveDir(installedPath, depsPath);

            InstalledPackages.Remove(ex);
            InstalledDepPackages.Add(ex);
        }
        else
        {
            Directory.Delete(installedPath, true);

            InstalledPackages.Remove(ex);

            List<NugetRegistrationCatalogDepedencyModel> deps = new List<NugetRegistrationCatalogDepedencyModel>();

            LoadPackageDeps(ex, deps);

            foreach (var dep in deps)
            {
                var idep = GetInstalledDepPackage(dep.Name);

                if (idep == null)
                    continue;

                if (InstalledPackages.Exists(x => x.SelectedFrameworkDeps.Dependencies.Any(z => z.Name == idep.VersionCatalog.Id)) ||
                    InstalledDepPackages.Exists(x => !deps.Exists(z => z.Name == x.VersionCatalog.Id) && x.SelectedFrameworkDeps.Dependencies.Any(z => z.Name == idep.VersionCatalog.Id)))
                    continue;

                var depsPath = Path.Combine(GetNugetInstalledDepDir(), $"{idep.VersionCatalog.Id}@{idep.VersionCatalog.Version}");

                Directory.Delete(depsPath, true);

                InstalledDepPackages.Remove(idep);
            }
        }

        window.CancelInstallProcessState();

        AssetDatabase.Refresh();

        return Task.CompletedTask;
    }

    private void LoadPackageDeps(InstalledPackage package, List<NugetRegistrationCatalogDepedencyModel> deps)
    {
        foreach (var item in package.SelectedFrameworkDeps.Dependencies)
        {
            var d = GetInstalledDepPackage(item.Name);

            if (d == null)
                d = GetInstalledPackage(item.Name);

            deps.Add(item);

            if (d != null)
                LoadPackageDeps(d, deps);
        }
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

        public List<PackageInstallProcessData> DependecyList { get; } = new List<PackageInstallProcessData>();

        public string BuildDir { get; set; }

        public NugetRegistrationCatalogDepedencyGroupModel SelectedFrameworkDeps { get; set; }

        public string SelectedFramework => SelectedFrameworkDeps?.TargetFramework;

        public PackageInstallProcessData Clone()
        {
            return new PackageInstallProcessData()
            {
                Package = new RepositoryPackageViewModel()
                {
                    Package = Package.Package,
                    Registration = Package.Registration,
                    Versions = new List<string>(Package.Versions)

                }
            };
        }

        public List<PackageInstallProcessData> IgnoringPackage { get; } = new List<PackageInstallProcessData>();

        public List<PackageInstallProcessData> InstallList { get; } = new List<PackageInstallProcessData>();

        public InstalledPackage InstalledPackage { get; set; }
    }

    private class InvalidPackageException : Exception
    {
        public PackageInstallProcessData InvalidPackageInfo { get; }

        public InvalidPackageException(string message, PackageInstallProcessData package) : base(message)
        {
            InvalidPackageInfo = package;
        }
    }

    private class NotFoundValidPackageInfoException : Exception
    {
        public PackageInstallProcessData InvalidPackageInfo { get; }

        public NotFoundValidPackageInfoException(string message, PackageInstallProcessData package) : base(message)
        {
            InvalidPackageInfo = package;
        }
    }
}
