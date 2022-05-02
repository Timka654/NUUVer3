#if UNITY_EDITOR

using NU.Core.Models.Response;
using NuGet.Versioning;
using NuGetV3.Data;
using NuGetV3.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

[Serializable]
public class NugetV3Window : EditorWindow
{
    private readonly Dictionary<NugetV3TabEnum, List<RepositoryPackageViewModel>> PackageListTabMap = new Dictionary<NugetV3TabEnum, List<RepositoryPackageViewModel>>()
    {
        { NugetV3TabEnum.Browse, new List<RepositoryPackageViewModel>() },
        { NugetV3TabEnum.Installed, new List<RepositoryPackageViewModel>() },
        { NugetV3TabEnum.Update, new List<RepositoryPackageViewModel>() },
    };

    private readonly Dictionary<NugetV3TabEnum, RepositoryPackageViewModel> SelectedPackageTabMap = new Dictionary<NugetV3TabEnum, RepositoryPackageViewModel>()
    {
        { NugetV3TabEnum.Browse, default },
        { NugetV3TabEnum.Installed, default },
        { NugetV3TabEnum.Update, default },
    };

    private readonly Dictionary<NugetV3TabEnum, string> SearchTextTabMap = new Dictionary<NugetV3TabEnum, string>()
    {
        { NugetV3TabEnum.Browse, "" },
        { NugetV3TabEnum.Installed, "" },
        { NugetV3TabEnum.Update, "" },
    };

    #region MenuItems

    [MenuItem("NuGet/NuGet Package Manager", false, 0)]
    protected static void DisplayNugetWindow()
    {
        GetWindow<NugetV3Window>();
    }

    #endregion

    private void OnEnable()
    {
        LocalNuget = new NugetV3Local(this);

        InitializeGUI();

        LocalNuget.InitializeNuGet();

        CurrentTab = NugetV3TabEnum.Browse;

        OnChangeTab(CurrentTab, CurrentTab);

        OnRefreshButtonClick();
    }

    RepositoryPackageViewModel selectedPackage
    {
        get => SelectedPackageTabMap[CurrentTab];
    }

    private string latestSearchContent;

    private NugetV3Local LocalNuget;

    private void OnSearchButtonClick()
    {
        if (latestSearchContent == SearchInputText)
            return;

        packageListBodyScroll = Vector2.zero;

        latestSearchContent = SearchInputText;

        OnRefreshButtonClick();
    }

    private void OnRefreshButtonClick()
    {
        refreshState = true;

        packageListBodyScroll = Vector2.zero;


        PackageListTabMap[CurrentTab].Clear();

        LocalNuget.Query(latestSearchContent, true);
    }

    internal void UpdateEditableRepositories(List<NugetRepositorySource> nugetRepositorySources)
    {
        editableRepositories = nugetRepositorySources;
        InitializeGuiSettings();
    }

    internal void UpdateEditableHandmadeInstalled(List<NugetHandmakeInstalled> nugetRepositorySources)
    {
        editableHandmadeInstalled = nugetRepositorySources;
        InitializeGuiSettings();
    }

    private void OnChangeTab(NugetV3TabEnum newTab, NugetV3TabEnum oldTab)
    {
        if (newTab >= NugetV3TabEnum.Settings && newTab >= NugetV3TabEnum.Settings)
            return;

        SearchTextTabMap[oldTab] = SearchInputText;
        ClearSearchInput();
        SearchInputText = SearchTextTabMap[newTab];
    }

    private void OnSettingsSaveButtonClick()
    {
        LocalNuget.UpdateSettings(
            editableRepositories.Select(x => x.Clone()).ToList(),
            editableHandmadeInstalled.Select(x => x.Clone()).ToList(),
            editableSettings.Clone());
    }

    private void OnSelectPackageButtonClick(RepositoryPackageViewModel package)
    {
        LocalNuget.OnSelectPackageButtonClick(CurrentTab, package);
    }

    private void OnSelectedPackageVersion(int newIdx, int oldIdx)
    {
        selectedVersionIdx = newIdx;
        selectedPackage.SelectedVersion = selectedPackage.Versions[newIdx];


        selectedPackage.VersionCatalog = selectedPackage.Registration.Items
        .SelectMany(x => x.Items)
        .FirstOrDefault(x => x.CatalogEntry.Version == selectedPackage.SelectedVersion)?.CatalogEntry;
    }

    private void OnMoreButtonClick()
    {
        LocalNuget.Query(latestSearchContent);
    }

