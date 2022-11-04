#if UNITY_EDITOR

using Codice.Client.BaseCommands.BranchExplorer;
using NU.Core;
using NU.Core.Models.Response;
using NuGet.Versioning;
using NuGetV3;
using NuGetV3.Data;
using NuGetV3.Utils.Exceptions;
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
using UnityEditor;
using UnityEditor.Sprites;
using UnityEngine;
using NUtils = NuGetV3.NugetV3Utils;

namespace NuGetV3
{
    internal partial class NugetV3Local
    {
        private Dictionary<NugetV3TabEnum, INugetQueryProcessor> PackageRepositoryMap;

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
            PackageRepositoryMap = new Dictionary<NugetV3TabEnum, INugetQueryProcessor>()
        {
            { NugetV3TabEnum.Browse, new NugetBrowseQueryProcessor(window,this) },
            { NugetV3TabEnum.Installed, new NugetInstalledPackageRepository(window,this) },
            { NugetV3TabEnum.Update, new NugetUpdatesPackageRepository(window,this) }
        };
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

            GetIndexResourcesRequestAsync();
        }

        public void OnSelectPackageButtonClick(NugetV3TabEnum tab, RepositoryPackageViewModel package)
        {
            details_cts.Cancel();

            details_cts = new CancellationTokenSource();

            var token = details_cts.Token;
            GetIndexResourcesRequestAsync(() =>
            {
                return PackageVersionsRequest(package, (updated, versions) =>
                {
                    if (!versions.Any())
                    {
                        if (package is InstalledPackageData ipd)
                        {
                            package.SetPackageVersions(Enumerable.Range(0, 1).Select(x => ipd.InstalledVersion).ToList());
                            package.Registration = new NugetRegistrationResponseModel()
                            {
                                Items = new List<NugetRegistrationPageModel>()
                                {
                                    new NugetRegistrationPageModel() {
                                        Items = new List<NugetRegistrationLeafModel>()
                                        {
                                            new NugetRegistrationLeafModel(){
                                                CatalogEntry = ipd.InstalledVersionCatalog
                                            }
                                        }
                                    }
                                }
                            };
                        }
                        window.SetPackageDetails(tab, package);
                        return;
                    }
                    PackageRegistrationRequestAsync(package, () =>
                    {
                        if (package.Registration.Items.Any() == false)
                            return;

                        if (updated)
                            package.SetPackageVersions(versions);

                        foreach (var regPage in package.Registration.Items)
                        {
                            if (regPage.Items == null)
                                regPage.Items = new List<NugetRegistrationLeafModel>();

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
            });
        }

        public void QueryAsync(NugetV3TabEnum tab, string query, bool clear = false)
        {
            refresh_cts.Cancel();

            refresh_cts = new CancellationTokenSource();

            var token = refresh_cts.Token;

            QueryTab(tab, query, token, clear);
        }

        private void QueryTab(NugetV3TabEnum tab, string query, CancellationToken token, bool clear = false)
        {
            if (!PackageRepositoryMap.TryGetValue(tab, out var processor))
            {
                window.CancelRefreshProcessState();

                return;
            }

            processor.Query(token, query, clear);
        }

        public async void OnUpdateButtonClick(RepositoryPackageViewModel package)
        {
            if (CheckPackageHandmadeCompatible(package.SelectedVersionCatalog).HasValue)
            {
                NugetV3Utils.LogError(settings, "Package contains in handmade list cannot be installed/removed/updated from Nuget package manager");

                window.CancelInstallProcessState();

                return;
            }

            var installed = GetInstalledPackage(package.PackageQueryInfo.Id);

            if (installed != null)
                await InstallPackage(package);
            else
                window.CancelInstallProcessState();
        }

        public async void OnInstallUninstallButtonClick(RepositoryPackageViewModel package)
        {
            if (CheckPackageHandmadeCompatible(package.SelectedVersionCatalog).HasValue)
            {
                NugetV3Utils.LogError(settings, "Package contains in handmade list cannot be installed/removed/updated from Nuget package manager");

                window.CancelInstallProcessState();

                return;
            }

            var installed = GetInstalledPackage(package.PackageQueryInfo.Id);

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

        private void SerializeRepositoryes() => File.WriteAllText(Path.Combine(GetNugetDir(), "Sources.json"), JsonSerializer.Serialize(repositories, NUtils.JsonOptions));

        private void SerializeHandmadeInstalled() => File.WriteAllText(Path.Combine(GetNugetDir(), "HandmadeInstalled.json"), JsonSerializer.Serialize(handmadeInstalled, NUtils.JsonOptions));

        private void SerializeSettings() => File.WriteAllText(Path.Combine(GetNugetDir(), "Settings.json"), JsonSerializer.Serialize(settings, NUtils.JsonOptions));

        private void LoadInstalled()
        {
            LoadInstalledPackages();
            LoadInstalledDepedency();

            UpdateInstalledTab();
        }

        private List<InstalledPackageData> LoadPackageCatalog(string catalog)
        {
            var result = new List<InstalledPackageData>();

            var di = new DirectoryInfo(catalog);

            if (di.Exists)
            {
                foreach (var item in di.GetFiles("catalog.json", SearchOption.AllDirectories))
                {
                    var package = JsonSerializer.Deserialize<InstalledPackageData>(File.ReadAllText(item.FullName));

                    LoadPackageFromCatalog(package);

                    result.Add(package);
                }
            }

            return result;
        }

        private void LoadPackageFromCatalog(InstalledPackageData package)
        {
            package.SelectedVersionCatalog = package.InstalledVersionCatalog;

            package.PackageQueryInfo = new NugetQueryPackageModel()
            {
                Id = package.SelectedVersionCatalog.Id,
                Version = package.SelectedVersionCatalog.Version,
                Description = ShrinkDescription(package.SelectedVersionCatalog.Description),
                Authors = package.SelectedVersionCatalog.Authors.Split(" ")
            };
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
                    new NugetRepositorySource { Name = "nuget.org", Value = "https://api.nuget.org/v3/index.json" } // -- default source
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

            QueryTab(NugetV3TabEnum.Browse, "", CancellationToken.None, true);
            QueryTab(NugetV3TabEnum.Installed, "", CancellationToken.None, true);
            QueryTab(NugetV3TabEnum.Update, "", CancellationToken.None, true);
        }

        #endregion

        #region Requests

        private SemaphoreSlim threadOperationLocker = new SemaphoreSlim(1);

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

        private static Dictionary<string, List<ApiCompatibilityLevelInfo>> frameworkCompatibility = new Dictionary<string, List<ApiCompatibilityLevelInfo>>()
        {
#if UNITY_2021_3_OR_NEWER
            { ".NETStandard2.1", new List<ApiCompatibilityLevelInfo>()
                {
                    new ApiCompatibilityLevelInfo(".NETStandard", "(,2.1]")
                }
            },
#endif
            { ".NETStandard2.0", new List<ApiCompatibilityLevelInfo>()
                {
                    new ApiCompatibilityLevelInfo(".NETStandard", "(,2.0]"),
                    new ApiCompatibilityLevelInfo(".NETFramework", "4.6.1")
                }
            },
            { ".NETStandard1.6", new List<ApiCompatibilityLevelInfo>()
                {
                    new ApiCompatibilityLevelInfo(".NETStandard", "(,1.6]"),
                    new ApiCompatibilityLevelInfo(".NETFramework", "4.6.1")
                }
            },
            { ".NETStandard1.5", new List<ApiCompatibilityLevelInfo>()
                {
                    new ApiCompatibilityLevelInfo(".NETStandard", "(,1.5]"),
                    new ApiCompatibilityLevelInfo(".NETFramework", "4.6.1")
                }
            },
            { ".NETStandard1.4", new List<ApiCompatibilityLevelInfo>()
                {
                    new ApiCompatibilityLevelInfo(".NETStandard", "(,1.4]"),
                    new ApiCompatibilityLevelInfo(".NETFramework", "4.6.1")
                }
            },
            { ".NETStandard1.3", new List<ApiCompatibilityLevelInfo>()
                {
                    new ApiCompatibilityLevelInfo(".NETStandard", "(,1.3]"),
                    new ApiCompatibilityLevelInfo(".NETFramework", "4.6")
                }
            },
            { ".NETStandard1.2", new List<ApiCompatibilityLevelInfo>()
                {
                    new ApiCompatibilityLevelInfo(".NETStandard", "(,1.2]"),
                    new ApiCompatibilityLevelInfo(".NETFramework", "4.5.1")
                }
            },
            { ".NETStandard1.1", new List<ApiCompatibilityLevelInfo>()
                {
                    new ApiCompatibilityLevelInfo(".NETStandard", "(,1.1]"),
                    new ApiCompatibilityLevelInfo(".NETFramework", "4.5")
                }
            },
            { ".NETStandard1.0", new List<ApiCompatibilityLevelInfo>()
                {
                    new ApiCompatibilityLevelInfo(".NETStandard", "(,1.0]"),
                    new ApiCompatibilityLevelInfo(".NETFramework", "4.5")
                }
            },
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

        public InstalledPackageData GetInstalledPackage(string name)
            => InstalledPackages.FirstOrDefault(x => name.Equals(x.SelectedVersionCatalog.Id));

        public InstalledPackageData GetInstalledDepPackage(string name)
            => InstalledDepPackages.FirstOrDefault(x => name.Equals(x.SelectedVersionCatalog.Id));

        public InstalledPackageData GetInstalledHandmadePackage(string name)
        {
            var hi = handmadeInstalled.FirstOrDefault(x => name.Equals(x.Package));

            return hi?.CreatePackage();
        }

        public NugetHandmakeInstalled GetHandMadeInstalledPackage(string name)
            => handmadeInstalled.FirstOrDefault(x => name.Equals(x.Package, StringComparison.OrdinalIgnoreCase));

        public bool HasInstalledPackage(string name)
            => GetInstalledPackage(name) != null || GetHandMadeInstalledPackage(name) != null;

        private List<InstalledPackageData> InstalledPackages = new List<InstalledPackageData>();

        private List<InstalledPackageData> InstalledDepPackages = new List<InstalledPackageData>();

        private ConcurrentBag<PackageInstallProcessData> PackageTemp { get; } = new ConcurrentBag<PackageInstallProcessData>();

        private async Task InstallPackage(RepositoryPackageViewModel package)
        {
            var process = new PackageInstallProcessData()
            {
                RepositoryPackage = package,
                BuildDir = GetNewPackageTempDir(),
                InstalledPackage = GetInstalledPackage(package.PackageQueryInfo.Id)
            };

            try
            {
                if (TryMoveDepToInstallPackage(process, GetInstalledDepPackage(package.PackageQueryInfo.Id)))
                {

                }
                else if (await BuildInstallPackageData(process) && await BuildTempDir(process) && await ProcessInstallPackage(process))
                {
                    UpdateInstalledTab();
                    UpdateUpdateTab();

                    AssetDatabase.Refresh();
                }
                else
                {
                    foreach (var item in process.ProcessExceptions)
                    {
                        NUtils.LogError(settings, item);
                    }

                    NUtils.LogError(settings, $"Cannot install/update package {process.PackageName}! Fix errors and try again...");
                }
            }
            catch (Exception ex)
            {
                NUtils.LogDebug(settings, ex.ToString());
            }

            window.CancelInstallProcessState();

            Directory.Delete(process.BuildDir, true);
        }

        private bool TryMoveDepToInstallPackage(PackageInstallProcessData process, InstalledPackageData dep)
        {
            if (dep != null)
            {
                if (dep.SelectedVersion == process.RepositoryPackage.SelectedVersion)
                {
                    MoveDepToInstalledPackage(dep);

                    AssetDatabase.Refresh();

                    return true;
                }
                else
                    process.RemovePackageList.Add(dep);
            }

            return false;
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

        private Task<bool> ProcessInstallPackage(PackageInstallProcessData process)
        {
            var installDir = new DirectoryInfo(GetNugetInstalledDir());

            if (!installDir.Exists)
                installDir.Create();

            MoveDir(process.BuildDir, installDir.FullName);


            foreach (var item in process.RemovePackageList)
            {
                DeletePackage(item);
            }


            InstalledPackages.Add(process.InstalledPackage);

            InstalledDepPackages.AddRange(process.InstallList.Select(x => x.InstalledPackage));

            window.ReplacePackageData(process.InstalledPackage);

            process.InstalledPackage.SetPackageVersions(process.RepositoryPackage.Versions);

            window.RemovePackage(NugetV3TabEnum.Update, process.InstalledPackage.PackageQueryInfo.Id);

            if (!process.RepositoryPackage.Versions.First().OriginalVersion.Equals(process.InstalledPackage.InstalledVersion.OriginalVersion))
            {
                window.AddPackage(NugetV3TabEnum.Update, process.InstalledPackage);
            }

            window.RemovePackage(NugetV3TabEnum.Installed, process.InstalledPackage.PackageQueryInfo.Id);
            window.AddPackage(NugetV3TabEnum.Installed, process.InstalledPackage);

            return Task.FromResult(true);
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

                processPackage.InstalledPackage = new InstalledPackageData
                {
                    InstalledVersionCatalog = processPackage.VersionCatalog,
                    SelectedFrameworkDeps = processPackage.SelectedFrameworkDeps
                };

                LoadPackageFromCatalog(processPackage.InstalledPackage);

                File.WriteAllText(Path.Combine(packageDir, "catalog.json"), JsonSerializer.Serialize(new
                {
                    processPackage.InstalledPackage.InstalledVersionCatalog,
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

        private async Task<bool> BuildInstallPackageData(PackageInstallProcessData package)
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
                NUtils.LogError(settings, $"Cannot find target for {string.Join(",", allowTarget.Select(x => $"{x.Name}@{x.Range}"))} for {package.VersionCatalog.Id}@{package.SelectedVersion}");

                return false;
            }

            if (await CheckPackageDeps(package, package) != CheckPackageDepsResultEnum.Compatible)
                return false;

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
                    if (package.IgnoringPackageList.Exists(x => x.VersionCatalog.Id == ex.InvalidPackageInfo.VersionCatalog.Id))
                    {
                        NUtils.LogError(settings, ex.ToString());
                        return false;
                    }
                    package.IgnoringPackageList.Add(ex.InvalidPackageInfo);

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


            return true;
        }

        private async Task<List<PackageInstallProcessData>> FoundPackageDepedencies(PackageInstallProcessData mainPackage, PackageInstallProcessData processingPackage)
        {
            var newDepList = await FoundPackageDepedency(processingPackage);

            foreach (var item in newDepList)
            {
                if (!await ProcessPackageDepedency(item, mainPackage))
                    throw new InvalidPackageException($"Not Found valid deps in {item.PackageName} package in {processingPackage.PackageName}", processingPackage);
            }

            return newDepList;
        }

        private async Task<List<PackageInstallProcessData>> FoundPackageDepedency(
            PackageInstallProcessData processingPackage)
        {
            var result = new List<PackageInstallProcessData>();

            foreach (var dep in processingPackage.SelectedFrameworkDeps.Dependencies)
            {
                var pkg = PackageTemp.FirstOrDefault(x => x.PackageName.Equals(dep.Name, StringComparison.OrdinalIgnoreCase) == true);

                if (pkg == null)
                {
                    pkg = new PackageInstallProcessData()
                    {
                        RepositoryPackage = new RepositoryPackageViewModel()
                        {
                            PackageQueryInfo = new NugetQueryPackageModel()
                            {
                                Id = dep.Name
                            }
                        },

                        InstalledPackage = GetInstalledPackage(dep.Name) ?? GetInstalledDepPackage(dep.Name) ?? GetInstalledHandmadePackage(dep.Name)
                    };

                    await PackageVersionsRequest(pkg.RepositoryPackage, (updated, versions) =>
                    {
                        if (updated)
                            pkg.RepositoryPackage.SetPackageVersions(versions);
                    }, CancellationToken.None);

                    PackageTemp.Add(pkg);
                }

                pkg = pkg.Clone();

                var depVer = VersionRange.Parse(dep.Range);

                var newVerList = new List<NuGetVersion>();

                var hmPackage = handmadeInstalled.FirstOrDefault(x => x.Package.Equals(pkg.PackageName));

                if (hmPackage == null || string.IsNullOrWhiteSpace(hmPackage.Version))
                {
                    foreach (var nuVer in pkg.RepositoryPackage.Versions)
                    {
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

                pkg.RepositoryPackage.SetPackageVersions(newVerList);

                result.Add(pkg);
            }

            return result;
        }

        private async Task<bool> ProcessPackageDepedency(PackageInstallProcessData dep, PackageInstallProcessData mainPackage)
        {
            if (dep.RepositoryPackage.Registration == null)
                await PackageRegistrationRequest(dep.RepositoryPackage, () => { }, CancellationToken.None);

            NUtils.LogDebug(settings, $"ProcessPackageDepedency {dep.PackageName}");

            if (!dep.Registration.Items.Any())
            {
                if (dep.InstalledPackage == null)
                    dep.InstalledPackage = GetInstalledHandmadePackage(dep.PackageName);

                if (dep.InstalledPackage != null)
                    dep.Registration.Items.AddRange(dep.InstalledPackage.Registration.Items);
            }

            var validItems = dep.Registration.Items
                .SelectMany(x => x.Items)
                .Where(x => dep.RepositoryPackage.Versions.Contains(NuGetVersion.Parse(x.CatalogEntry.Version)))
                .OrderBy(x => dep.RepositoryPackage.Versions.IndexOf(NuGetVersion.Parse(x.CatalogEntry.Version)))
                .ToArray();

            foreach (var depCatalog in validItems)
            {
                if (depCatalog.CatalogEntry.DependencyGroups == null)
                    continue;

                var tfRange = OrderFramework.Skip(OrderFramework.IndexOf(mainPackage.SelectedFramework)).ToList();

                dep.RepositoryPackage.SelectedVersionCatalog = depCatalog.CatalogEntry;

                var ta = depCatalog.CatalogEntry.DependencyGroups
                    .Where(x => OrderFramework.Contains(x.TargetFramework)); // check contains in unity supported frameworks

                var ta2 = ta = ta.Where(x =>
                !mainPackage.IgnoringPackageList.Any(i => i.VersionCatalog.Id == depCatalog.CatalogEntry.Id &&
                i.VersionCatalog.Version == depCatalog.CatalogEntry.Version &&
                (i.SelectedFramework == null || x.TargetFramework.Equals(i.SelectedFramework)))
                ).ToArray();

                if (frameworkCompatibility.TryGetValue(mainPackage.SelectedFramework, out var compability))
                {
                    ta = ta.Where(x => compability.Any(c =>
                    {
                        if (dep.PackageName.Equals("Unity.NSL.SocketCore.Extensions.Buffer"))
                        {
                            Debug.LogWarning($"DEBUG!!!!! - TF {x.TargetFramework}, TFR {x.TargetFramework.Replace(c.Name, string.Empty)}, C {c.Range}");
                        }

                        if (!NuGetVersion.TryParse(x.TargetFramework.Replace(c.Name, string.Empty), out var cver))
                            return false;

                        return c.Range.Satisfies(cver);
                    }));

                    if (!ta.Any())
                    {

                    }

                }

                var ta3 = ta;

                var ta4 = ta = ta
                    .OrderByDescending(x => x.TargetFramework.Equals(mainPackage.SelectedFramework))
                    .ThenBy(x => tfRange.IndexOf(x.TargetFramework));


                dep.SelectedFrameworkDeps = ta.FirstOrDefault();

                if (dep.SelectedFrameworkDeps == null)
                {
                    NUtils.LogError(settings, $"Cannot find target framework {mainPackage.SelectedFramework} for {depCatalog.CatalogEntry.Id}@{depCatalog.CatalogEntry.Version}");

                    continue;
                }

                dep.RepositoryPackage.SelectedVersionCatalog = depCatalog.CatalogEntry;

                if (dep.SelectedFrameworkDeps.Dependencies == null)
                    dep.SelectedFrameworkDeps.Dependencies = new List<NugetRegistrationCatalogDepedencyModel>();

                var checkResult = await CheckPackageDeps(mainPackage, dep);

                if (checkResult == CheckPackageDepsResultEnum.Incompatible)
                    continue;

                var hmResult = CheckPackageHandmadeCompatible(depCatalog.CatalogEntry);

                if (!hmResult.HasValue)
                {
                    if (checkResult != CheckPackageDepsResultEnum.AlreadyExists)
                        mainPackage.InstallList.Add(dep);
                }
                else if (hmResult == false)
                    continue;
                else
                    return true;

                if (dep.SelectedFrameworkDeps.Dependencies.Any())
                {
                    dep.DependecyList.AddRange(await FoundPackageDepedencies(mainPackage, dep));
                }

                return true;
            }

            return false;
        }

        private enum CheckPackageDepsResultEnum
        {
            Compatible,
            Incompatible,
            AlreadyExists
        }

        private Task<CheckPackageDepsResultEnum> CheckPackageDeps(PackageInstallProcessData mainPackage, PackageInstallProcessData package)
        {
            var ipackage = GetInstalledPackage(package.PackageName);

            if (ipackage != null && ipackage.InstalledVersion != package.SelectedVersion && mainPackage == package)
            {
                mainPackage.RemovePackageList.Add(ipackage);

                return Task.FromResult(CheckPackageDepsResultEnum.Compatible);
            }
            else if (ipackage == null && mainPackage == package)
                return Task.FromResult(CheckPackageDepsResultEnum.Compatible);
            else if (ipackage != null && ipackage.InstalledVersion == package.SelectedVersion)
                return Task.FromResult(CheckPackageDepsResultEnum.AlreadyExists);
            else if (ipackage != null)
            {
                mainPackage.ProcessExceptions.Add(new ConflictPackageException($"Cannot change version for user manual installed depedency {package.PackageName}@{package.SelectedVersion}, first you need update {package.PackageName} to compatible versions or manual remove this", package));

                return Task.FromResult(CheckPackageDepsResultEnum.Incompatible);
            }

            ipackage = GetInstalledDepPackage(package.PackageName);

            if (ipackage != null && ipackage.InstalledVersion == package.SelectedVersion)
                return Task.FromResult(CheckPackageDepsResultEnum.AlreadyExists);

            var needDeps = new List<InstalledPackageData>(InstalledPackages);

            needDeps.AddRange(InstalledDepPackages);
            needDeps.AddRange(mainPackage.InstallList.Where(x => x.InstalledPackage != null).Select(x => x.InstalledPackage));

            var exDeps = needDeps.SelectMany(x => x.SelectedFrameworkDeps.Dependencies.Where(x => x.Name.Equals(package.PackageName))).ToArray();

            VersionRange tver;

            foreach (var item in exDeps)
            {
                tver = VersionRange.Parse(item.Range);

                if (!tver.Satisfies(package.SelectedVersion))
                {
                    mainPackage.ProcessExceptions.Add(new ConflictDepPackageException($"Depedency {package.PackageName}@{package.SelectedVersion} incompatible {tver} installed depedency", package));

                    return Task.FromResult(CheckPackageDepsResultEnum.Incompatible);
                }
            }

            if (!mainPackage.RemovePackageList.Contains(ipackage) && ipackage != null)
                mainPackage.RemovePackageList.Add(ipackage);

            return Task.FromResult(CheckPackageDepsResultEnum.Compatible);
        }

        private Task RemovePackage(InstalledPackageData ex)
        {
            if (CheckPackageNeedAsDep(ex))
            {
                MoveInstalledPackageToDep(ex);
            }
            else
            {
                RemoveInstalledPackage(ex);

                ProcessingRemovedPackage(ex);
            }

            window.CancelInstallProcessState();

            AssetDatabase.Refresh();

            return Task.CompletedTask;
        }

        private void LoadPackageDeps(InstalledPackageData package, List<NugetRegistrationCatalogDepedencyModel> deps)
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

        private void MoveInstalledPackageToDep(InstalledPackageData package)
        {
            if (!InstalledPackages.Contains(package))
                throw new Exception($"InstalledPackages not have {package.SelectedVersionCatalog.Id}");


            var installedPath = Path.Combine(GetNugetInstalledPackagesDir(), $"{package.Name}@{package.InstalledVersion}");

            var depsPath = Path.Combine(GetNugetInstalledDepDir(), $"{package.Name}@{package.InstalledVersion}");

            MoveDir(installedPath, depsPath);

            InstalledPackages.Remove(package);
            InstalledDepPackages.Add(package);

            UpdateInstalledTab();
        }

        private void MoveDepToInstalledPackage(InstalledPackageData package)
        {
            if (!InstalledDepPackages.Contains(package))
                throw new Exception($"InstalledDepPackages not have {package.SelectedVersionCatalog.Id}");


            var installedPath = Path.Combine(GetNugetInstalledPackagesDir(), $"{package.SelectedVersionCatalog.Id}@{package.SelectedVersionCatalog.Version}");

            var depsPath = Path.Combine(GetNugetInstalledDepDir(), $"{package.SelectedVersionCatalog.Id}@{package.SelectedVersionCatalog.Version}");

            MoveDir(depsPath, installedPath);

            InstalledDepPackages.Remove(package);
            InstalledPackages.Add(package);

            UpdateInstalledTab();
        }

        private void RemoveInstalledPackage(InstalledPackageData package)
        {
            var installedPath = Path.Combine(GetNugetInstalledPackagesDir(), $"{package.InstalledVersionCatalog.Id}@{package.InstalledVersionCatalog.Version}");

            RemoveUnityDir(installedPath);

            InstalledPackages.Remove(package);

            window.RemovePackage(NugetV3TabEnum.Update, package.PackageQueryInfo.Id);
            window.RemovePackage(NugetV3TabEnum.Installed, package.PackageQueryInfo.Id);
        }

        private void RemoveInstalledDepPackage(InstalledPackageData package)
        {
            var installedPath = Path.Combine(GetNugetInstalledDepDir(), $"{package.InstalledVersionCatalog.Id}@{package.InstalledVersionCatalog.Version}");

            RemoveUnityDir(installedPath);

            InstalledDepPackages.Remove(package);
        }

        private void ProcessingRemovedPackage(InstalledPackageData package)
        {
            List<NugetRegistrationCatalogDepedencyModel> deps = new List<NugetRegistrationCatalogDepedencyModel>();

            LoadPackageDeps(package, deps);

            foreach (var dep in deps)
            {
                var idep = GetInstalledDepPackage(dep.Name);

                if (idep == null)
                    continue;

                if (CheckPackageNeedAsDep(idep, deps))
                    continue;

                RemoveInstalledDepPackage(idep);
            }
        }

        private bool CheckPackageNeedAsDep(InstalledPackageData dep, List<NugetRegistrationCatalogDepedencyModel> ignoreDeps = null)
        {
            return InstalledPackages.Exists(x => x.SelectedFrameworkDeps.Dependencies.Any(z => z.Name == dep.SelectedVersionCatalog.Id)) ||
                InstalledDepPackages.Exists(x => (ignoreDeps == null || !ignoreDeps.Exists(z => z.Name == x.SelectedVersionCatalog.Id)) && x.SelectedFrameworkDeps.Dependencies.Any(z => z.Name == dep.SelectedVersionCatalog.Id));
        }

        private bool? CheckPackageHandmadeCompatible(NugetRegistrationCatalogEntryModel catalog)
        {
            var hm = handmadeInstalled.FirstOrDefault(x => x.Package == catalog.Id);

            if (hm == null)
                return null;

            return string.IsNullOrWhiteSpace(hm.Version) || catalog.Version == hm.Version;
        }

        private string GetNewPackageTempDir()
        {
            var dir = FileUtil.GetUniqueTempPathInProject();

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            RemoveUnityDir(dir);

            Directory.CreateDirectory(dir);

            return dir;
        }

        private string ShrinkDescription(string description)
        {
            return description.Split('\n')[0].Trim();
        }

        public void UpdateInstalledTab()
        {
            if (!InstalledPackages.Any())
            {
                window.UpdateTabTitle(NugetV3TabEnum.Installed, "Installed");
                return;
            }

            window.UpdateTabTitle(NugetV3TabEnum.Installed, $"Installed - {InstalledPackages.Count}");


            //window.Refresh();
        }

        public void UpdateUpdateTab()
        {
            var updatesEnumerable = InstalledPackages.Where(x => x.Versions != null && x.HasUpdates);

            if (!updatesEnumerable.Any())
            {
                window.UpdateTabTitle(NugetV3TabEnum.Update, "Update");
                return;
            }

            window.UpdateTabTitle(NugetV3TabEnum.Update, $"Update - {updatesEnumerable.Count()}");

            //window.Refresh();
        }

        public List<InstalledPackageData> GetInstalledPackages()
        {
            return InstalledPackages;
        }


        public void DeletePackage(InstalledPackageData package)
        {
            if (InstalledPackages.Contains(package))
            {
                RemoveInstalledPackage(package);
            }
            else
            {
                RemoveInstalledDepPackage(package);
            }
        }

        #region IOUtils

        private void MoveDir(string sourceDir, string destDir)
        {
            var sourceDI = new DirectoryInfo(sourceDir);

            foreach (var item in sourceDI.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                var destFile = new FileInfo(Path.Combine(destDir, Path.GetRelativePath(sourceDI.FullName, item.FullName)));

                if (destFile.Exists)
                    destFile.Delete();

                if (!destFile.Directory.Exists)
                    destFile.Directory.Create();

                item.MoveTo(destFile.FullName);
            }

            var meta = new FileInfo($"{sourceDI.FullName.TrimEnd('/').TrimEnd('\\')}.meta");

            if (meta.Exists)
                meta.Delete();
        }

        private void RemoveUnityDir(string path)
        {
            path = path.TrimEnd('\\').TrimEnd('/');

            if (!Directory.Exists(path))
                return;

            Directory.Delete(path, true);

            path += ".meta";

            if (File.Exists(path))
                File.Delete(path);
        }

        #endregion
    }
}

#endif