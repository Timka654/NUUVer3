using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor.Build.Reporting;

namespace NuGetV3.Data
{
    public class TabContentData
    {
        public List<RepositoryPackageViewModel> PackageList = new List<RepositoryPackageViewModel>();

        public List<RepositoryPackageViewModel> SelectedList = new List<RepositoryPackageViewModel>();

        public RepositoryPackageViewModel SelectedPackage;

        public string SearchText;
        public readonly NugetV3TabEnum Tab;
        public readonly bool MultipleSelection;
        public readonly bool LoadMore;

        public bool RefreshState = false;

        public TabContentData(NugetV3TabEnum tab, bool multipleSelection, bool loadMore)
        {
            Tab = tab;
            MultipleSelection = multipleSelection;
            LoadMore = loadMore;
        }

        public event Action<TabContentData> OnUpdate = (tab) => { };

        public void Update()
        {
            OnUpdate(this);
        }

        public void SelectAll()
        {
            if (!MultipleSelection)
                return;

            SelectedList.AddRange(PackageList.Where(x=> !PackageSelected(x)));
        }

        public void ClearSelection()
            => SelectedList.Clear();

        public bool PackageSelected(RepositoryPackageViewModel package) => SelectedList.Contains(package);

        internal void TogglePackageSelection(RepositoryPackageViewModel package, bool selection)
        {
            if (selection)
                SelectedList.Add(package);
            else 
                SelectedList.Remove(package);
        }

        public bool AllSelected => SelectedList.Count == PackageList.Count;
    }
}