    public void OnInstallUninstallButtonClick(RepositoryPackageViewModel package)
    {
        if (processingStateInstall)
            return;

        processingStateInstall = true;

        LocalNuget.OnInstallUninstallButtonClick(package);
    }

    private bool processingStateInstall = false;

    public bool CancelInstallProcessState() => processingStateInstall = false;

    public bool CancelRefreshProcessState() => refreshState = false;

    #region GUI

    #region Styles

    private GUIStyle packageItemNameStyle;

    private GUIStyle packageItemAvtorStyle;

    private GUIStyle packageItemDescriptionStyle;

    private GUIStyle packageDetailsNameStyle;

    private GUIStyle packageDetailsValueStyle;

    private void InitializeGUIStyles()
    {
        packageItemNameStyle = new GUIStyle(GUIStyle.none) { stretchWidth = false, fontSize = 17, normal = new GUIStyleState() { textColor = Color.white } };

        packageItemAvtorStyle = new GUIStyle(GUIStyle.none) { stretchWidth = false, fontSize = 12, normal = new GUIStyleState() { textColor = Color.white } };

        packageItemDescriptionStyle = new GUIStyle(GUIStyle.none) { stretchWidth = true, wordWrap = false, fontSize = 12, normal = new GUIStyleState() { textColor = Color.white }, fixedHeight = 20, stretchHeight = false };

        packageDetailsNameStyle = new GUIStyle(GUIStyle.none) { stretchWidth = false, fontSize = 13, fontStyle = FontStyle.Bold, normal = new GUIStyleState() { textColor = Color.white } };

        packageDetailsValueStyle = new GUIStyle(GUIStyle.none) { stretchWidth = false, wordWrap = true, fontSize = 13, normal = new GUIStyleState() { textColor = Color.white } };
    }

    #endregion

    #region Resources

    Sprite nuDefaultIcon;

