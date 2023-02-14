#if UNITY_EDITOR

using NuGet.Versioning;
using NuGetV3;
using NuGetV3.Data;
using NuGetV3.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NuGetV3
{
    [Serializable]
    public class NugetV3Window : EditorWindow
    {
        #region MenuItems

        [MenuItem("NuGet/NuGet Package Manager", false, 0)]
        protected static void DisplayNugetWindow()
        {
            GetWindow<NugetV3Window>();
        }

        #endregion

        #region Tabs

        private readonly Dictionary<NugetV3TabEnum, TabContentData> tabContentMap = new Dictionary<NugetV3TabEnum, TabContentData>()
        {
            { NugetV3TabEnum.Browse, new TabContentData(NugetV3TabEnum.Browse, false, true) },
            { NugetV3TabEnum.Installed, new TabContentData(NugetV3TabEnum.Installed, false, false) },
            { NugetV3TabEnum.Update, new TabContentData(NugetV3TabEnum.Update, true, false)},
        };

        RepositoryPackageViewModel selectedPackage
        {
            get => CurrentTabContent.SelectedPackage;
            set => CurrentTabContent.SelectedPackage = value;
        }

        List<RepositoryPackageViewModel> tabSelectedPackageList
        {
            get => CurrentTabContent.SelectedList;
        }

        List<RepositoryPackageViewModel> tabPackageList
        {
            get => CurrentTabContent.PackageList;
        }

        private string tabSearchText
        {
            get => CurrentTabContent.SearchText;
            set => CurrentTabContent.SearchText = value;
        }

        private bool tabMultipleSelection
        {
            get => CurrentTabContent.MultipleSelection;
        }

        private bool tabRefreshState
        {
            get => CurrentTabContent.RefreshState;
            set => CurrentTabContent.RefreshState = value;
        }

        private bool tabLoadMore
        {
            get => CurrentTabContent.LoadMore;
        }

        #endregion

        private void OnEnable()
        {
            LocalNuget = new NugetV3Local(this);

            foreach (var item in tabContentMap.Values)
            {
                item.OnUpdate += (tab) => { if (CurrentTab == tab.Tab) Repaint(); };
            }
            CurrentTabContent = tabContentMap[CurrentTab];

            InitializeGUI();

            LocalNuget.InitializeNuGet();

            if (CurrentTab < NugetV3TabEnum.Settings)
            {
                OnChangeTab(CurrentTab, CurrentTab);
            }
        }

        private NugetV3Local LocalNuget;

        private string SearchInputText;

        private void OnSearchButtonClick()
        {
            if (tabSearchText == SearchInputText)
                return;

            packageListBodyScroll = Vector2.zero;

            tabSearchText = SearchInputText;

            OnRefreshButtonClick();
        }

        private void OnRefreshButtonClick() { Refresh(); }

        public void Refresh()
        {
            tabRefreshState = true;

            packageListBodyScroll = Vector2.zero;

            selectedPackage = null;
            tabPackageList.Clear();

            LocalNuget.QueryAsync(CurrentTab, tabSearchText, true);
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
            ClearSearchInput();

            if (newTab < NugetV3TabEnum.Settings)
            {
                CurrentTabContent = tabContentMap[newTab];
                SearchInputText = CurrentTabContent.SearchText;
            }
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

            selectedPackage.SelectedVersionCatalog = selectedPackage.Registration.Items
            .SelectMany(x => x.Items)
            .FirstOrDefault(x => x.CatalogEntry.Version == selectedPackage.Versions[selectedVersionIdx].ToString())?.CatalogEntry;
        }

        private void OnMoreButtonClick()
        {
            LocalNuget.QueryAsync(CurrentTab, tabSearchText);
        }

        public void OnInstallUninstallButtonClick(RepositoryPackageViewModel package)
        {
            if (processingStateInstall)
                return;

            processingStateInstall = true;

            LocalNuget.OnInstallUninstallButtonClick(package);
        }

        public void OnUpdateButtonClick(IEnumerable<RepositoryPackageViewModel> packages)
        {
            if (processingStateInstall)
                return;

            processingStateInstall = true;

            LocalNuget.OnUpdateButtonClick(packages);
        }

        public void OnUpdateButtonClick(RepositoryPackageViewModel package)
        {
            if (processingStateInstall)
                return;

            processingStateInstall = true;

            LocalNuget.OnUpdateButtonClick(package);
        }


        private bool __psi = false;
        private bool processingStateInstall
        {
            get { return __psi; }
            set
            {
                if (__psi == value)
                    return;

                __psi = value;


                if (__psi)
                    EditorUtility.DisplayProgressBar("Processing update", string.Empty, 0);
                else
                    EditorUtility.ClearProgressBar();
            }
        }

        public bool CancelInstallProcessState() => processingStateInstall = false;

        public bool CancelRefreshProcessState() => tabRefreshState = false;

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
        private TabContentData CurrentTabContent;

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

        private GUIStyle loadMoreBtnStyle;
        private GUIStyle selectAllToggleStyle;
        private GUIStyle selectPkgToggleStyle;

        private void BuildStyles()
        {
            loadMoreBtnStyle = new GUIStyle(GUI.skin.button);
            loadMoreBtnStyle.stretchWidth = false;
            loadMoreBtnStyle.margin = new RectOffset(0, 0, 0, 0);
            loadMoreBtnStyle.padding = new RectOffset(0, 0, 10, 10);
            loadMoreBtnStyle.alignment = TextAnchor.MiddleCenter;

            loadMoreBtnStyle.fixedWidth = leftSideWidth - 10;

            selectAllToggleStyle = new GUIStyle(GUI.skin.toggle);

            selectAllToggleStyle.margin.bottom = 15;
            selectAllToggleStyle.margin.left = 5;

            selectPkgToggleStyle = new GUIStyle(GUI.skin.toggle);

            selectPkgToggleStyle.margin.top = 15;
            selectPkgToggleStyle.margin.left = 6;
            selectPkgToggleStyle.fixedWidth = 12;

        }

        private void OnGUI()
        {
            BuildStyles();
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

                DrawPackageList();

                GUILayout.EndHorizontal();


                leftSideWidth = bodyResizer.Process(100, position.width / 1.3f);

                DrawRigthBody(selectedPackage);

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

            if (GUILayout.Button("R", GUILayout.Width(20)) && !tabRefreshState)
                OnRefreshButtonClick();

            SearchInputText = GUILayout.TextField(SearchInputText);

            if (GUILayout.Button("Search", GUILayout.Width(HeaderHeight)))
                OnSearchButtonClick();

            GUILayout.EndHorizontal();
        }

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

        private void DrawPackageList()
        {
            if (tabPackageList.Any())
            {
                packageListBodyScroll = GUILayout.BeginScrollView(packageListBodyScroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.MinWidth(leftSideWidth));

                if (tabMultipleSelection)
                {
                    GLayoutUtils.HorizontalControlGroup(() =>
                    {
                        var selectAll = GUILayout.Toggle(CurrentTabContent.AllSelected, "Select all", selectAllToggleStyle);

                        if (GUILayout.Button("Update selected"))
                        {
                            OnUpdateButtonClick(CurrentTabContent.SelectedList);
                        }

                        if (CurrentTabContent.AllSelected != selectAll)
                        {
                            if (selectAll)
                                CurrentTabContent.SelectAll();
                            else
                                CurrentTabContent.ClearSelection();
                        }
                    });
                }

                foreach (var package in tabPackageList)
                {
                    DrawPackageItem(package);
                }

                if (!tabRefreshState && tabLoadMore)
                {
                    //todo mb. hide button if all source fully loaded
                    // GLayoutUtils.HorizontalControlGroup(() =>
                    //{
                    if (GUILayout.Button("Load more ...", loadMoreBtnStyle))
                        OnMoreButtonClick();
                    //});
                }

                GUILayout.EndScrollView();
            }
            else if (tabRefreshState)
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
                if (CurrentTabContent.MultipleSelection)
                {
                    var selected = CurrentTabContent.PackageSelected(package);

                    if (GUILayout.Toggle(selected, GUIContent.none, selectPkgToggleStyle) != selected)
                        CurrentTabContent.TogglePackageSelection(package, !selected);
                }

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
                    GUILayout.Box(package.PackageQueryInfo.Id, packageItemNameStyle);
                    GUILayout.Box(package.PackageQueryInfo.Authors[0], packageItemAvtorStyle);
                });


                GLayoutUtils.HorizontalControlGroup(() =>
                {
                    GUILayout.Label(package.PackageQueryInfo.Description, packageItemDescriptionStyle, GUILayout.MaxWidth(leftSideWidth - 200), GUILayout.MaxHeight(20), GUILayout.ExpandHeight(false));
                });

                GUILayout.Space(5);
            });
        }

        private void DrawPackageItem2ndRow(RepositoryPackageViewModel package)
        {
            GLayoutUtils.VerticalControlGroup(() =>
            {
                GUILayout.Box(package.PackageQueryInfo.Version, GUILayout.MinWidth(130));
            });
        }

        private int newSelectedVersionIdx;

        private void DrawRigthBody(RepositoryPackageViewModel package)
        {
            if (package != null)
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
                                    GUILayout.Box(package.PackageQueryInfo.Id, packageItemNameStyle);

                                    GLayoutUtils.HorizontalControlGroup(() =>
                                    {
                                        newSelectedVersionIdx = EditorGUILayout.Popup(selectedVersionIdx, package.Versions.Select(x => x.ToString()).ToArray(), GUILayout.MaxWidth(240));

                                        if (newSelectedVersionIdx != selectedVersionIdx)
                                            OnSelectedPackageVersion(newSelectedVersionIdx, selectedVersionIdx);

                                        if (package is InstalledPackageData ipd
                                        && ipd.SelectedVersion != ipd.InstalledVersion
                                        && GUILayout.Button("Update", GUILayout.Width(90)))
                                            OnUpdateButtonClick(package);

                                        if (GUILayout.Button(LocalNuget.HasInstalledPackage(package.PackageQueryInfo.Id) ? "Remove" : "Install", GUILayout.Width(90)))
                                            OnInstallUninstallButtonClick(package);
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
                                GUILayout.Box(package.PackageQueryInfo.Description, packageDetailsValueStyle);
                                GUILayout.Space(20);
                            });

                            if (package.SelectedVersionCatalog == null)
                            {
                                GUILayout.Label("Invalid package!!");
                                return;
                            }
                            GLayoutUtils.VerticalControlGroup(() =>
                            {
                                GLayoutUtils.HorizontalControlGroup(() =>
                                {
                                    GUILayout.Label("Version:", packageDetailsNameStyle);
                                    GUILayout.Space(5);
                                    GUILayout.Label(package.SelectedVersion.ToString(), packageDetailsValueStyle);
                                });
                                GLayoutUtils.HorizontalControlGroup(() =>
                                {
                                    GUILayout.Label("Author(s):", packageDetailsNameStyle);
                                    GUILayout.Space(5);
                                    GUILayout.Label(package.PackageQueryInfo.Authors[0], packageDetailsValueStyle);
                                });
                                GLayoutUtils.HorizontalControlGroup(() =>
                                {
                                    GUILayout.Label("Date published:", packageDetailsNameStyle);
                                    GUILayout.Space(5);
                                    GUILayout.Label(package.SelectedVersionCatalog.Published.ToString(), packageDetailsValueStyle);
                                });

                                GUILayout.Space(20);

                                GLayoutUtils.VerticalControlGroup(() =>
                                {
                                    GUILayout.Label("Dependencies", packageDetailsNameStyle);
                                    foreach (var group in package.SelectedVersionCatalog.DependencyGroups)
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
            tabContentMap[tab].SelectedPackage = package;

            if (CurrentTab != tab)
                return;

            OnSelectedPackageVersion(0, -1);
        }

        internal void SetBrowsePackageViewList(List<RepositoryPackageViewModel> newPackageList)
        {
            tabContentMap[NugetV3TabEnum.Browse].PackageList = newPackageList;
        }

        internal void SetInstalledPackageViewList(List<RepositoryPackageViewModel> newPackageList)
        {
            tabContentMap[NugetV3TabEnum.Installed].PackageList = newPackageList;
        }

        internal void SetUpdatesPackageViewList(List<RepositoryPackageViewModel> newPackageList)
        {
            tabContentMap[NugetV3TabEnum.Update].PackageList = newPackageList;
        }

        internal void UpdateTabTitle(NugetV3TabEnum tab, string title)
        {
            GUITabButtons[(int)tab].text = title;
        }

        internal void ReplacePackageData(RepositoryPackageViewModel package)
        {
            foreach (var tabList in tabContentMap.Values)
            {
                var oldIdx = tabList.PackageList.FindIndex(x => x.PackageQueryInfo.Id.Equals(package.PackageQueryInfo.Id));

                if (oldIdx == -1)
                    continue;

                tabList.PackageList[oldIdx] = package;
            }
        }

        internal void RemovePackage(NugetV3TabEnum tab, string id)
        {
            if (tabContentMap.TryGetValue(tab, out var tabContent))
            {
                if (tabContent.SelectedPackage != null && tabContent.SelectedPackage.PackageQueryInfo.Id.Equals(id))
                    tabContent.SelectedPackage = null;

                tabContent.PackageList.RemoveAll(x => x.PackageQueryInfo.Id.Equals(id));
            }
        }

        internal void AddPackage(NugetV3TabEnum tab, InstalledPackageData package)
        {
            if (tabContentMap.TryGetValue(tab, out var tabContent))
                tabContent.PackageList.Add(package);
        }

        #endregion
    }
}

#endif