    private void InitializeGUIResources()
    {
        var path = AssetDatabase.GetAllAssetPaths().FirstOrDefault(x => x.EndsWith("Nuget.defaultIcon.png"));

        if (path != default)
            nuDefaultIcon = AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    SerializedObject thisSO;
    SerializedProperty editableRepositoriesProperty;
    SerializedProperty editableHandmadeInstalledProperty;

    private void InitializeGuiSettings()
    {
        thisSO = new SerializedObject(this);

        editableRepositoriesProperty = thisSO.FindProperty(nameof(editableRepositories));

        editableHandmadeInstalledProperty = thisSO.FindProperty(nameof(editableHandmadeInstalled));
    }

    #endregion

    private static readonly GUIContent[] GUITabButtons = new GUIContent[]
        {
            new GUIContent("Browse"),
            new GUIContent("Installed"),
            new GUIContent("Update"),
            new GUIContent("Settings"),
        };

    private const int HeaderHeight = 50;

    private ResizeHorizontalView bodyResizer;

    private Vector2 packageListBodyScroll = new Vector2();

    private Vector2 packageDescriptionScrollPos;

    private NugetV3TabEnum CurrentTab = NugetV3TabEnum.Browse;

    private string SearchInputText;

    private bool refreshState = false;

    private float leftSideWidth = 230;

    private int selectedVersionIdx = 0;


    [SerializeField] public List<NugetRepositorySource> editableRepositories;

    public NugetSettings editableSettings { get; set; }

    [SerializeField] public List<NugetHandmakeInstalled> editableHandmadeInstalled;

    private void InitializeGUI()
    {
        InitializeGUIStyles();

        InitializeGUIResources();

        InitializeGuiSettings();

        bodyResizer = new ResizeHorizontalView(this, leftSideWidth, HeaderHeight, position.height - HeaderHeight);
    }

    private void OnGUI()
    {
        DrawTabs();
    }

    private void DrawTabs()
    {
        NugetV3TabEnum selectedTab = (NugetV3TabEnum)GUILayout.Toolbar((int)CurrentTab, GUITabButtons);

        if (CurrentTab != selectedTab)
        {
            OnChangeTab(selectedTab, CurrentTab);
            CurrentTab = selectedTab;
        }

        if (CurrentTab < NugetV3TabEnum.Settings)
        {
            DrawTabTitle();

            GUILayout.BeginHorizontal();

            GUILayout.BeginHorizontal(GUILayout.Width(leftSideWidth));

            switch (selectedTab)
            {
                case NugetV3TabEnum.Browse:
                    DrawBrowseTab();
                    break;
                case NugetV3TabEnum.Installed:
                    DrawInstalledTab();
                    break;
                case NugetV3TabEnum.Update:
                    DrawUpdateTab();
                    break;
                default:
                    break;
            }

            GUILayout.EndHorizontal();


            leftSideWidth = bodyResizer.Process(100, position.width / 1.3f);

            DrawRigthBody();

            GUILayout.EndHorizontal();

            Repaint();
        }
        else
            DrawSettingsTab();
    }

    private void ClearSearchInput() => SearchInputText = string.Empty;

    private void DrawTabTitle()
    {
        GUILayout.BeginHorizontal(GUILayout.Height(30));

        if (GUILayout.Button("R", GUILayout.Width(20)) && !refreshState)
            OnRefreshButtonClick();

        SearchInputText = GUILayout.TextField(SearchInputText);

        if (GUILayout.Button("Search", GUILayout.Width(HeaderHeight)))
            OnSearchButtonClick();

        GUILayout.EndHorizontal();
    }

    private void DrawBrowseTab() => DrawPackageList(NugetV3TabEnum.Browse);

    private void DrawInstalledTab() => DrawPackageList(NugetV3TabEnum.Installed);

    private void DrawUpdateTab() => DrawPackageList(NugetV3TabEnum.Update);

    private void DrawSettingsTab()
    {
        GLayoutUtils.VerticalControlGroup(() =>
        {
            using (var check = new EditorGUI.ChangeCheckScope())
            {
                var co = editableSettings.ConsoleOutput;

                co = EditorGUILayout.Toggle(new GUIContent("Console Output"), co);

                if (check.changed)
                    editableSettings.ConsoleOutput = co;
            }

            EditorGUILayout.LabelField("Relative Packages Path");

            using (var check = new EditorGUI.ChangeCheckScope())
            {
                var rpp = editableSettings.RelativePackagePath;

                rpp = GUILayout.TextField(rpp);

                if (check.changed)
                    editableSettings.RelativePackagePath = rpp;
            }

            if (editableRepositories != null)
            {
                EditorGUILayout.PropertyField(editableRepositoriesProperty, new GUIContent("Repository"), true); // True means show children
            }

            if (editableHandmadeInstalled != null)
            {
                EditorGUILayout.PropertyField(editableHandmadeInstalledProperty, new GUIContent("Handmade installed packages"), true); // True means show children
            }

            thisSO.ApplyModifiedProperties();

            GUILayout.Space(5);
            GLayoutUtils.HorizontalControlGroup(() =>
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("save", GUILayout.Width(100))) OnSettingsSaveButtonClick();
            });
        });
    }

    private void DrawPackageList(NugetV3TabEnum type)
    {
        if (PackageListTabMap[type].Any())
        {
            packageListBodyScroll = GUILayout.BeginScrollView(packageListBodyScroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar);

            foreach (var package in PackageListTabMap[type])
            {
                DrawPackageItem(package);
            }

            if (!refreshState)
            {
                //todo mb. hide button if all source fully loaded
                GLayoutUtils.HorizontalControlGroup(() =>
                {
                    if (GUILayout.Button("Load more ..."))
                        OnMoreButtonClick();
                });
            }

            GUILayout.EndScrollView();
        }
        else if (refreshState)
        {
            GUILayout.Box("Receive package list. Please wait ...");
            return;
        }
        else
        {
            GUILayout.Box("Package list is empty. Cannot find any packages");
            return;
        }
    }

    private void DrawPackageItem(RepositoryPackageViewModel package)
    {
        GLayoutUtils.HorizontalControlGroup(() =>
        {
            GUILayout.Box(nuDefaultIcon.texture, GUILayout.Width(38), GUILayout.Height(38));//icon

            DrawPackageItem1stRow(package);

            DrawPackageItem2ndRow(package);
        }, new GUIStyle()
        {
            normal = new GUIStyleState()
            {
                background = selectedPackage == package ? Texture2D.grayTexture : null
            },
            hover = new GUIStyleState()
            {
                background = Texture2D.grayTexture
            }
        }, GUILayout.MaxWidth(leftSideWidth - 15));

        if (Event.current.type == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
        {
            OnSelectPackageButtonClick(package);
        }
    }

    private void DrawPackageItem1stRow(RepositoryPackageViewModel package)
    {
        GLayoutUtils.VerticalControlGroup(() =>
        {
            GUILayout.Space(5);

            GLayoutUtils.HorizontalControlGroup(() =>
            {
                GUILayout.Box(package.Package.Id, packageItemNameStyle);
                GUILayout.Box(package.Package.Authors[0], packageItemAvtorStyle);
            });


            GLayoutUtils.HorizontalControlGroup(() =>
            {
                GUILayout.Label(package.Package.Description, packageItemDescriptionStyle, GUILayout.MaxWidth(leftSideWidth - 200), GUILayout.MaxHeight(20), GUILayout.ExpandHeight(false));
            });

            GUILayout.Space(5);
        });
    }

    private void DrawPackageItem2ndRow(RepositoryPackageViewModel package)
    {
        GLayoutUtils.VerticalControlGroup(() =>
        {
            GUILayout.Box(package.Package.Version, GUILayout.MinWidth(130));
        });
    }

    private int newSelectedVersionIdx;

    private void DrawRigthBody()
    {
        if (selectedPackage != null)
        {
            GLayoutUtils.HorizontalControlGroup(() =>
            {
                GUILayout.Space(5);

                GLayoutUtils.VerticalControlGroup(() =>
                {
                    GLayoutUtils.HorizontalControlGroup(() =>
                    {
                        GUILayout.Box(nuDefaultIcon.texture, GUILayout.Width(80), GUILayout.Height(80));//icon

                        GLayoutUtils.VerticalControlGroup(() =>
                        {
                            GUILayout.Box(selectedPackage.Package.Id, packageItemNameStyle);

                            GLayoutUtils.HorizontalControlGroup(() =>
                            {
                                newSelectedVersionIdx = EditorGUILayout.Popup(selectedVersionIdx, selectedPackage.Versions.ToArray(), GUILayout.MaxWidth(240));

                                if (newSelectedVersionIdx != selectedVersionIdx)
                                    OnSelectedPackageVersion(newSelectedVersionIdx, selectedVersionIdx);

                                if (GUILayout.Button(LocalNuget.HasInstalledPackage(selectedPackage.Package.Id) ? "Remove" : "Install", GUILayout.Width(90)))
                                    OnInstallUninstallButtonClick(selectedPackage);
                            });
                        });

                        //todo: check installed
                    });

                    packageDescriptionScrollPos = GLayoutUtils.ScrollViewGroup(packageDescriptionScrollPos, () =>
                    {
                        GLayoutUtils.VerticalControlGroup(() =>
                        {
                            GUILayout.Space(10);
                            GUILayout.Label("Description", packageDetailsNameStyle);
                            GUILayout.Box(selectedPackage.Package.Description, packageDetailsValueStyle);
                            GUILayout.Space(20);
                        });

                        GLayoutUtils.VerticalControlGroup(() =>
                        {
                            GLayoutUtils.HorizontalControlGroup(() =>
                            {
                                GUILayout.Label("Version:", packageDetailsNameStyle);
                                GUILayout.Space(5);
                                GUILayout.Label(selectedPackage.SelectedVersion.ToString(), packageDetailsValueStyle);
                            });
                            GLayoutUtils.HorizontalControlGroup(() =>
                            {
                                GUILayout.Label("Author(s):", packageDetailsNameStyle);
                                GUILayout.Space(5);
                                GUILayout.Label(selectedPackage.Package.Authors[0], packageDetailsValueStyle);
                            });
                            GLayoutUtils.HorizontalControlGroup(() =>
                            {
                                GUILayout.Label("Date published:", packageDetailsNameStyle);
                                GUILayout.Space(5);
                                GUILayout.Label(selectedPackage.VersionCatalog.Published.ToString(), packageDetailsValueStyle);
                            });

                            GUILayout.Space(20);

                            GLayoutUtils.VerticalControlGroup(() =>
                            {
                                GUILayout.Label("Dependencies", packageDetailsNameStyle);
                                foreach (var group in selectedPackage.VersionCatalog.DependencyGroups)
                                {
                                    GUILayout.Label($"- {group.TargetFramework}", packageDetailsValueStyle);

                                    if (!group.Dependencies.Any())
                                    {
                                        GUILayout.Label("-- No dependencies");
                                        continue;
                                    }

                                    foreach (var depedency in group.Dependencies)
                                    {
                                        GUILayout.Label($"-- {depedency.Name} {VersionRange.Parse(depedency.Range).PrettyPrint()}", packageDetailsValueStyle);
                                    }
                                }
                            });
                        });
                    });

                });

                GUILayout.Space(5);
            });
        }
    }

    internal void SetPackageDetails(NugetV3TabEnum tab, RepositoryPackageViewModel package)
    {
        SelectedPackageTabMap[tab] = package;

        if (CurrentTab != tab)
            return;

        OnSelectedPackageVersion(0, -1);
    }

    internal void SetBrowsePackageViewList(List<RepositoryPackageViewModel> newPackageList)
    {
        PackageListTabMap[NugetV3TabEnum.Browse] = newPackageList;
    }

    #endregion
}

#endif