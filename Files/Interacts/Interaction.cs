using Files.Dialogs;
using Files.Enums;
using Files.Filesystem;
using Files.Helpers;
using Files.View_Models;
using Files.Views;
using Files.Views.Pages;
using Microsoft.Toolkit.Mvvm.Input;
using Microsoft.Toolkit.Uwp.Extensions;
using Microsoft.Toolkit.Uwp.Notifications;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Foundation.Metadata;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.Storage.Streams;
using Windows.System;
using Windows.System.UserProfile;
using Windows.UI.Core;
using Windows.UI.Notifications;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using static Files.Properties;

namespace Files.Interacts
{
    public class Interaction
    {
        public readonly IFilesystemHelpers FilesystemHelpers;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly IShellPage AssociatedInstance;

        public SettingsViewModel AppSettings => App.AppSettings;

        private AppServiceConnection Connection => AssociatedInstance?.ServiceConnection;

        public Interaction(IShellPage appInstance)
        {
            AssociatedInstance = appInstance;
            FilesystemHelpers = new FilesystemHelpers(AssociatedInstance, App.CancellationToken);
        }

        public void List_ItemDoubleClick(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (!AppSettings.OpenItemsWithOneclick)
            {
                OpenSelectedItems(false);
            }
        }

        public void SetAsDesktopBackgroundItem_Click(object sender, RoutedEventArgs e)
        {
            SetAsBackground(WallpaperType.Desktop);
        }

        public void SetAsLockscreenBackgroundItem_Click(object sender, RoutedEventArgs e)
        {
            SetAsBackground(WallpaperType.LockScreen);
        }

        public async void SetAsBackground(WallpaperType type)
        {
            if (UserProfilePersonalizationSettings.IsSupported())
            {
                // Get the path of the selected file
                var sourceFile = (StorageFile)await AssociatedInstance.FilesystemViewModel.GetFileFromPathAsync(AssociatedInstance.ContentPage.SelectedItem.ItemPath);
                if (sourceFile == null)
                {
                    return;
                }

                // Get the app's local folder to use as the destination folder.
                StorageFolder localFolder = ApplicationData.Current.LocalFolder;

                // the file to the destination folder.
                // Generate unique name if the file already exists.
                // If the file you are trying to set as the wallpaper has the same name as the current wallpaper,
                // the system will ignore the request and no-op the operation
                var file = (StorageFile)await FilesystemTasks.Wrap(() => sourceFile.CopyAsync(localFolder, sourceFile.Name, NameCollisionOption.GenerateUniqueName).AsTask());
                if (file == null)
                {
                    return;
                }

                UserProfilePersonalizationSettings profileSettings = UserProfilePersonalizationSettings.Current;
                if (type == WallpaperType.Desktop)
                {
                    // Set the desktop background
                    await profileSettings.TrySetWallpaperImageAsync(file);
                }
                else if (type == WallpaperType.LockScreen)
                {
                    // Set the lockscreen background
                    await profileSettings.TrySetLockScreenImageAsync(file);
                }
            }
        }

        public RelayCommand AddNewTabToMultitaskingControl => new RelayCommand(() => OpenNewTab());

        private async void OpenNewTab()
        {
            await MainPage.AddNewTabByPathAsync(typeof(ModernShellPage), "NewTab".GetLocalized());
        }

        public async void OpenInNewWindowItem_Click()
        {
            var items = AssociatedInstance.ContentPage.SelectedItems;
            foreach (ListedItem listedItem in items)
            {
                var selectedItemPath = (listedItem as ShortcutItem)?.TargetPath ?? listedItem.ItemPath;
                var folderUri = new Uri("files-uwp:" + "?folder=" + @selectedItemPath);
                await Launcher.LaunchUriAsync(folderUri);
            }
        }

        public async void OpenDirectoryInNewTab_Click()
        {
            foreach (ListedItem listedItem in AssociatedInstance.ContentPage.SelectedItems)
            {
                await CoreWindow.GetForCurrentThread().Dispatcher.RunAsync(CoreDispatcherPriority.Low, async () =>
                {
                    await MainPage.AddNewTabByPathAsync(typeof(ModernShellPage), (listedItem as ShortcutItem)?.TargetPath ?? listedItem.ItemPath);
                });
            }
        }

        public void ItemPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (e.GetCurrentPoint(null).Properties.IsMiddleButtonPressed)
            {
                if ((e.OriginalSource as FrameworkElement)?.DataContext is ListedItem Item && Item.PrimaryItemAttribute == StorageItemTypes.Folder)
                {
                    if (Item.IsShortcutItem)
                    {
                        OpenPathInNewTab(((e.OriginalSource as FrameworkElement)?.DataContext as ShortcutItem)?.TargetPath ?? Item.ItemPath);
                    }
                    else
                    {
                        OpenPathInNewTab(Item.ItemPath);
                    }
                }
            }
        }

        public static async void OpenPathInNewTab(string path)
        {
            await MainPage.AddNewTabByPathAsync(typeof(ModernShellPage), path);
        }

        public static async Task<bool> OpenPathInNewWindowAsync(string path)
        {
            var folderUri = new Uri("files-uwp:" + "?folder=" + path);
            return await Launcher.LaunchUriAsync(folderUri);
        }

        public RelayCommand OpenDirectoryInDefaultTerminal => new RelayCommand(() => OpenDirectoryInTerminal());

        private async void OpenDirectoryInTerminal()
        {
            var terminal = AppSettings.TerminalController.Model.GetDefaultTerminal();

            if (Connection != null)
            {
                var value = new ValueSet
                {
                    { "WorkingDirectory", AssociatedInstance.FilesystemViewModel.WorkingDirectory },
                    { "Application", terminal.Path },
                    { "Arguments", string.Format(terminal.Arguments,
                       Helpers.PathNormalization.NormalizePath(AssociatedInstance.FilesystemViewModel.WorkingDirectory)) }
                };
                await Connection.SendMessageAsync(value);
            }
        }

        public void PinItem_Click(object sender, RoutedEventArgs e)
        {
            if (AssociatedInstance.ContentPage != null)
            {
                foreach (ListedItem listedItem in AssociatedInstance.ContentPage.SelectedItems)
                {
                    App.SidebarPinnedController.Model.AddItem(listedItem.ItemPath);
                }
            }
        }

        public void GetPath_Click(object sender, RoutedEventArgs e)
        {
            if (AssociatedInstance.ContentPage != null)
            {
                Clipboard.Clear();
                DataPackage data = new DataPackage();
                data.SetText(AssociatedInstance.FilesystemViewModel.WorkingDirectory);
                Clipboard.SetContent(data);
                Clipboard.Flush();
            }
        }

        public async Task InvokeWin32ComponentAsync(string applicationPath, string arguments = null, bool runAsAdmin = false, string workingDir = null)
        {
            await InvokeWin32ComponentsAsync(new List<string>() { applicationPath }, arguments, runAsAdmin, workingDir);
        }

        public async Task InvokeWin32ComponentsAsync(List<string> applicationPaths, string arguments = null, bool runAsAdmin = false, string workingDir = null)
        {
            Debug.WriteLine("Launching EXE in FullTrustProcess");
            if (Connection != null)
            {
                var value = new ValueSet
                {
                    { "WorkingDirectory", string.IsNullOrEmpty(workingDir) ? AssociatedInstance?.FilesystemViewModel?.WorkingDirectory : workingDir },
                    { "Application", applicationPaths.FirstOrDefault() },
                    { "ApplicationList", JsonConvert.SerializeObject(applicationPaths) },
                };

                if (runAsAdmin)
                {
                    value.Add("Arguments", "runas");
                }
                else
                {
                    value.Add("Arguments", arguments);
                }

                await Connection.SendMessageAsync(value);
            }
        }

        public async Task OpenShellCommandInExplorerAsync(string shellCommand)
        {
            Debug.WriteLine("Launching shell command in FullTrustProcess");
            if (Connection != null)
            {
                var value = new ValueSet();
                value.Add("ShellCommand", shellCommand);
                value.Add("Arguments", "ShellCommand");
                await Connection.SendMessageAsync(value);
            }
        }

        public async void GrantAccessPermissionHandler(IUICommand command)
        {
            await Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-broadfilesystemaccess"));
        }

        public static T FindChild<T>(DependencyObject startNode) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(startNode);
            for (int i = 0; i < count; i++)
            {
                DependencyObject current = VisualTreeHelper.GetChild(startNode, i);
                if (current.GetType().Equals(typeof(T)) || current.GetType().GetTypeInfo().IsSubclassOf(typeof(T)))
                {
                    T asType = (T)current;
                    return asType;
                }
                var retVal = FindChild<T>(current);
                if (retVal != null)
                {
                    return retVal;
                }
            }
            return null;
        }

        public static void FindChildren<T>(IList<T> results, DependencyObject startNode) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(startNode);
            for (int i = 0; i < count; i++)
            {
                DependencyObject current = VisualTreeHelper.GetChild(startNode, i);
                if (current.GetType().Equals(typeof(T)) || (current.GetType().GetTypeInfo().IsSubclassOf(typeof(T))))
                {
                    T asType = (T)current;
                    results.Add(asType);
                }
                FindChildren<T>(results, current);
            }
        }

        public static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            T parent = null;
            DependencyObject CurrentParent = VisualTreeHelper.GetParent(child);
            while (CurrentParent != null)
            {
                if (CurrentParent is T)
                {
                    parent = (T)CurrentParent;
                    break;
                }
                CurrentParent = VisualTreeHelper.GetParent(CurrentParent);
            }
            return parent;
        }

        public static TEnum GetEnum<TEnum>(string text) where TEnum : struct
        {
            if (!typeof(TEnum).GetTypeInfo().IsEnum)
            {
                throw new InvalidOperationException("Generic parameter 'TEnum' must be an enum.");
            }
            return (TEnum)Enum.Parse(typeof(TEnum), text);
        }

        public async void RunAsAdmin_Click()
        {
            if (Connection != null)
            {
                await Connection.SendMessageAsync(new ValueSet()
                {
                    { "Arguments", "InvokeVerb" },
                    { "FilePath", AssociatedInstance.ContentPage.SelectedItem.ItemPath },
                    { "Verb", "runas" }
                });
            }
        }

        public async void RunAsAnotherUser_Click()
        {
            if (Connection != null)
            {
                await Connection.SendMessageAsync(new ValueSet()
                {
                    { "Arguments", "InvokeVerb" },
                    { "FilePath", AssociatedInstance.ContentPage.SelectedItem.ItemPath },
                    { "Verb", "runasuser" }
                });
            }
        }

        public void OpenItem_Click(object sender, RoutedEventArgs e)
        {
            OpenSelectedItems(false);
        }

        public void OpenItemWithApplicationPicker_Click(object sender, RoutedEventArgs e)
        {
            OpenSelectedItems(true);
        }

        public async void OpenFileLocation_Click(object sender, RoutedEventArgs e)
        {
            var item = AssociatedInstance.ContentPage.SelectedItem as ShortcutItem;
            var folderPath = Path.GetDirectoryName(item.TargetPath);
            // Check if destination path exists
            var destFolder = await AssociatedInstance.FilesystemViewModel.GetFolderWithPathFromPathAsync(folderPath);
            if (destFolder)
            {
                AssociatedInstance.ContentFrame.Navigate(AppSettings.GetLayoutType(), new NavigationArguments()
                {
                    NavPathParam = folderPath,
                    AssociatedTabInstance = AssociatedInstance
                });
            }
            else if (destFolder == FilesystemErrorCode.ERROR_NOTFOUND)
            {
                await DialogDisplayHelper.ShowDialogAsync("FileNotFoundDialog/Title".GetLocalized(), "FileNotFoundDialog/Text".GetLocalized());
            }
            else
            {
                await DialogDisplayHelper.ShowDialogAsync("InvalidItemDialogTitle".GetLocalized(),
                    string.Format("InvalidItemDialogContent".GetLocalized()), Environment.NewLine, destFolder.ErrorCode.ToString());
            }
        }

        private async void OpenSelectedItems(bool displayApplicationPicker)
        {
            if (AssociatedInstance.FilesystemViewModel.WorkingDirectory.StartsWith(AppSettings.RecycleBinPath))
            {
                // Do not open files and folders inside the recycle bin
                return;
            }

            int selectedItemCount;
            Type sourcePageType = AssociatedInstance.CurrentPageType;
            selectedItemCount = AssociatedInstance.ContentPage.SelectedItems.Count;
            var opened = (FilesystemResult)false;
            string previousDir = AssociatedInstance.FilesystemViewModel.WorkingDirectory;

            // Access MRU List
            var mostRecentlyUsed = Windows.Storage.AccessCache.StorageApplicationPermissions.MostRecentlyUsedList;

            if (selectedItemCount == 1)
            {
                var clickedOnItem = AssociatedInstance.ContentPage.SelectedItem;
                var clickedOnItemPath = clickedOnItem.ItemPath;
                if (clickedOnItem.PrimaryItemAttribute == StorageItemTypes.Folder && !clickedOnItem.IsHiddenItem)
                {
                    var folderPath = (clickedOnItem as ShortcutItem)?.TargetPath ?? clickedOnItem.ItemPath;
                    opened = await AssociatedInstance.FilesystemViewModel.GetFolderWithPathFromPathAsync(folderPath)
                        .OnSuccess(childFolder =>
                        {
                            // Add location to MRU List
                            mostRecentlyUsed.Add(childFolder.Folder, childFolder.Path);
                        });
                    if (!opened)
                    {
                        opened = (FilesystemResult)AssociatedInstance.FilesystemViewModel.CheckFolderAccessWithWin32(folderPath);
                    }
                    if (opened)
                    {
                        await AssociatedInstance.FilesystemViewModel.SetWorkingDirectoryAsync(folderPath);
                        AssociatedInstance.NavigationToolbar.PathControlDisplayText = folderPath;

                        AssociatedInstance.FilesystemViewModel.IsFolderEmptyTextDisplayed = false;
                        AssociatedInstance.ContentFrame.Navigate(sourcePageType, new NavigationArguments()
                        {
                            NavPathParam = folderPath,
                            AssociatedTabInstance = AssociatedInstance
                        }, new SuppressNavigationTransitionInfo());
                    }
                }
                else if (clickedOnItem.IsHiddenItem)
                {
                    if (clickedOnItem.PrimaryItemAttribute == StorageItemTypes.Folder)
                    {
                        await AssociatedInstance.FilesystemViewModel.SetWorkingDirectoryAsync(clickedOnItemPath);
                        AssociatedInstance.NavigationToolbar.PathControlDisplayText = clickedOnItemPath;

                        AssociatedInstance.FilesystemViewModel.IsFolderEmptyTextDisplayed = false;
                        AssociatedInstance.ContentFrame.Navigate(sourcePageType, new NavigationArguments()
                        {
                            NavPathParam = clickedOnItemPath,
                            AssociatedTabInstance = AssociatedInstance
                        }, new SuppressNavigationTransitionInfo());
                    }
                    else if (clickedOnItem.PrimaryItemAttribute == StorageItemTypes.File)
                    {
                        await InvokeWin32ComponentAsync(clickedOnItemPath);
                    }
                }
                else if (clickedOnItem.IsShortcutItem)
                {
                    var shortcutItem = (ShortcutItem)clickedOnItem;
                    if (string.IsNullOrEmpty(shortcutItem.TargetPath))
                    {
                        await InvokeWin32ComponentAsync(shortcutItem.ItemPath);
                    }
                    else
                    {
                        if (!shortcutItem.IsUrl)
                        {
                            StorageFileWithPath childFile = await AssociatedInstance.FilesystemViewModel.GetFileWithPathFromPathAsync(shortcutItem.TargetPath);
                            if (childFile != null)
                            {
                                // Add location to MRU List
                                mostRecentlyUsed.Add(childFile.File, childFile.Path);
                            }
                        }
                        await InvokeWin32ComponentAsync(shortcutItem.TargetPath, shortcutItem.Arguments, shortcutItem.RunAsAdmin, shortcutItem.WorkingDirectory);
                    }
                    opened = (FilesystemResult)true;
                }
                else
                {
                    opened = await AssociatedInstance.FilesystemViewModel.GetFileWithPathFromPathAsync(clickedOnItem.ItemPath)
                        .OnSuccess(async childFile =>
                        {
                            // Add location to MRU List
                            mostRecentlyUsed.Add(childFile.File, childFile.Path);

                            if (displayApplicationPicker)
                            {
                                var options = new LauncherOptions
                                {
                                    DisplayApplicationPicker = true
                                };
                                await Launcher.LaunchFileAsync(childFile.File, options);
                            }
                            else
                            {
                                //try using launcher first
                                bool launchSuccess = false;

                                StorageFileQueryResult fileQueryResult = null;

                                //Get folder to create a file query (to pass to apps like Photos, Movies & TV..., needed to scroll through the folder like what Windows Explorer does)
                                StorageFolder currFolder = await AssociatedInstance.FilesystemViewModel.GetFolderFromPathAsync(Path.GetDirectoryName(clickedOnItem.ItemPath));

                                if (currFolder != null)
                                {
                                    QueryOptions queryOptions = new QueryOptions(CommonFileQuery.DefaultQuery, null);

                                    //We can have many sort entries
                                    SortEntry sortEntry = new SortEntry()
                                    {
                                        AscendingOrder = AppSettings.DirectorySortDirection == Microsoft.Toolkit.Uwp.UI.SortDirection.Ascending
                                    };

                                    //Basically we tell to the launched app to follow how we sorted the files in the directory.

                                    var sortOption = AppSettings.DirectorySortOption;

                                    switch (sortOption)
                                    {
                                        case Enums.SortOption.Name:
                                            sortEntry.PropertyName = "System.ItemNameDisplay";
                                            queryOptions.SortOrder.Clear();
                                            queryOptions.SortOrder.Add(sortEntry);
                                            break;

                                        case Enums.SortOption.DateModified:
                                            sortEntry.PropertyName = "System.DateModified";
                                            queryOptions.SortOrder.Clear();
                                            queryOptions.SortOrder.Add(sortEntry);
                                            break;

                                        //Unfortunately this is unsupported | Remarks: https://docs.microsoft.com/en-us/uwp/api/windows.storage.search.queryoptions.sortorder?view=winrt-19041
                                        //case Enums.SortOption.Size:

                                        //sortEntry.PropertyName = "System.TotalFileSize";
                                        //queryOptions.SortOrder.Clear();
                                        //queryOptions.SortOrder.Add(sortEntry);
                                        //break;

                                        //Unfortunately this is unsupported | Remarks: https://docs.microsoft.com/en-us/uwp/api/windows.storage.search.queryoptions.sortorder?view=winrt-19041
                                        //case Enums.SortOption.FileType:

                                        //sortEntry.PropertyName = "System.FileExtension";
                                        //queryOptions.SortOrder.Clear();
                                        //queryOptions.SortOrder.Add(sortEntry);
                                        //break;

                                        //Handle unsupported
                                        default:
                                            //keep the default one in SortOrder IList
                                            break;
                                    }

                                    fileQueryResult = currFolder.CreateFileQueryWithOptions(queryOptions);

                                    var options = new LauncherOptions
                                    {
                                        NeighboringFilesQuery = fileQueryResult
                                    };

                                    //Now launch file with options.
                                    launchSuccess = await Launcher.LaunchFileAsync(childFile.File, options);
                                }

                                if (!launchSuccess)
                                {
                                    await InvokeWin32ComponentAsync(clickedOnItem.ItemPath);
                                }
                            }
                        });
                }
            }
            else if (selectedItemCount > 1)
            {
                foreach (ListedItem clickedOnItem in AssociatedInstance.ContentPage.SelectedItems.Where(x => x.PrimaryItemAttribute == StorageItemTypes.File
                    && !x.IsShortcutItem))
                {
                    StorageFileWithPath childFile = await AssociatedInstance.FilesystemViewModel.GetFileWithPathFromPathAsync(clickedOnItem.ItemPath);
                    if (childFile != null)
                    {
                        // Add location to MRU List
                        mostRecentlyUsed.Add(childFile.File, childFile.Path);

                        if (displayApplicationPicker)
                        {
                            var options = new LauncherOptions
                            {
                                DisplayApplicationPicker = true
                            };
                            await Launcher.LaunchFileAsync(childFile.File, options);
                        }
                    }
                }
                if (!displayApplicationPicker)
                {
                    var applicationPath = string.Join('|', AssociatedInstance.ContentPage.SelectedItems.Where(x => x.PrimaryItemAttribute == StorageItemTypes.File).Select(x => x.ItemPath));
                    await InvokeWin32ComponentAsync(applicationPath);
                }
                foreach (ListedItem clickedOnItem in AssociatedInstance.ContentPage.SelectedItems.Where(x => x.PrimaryItemAttribute == StorageItemTypes.Folder))
                {
                    await MainPage.AddNewTabByPathAsync(typeof(ModernShellPage), (clickedOnItem as ShortcutItem)?.TargetPath ?? clickedOnItem.ItemPath);
                }
                opened = (FilesystemResult)true;
            }

            if (opened.ErrorCode == FilesystemErrorCode.ERROR_NOTFOUND)
            {
                await DialogDisplayHelper.ShowDialogAsync("FileNotFoundDialog/Title".GetLocalized(), "FileNotFoundDialog/Text".GetLocalized());
                AssociatedInstance.NavigationToolbar.CanRefresh = false;
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    var ContentOwnedViewModelInstance = AssociatedInstance.FilesystemViewModel;
                    ContentOwnedViewModelInstance.RefreshItems(previousDir);
                });
            }
        }

        public void CloseTab()
        {
            MainPage.MultitaskingControl.RemoveTab(MainPage.MultitaskingControl.Items.ElementAt(App.InteractionViewModel.TabStripSelectedIndex));
        }

        public RelayCommand OpenNewWindow => new RelayCommand(() => LaunchNewWindow());

        public async void LaunchNewWindow()
        {
            var filesUWPUri = new Uri("files-uwp:");
            await Launcher.LaunchUriAsync(filesUWPUri);
        }

        public void ShareItem_Click(object sender, RoutedEventArgs e)
        {
            DataTransferManager manager = DataTransferManager.GetForCurrentView();
            manager.DataRequested += new TypedEventHandler<DataTransferManager, DataRequestedEventArgs>(Manager_DataRequested);
            DataTransferManager.ShowShareUI();
        }

        private async void ShowProperties()
        {
            if (AssociatedInstance.ContentPage.IsItemSelected)
            {
                if (AssociatedInstance.ContentPage.SelectedItems.Count > 1)
                {
                    await OpenPropertiesWindowAsync(AssociatedInstance.ContentPage.SelectedItems);
                }
                else
                {
                    await OpenPropertiesWindowAsync(AssociatedInstance.ContentPage.SelectedItem);
                }
            }
            else
            {
                if (!Path.GetPathRoot(AssociatedInstance.FilesystemViewModel.CurrentFolder.ItemPath)
                    .Equals(AssociatedInstance.FilesystemViewModel.CurrentFolder.ItemPath, StringComparison.OrdinalIgnoreCase))
                {
                    await OpenPropertiesWindowAsync(AssociatedInstance.FilesystemViewModel.CurrentFolder);
                }
                else
                {
                    await OpenPropertiesWindowAsync(App.AppSettings.DrivesManager.Drives
                        .Single(x => x.Path.Equals(AssociatedInstance.FilesystemViewModel.CurrentFolder.ItemPath)));
                }
            }
        }

        public async Task OpenPropertiesWindowAsync(object item)
        {
            if (ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 8))
            {
                CoreApplicationView newWindow = CoreApplication.CreateNewView();
                ApplicationView newView = null;

                await newWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    Frame frame = new Frame();
                    frame.Navigate(typeof(Test), new PropertiesPageNavigationArguments()
                    {
                        Item = item,
                        AppInstanceArgument = AssociatedInstance
                    }, new SuppressNavigationTransitionInfo());
                    Window.Current.Content = frame;
                    Window.Current.Activate();

                    newView = ApplicationView.GetForCurrentView();
                    newWindow.TitleBar.ExtendViewIntoTitleBar = true;
                    newView.Title = "PropertiesTitle".GetLocalized();
                    newView.PersistedStateId = "Properties";
                    newView.SetPreferredMinSize(new Size(400, 550));
                    newView.Consolidated += delegate
                    {
                        Window.Current.Close();
                    };
                });
                bool viewShown = await ApplicationViewSwitcher.TryShowAsStandaloneAsync(newView.Id);
                // Set window size again here as sometimes it's not resized in the page Loaded event
                newView.TryResizeView(new Size(400, 550));
            }
            else
            {
                var propertiesDialog = new PropertiesDialog();
                propertiesDialog.propertiesFrame.Tag = propertiesDialog;
                propertiesDialog.propertiesFrame.Navigate(typeof(Properties), new PropertiesPageNavigationArguments()
                {
                    Item = item,
                    AppInstanceArgument = AssociatedInstance
                }, new SuppressNavigationTransitionInfo());
                await propertiesDialog.ShowAsync(ContentDialogPlacement.Popup);
            }
        }

        public void ShowPropertiesButton_Click(object sender, RoutedEventArgs e)
        {
            ShowProperties();
        }

        public void ShowFolderPropertiesButton_Click(object sender, RoutedEventArgs e)
        {
            ShowProperties();
        }

        public void PinDirectoryToSidebar(object sender, RoutedEventArgs e)
        {
            App.SidebarPinnedController.Model.AddItem(AssociatedInstance.FilesystemViewModel.WorkingDirectory);
        }

        private async void Manager_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            DataRequestDeferral dataRequestDeferral = args.Request.GetDeferral();
            List<IStorageItem> items = new List<IStorageItem>();
            DataRequest dataRequest = args.Request;

            /*dataRequest.Data.Properties.Title = "Data Shared From Files";
            dataRequest.Data.Properties.Description = "The items you selected will be shared";*/

            foreach (ListedItem item in AssociatedInstance.ContentPage.SelectedItems)
            {
                if (item.IsShortcutItem)
                {
                    if (item.IsLinkItem)
                    {
                        dataRequest.Data.Properties.Title = string.Format("ShareDialogTitle".GetLocalized(), items.First().Name);
                        dataRequest.Data.Properties.Description = "ShareDialogSingleItemDescription".GetLocalized();
                        dataRequest.Data.SetWebLink(new Uri(((ShortcutItem)item).TargetPath));
                        dataRequestDeferral.Complete();
                        return;
                    }
                }
                else if (item.PrimaryItemAttribute == StorageItemTypes.Folder)
                {
                    await AssociatedInstance.FilesystemViewModel.GetFolderFromPathAsync(item.ItemPath)
                        .OnSuccess(folderAsItem => items.Add(folderAsItem));
                }
                else
                {
                    await AssociatedInstance.FilesystemViewModel.GetFileFromPathAsync(item.ItemPath)
                        .OnSuccess(fileAsItem => items.Add(fileAsItem));
                }
            }

            if (items.Count == 1)
            {
                dataRequest.Data.Properties.Title = string.Format("ShareDialogTitle".GetLocalized(), items.First().Name);
                dataRequest.Data.Properties.Description = "ShareDialogSingleItemDescription".GetLocalized();
            }
            else if (items.Count == 0)
            {
                dataRequest.FailWithDisplayText("ShareDialogFailMessage".GetLocalized());
                dataRequestDeferral.Complete();
                return;
            }
            else
            {
                dataRequest.Data.Properties.Title = string.Format("ShareDialogTitleMultipleItems".GetLocalized(), items.Count,
                    "ItemsCount.Text".GetLocalized());
                dataRequest.Data.Properties.Description = "ShareDialogMultipleItemsDescription".GetLocalized();
            }

            dataRequest.Data.SetStorageItems(items);
            dataRequestDeferral.Complete();
        }

        public async void CreateShortcutFromItem_Click(object sender, RoutedEventArgs e)
        {
            foreach (ListedItem selectedItem in AssociatedInstance.ContentPage.SelectedItems)
            {
                if (Connection != null)
                {
                    var value = new ValueSet
                    {
                        { "Arguments", "FileOperation" },
                        { "fileop", "CreateLink" },
                        { "targetpath", selectedItem.ItemPath },
                        { "arguments", "" },
                        { "workingdir", "" },
                        { "runasadmin", false },
                        {
                            "filepath",
                            Path.Combine(AssociatedInstance.FilesystemViewModel.WorkingDirectory,
                                string.Format("ShortcutCreateNewSuffix".GetLocalized(), selectedItem.ItemName) + ".lnk")
                        }
                    };
                    await Connection.SendMessageAsync(value);
                }
            }
        }

        public async void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            await FilesystemHelpers.DeleteItemsAsync(
                AssociatedInstance.ContentPage.SelectedItems.Select((item) => new PathWithType(
                    item.ItemPath,
                    item.PrimaryItemAttribute == StorageItemTypes.File ? FilesystemItemType.File : FilesystemItemType.Directory)).ToList(),
                true, false, true);
        }

        public void RenameItem_Click(object sender, RoutedEventArgs e)
        {
            if (AssociatedInstance.ContentPage.IsItemSelected)
            {
                AssociatedInstance.ContentPage.StartRenameItem();
            }
        }

        public async Task<bool> RenameFileItemAsync(ListedItem item, string oldName, string newName)
        {
            if (oldName == newName)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(newName)
                && !Filesystem.FilesystemHelpers.ContainsRestrictedCharacters(newName)
                && !Filesystem.FilesystemHelpers.ContainsRestrictedFileName(newName))
            {
                var renamed = (FilesystemResult)false;
                if (item.PrimaryItemAttribute == StorageItemTypes.Folder)
                {
                    renamed = await AssociatedInstance.FilesystemViewModel.GetFolderFromPathAsync(item.ItemPath)
                        .OnSuccess(async (t) => await FilesystemHelpers.RenameAsync(t, newName, NameCollisionOption.FailIfExists, true));
                }
                else
                {
                    if (!AppSettings.ShowFileExtensions)
                    {
                        newName += item.FileExtension;
                    }

                    renamed = await AssociatedInstance.FilesystemViewModel.GetFileFromPathAsync(item.ItemPath)
                        .OnSuccess(async (t) => await FilesystemHelpers.RenameAsync(t, newName, NameCollisionOption.FailIfExists, true));
                }
                if (renamed == FilesystemErrorCode.ERROR_UNAUTHORIZED)
                {
                    // Try again with MoveFileFromApp
                    if (!NativeFileOperationsHelper.MoveFileFromApp(item.ItemPath, Path.Combine(Path.GetDirectoryName(item.ItemPath), newName)))
                    {
                        Debug.WriteLine(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                        return false;
                    }
                }
                else if (renamed == FilesystemErrorCode.ERROR_NOTAFILE || renamed == FilesystemErrorCode.ERROR_NOTAFOLDER)
                {
                    await DialogDisplayHelper.ShowDialogAsync("RenameError.NameInvalid.Title".GetLocalized(), "RenameError.NameInvalid.Text".GetLocalized());
                }
                else if (renamed == FilesystemErrorCode.ERROR_NAMETOOLONG)
                {
                    await DialogDisplayHelper.ShowDialogAsync("RenameError.TooLong.Title".GetLocalized(), "RenameError.TooLong.Text".GetLocalized());
                }
                else if (renamed == FilesystemErrorCode.ERROR_INUSE)
                {
                    // TODO: proper dialog, retry
                    await DialogDisplayHelper.ShowDialogAsync("FileInUseDeleteDialog/Title".GetLocalized(), "");
                }
                else if (renamed == FilesystemErrorCode.ERROR_NOTFOUND)
                {
                    await DialogDisplayHelper.ShowDialogAsync("RenameError.ItemDeleted.Title".GetLocalized(), "RenameError.ItemDeleted.Text".GetLocalized());
                }
                else if (renamed == FilesystemErrorCode.ERROR_ALREADYEXIST)
                {
                    var ItemAlreadyExistsDialog = new ContentDialog()
                    {
                        Title = "ItemAlreadyExistsDialogTitle".GetLocalized(),
                        Content = "ItemAlreadyExistsDialogContent".GetLocalized(),
                        PrimaryButtonText = "ItemAlreadyExistsDialogPrimaryButtonText".GetLocalized(),
                        SecondaryButtonText = "ItemAlreadyExistsDialogSecondaryButtonText".GetLocalized()
                    };

                    ContentDialogResult result = await ItemAlreadyExistsDialog.ShowAsync();

                    if (result == ContentDialogResult.Primary)
                    {
                        if (item.PrimaryItemAttribute == StorageItemTypes.Folder)
                        {
                            renamed = await AssociatedInstance.FilesystemViewModel.GetFolderFromPathAsync(item.ItemPath)
                                .OnSuccess(async (t) => await FilesystemHelpers.RenameAsync(t, newName, NameCollisionOption.GenerateUniqueName, true));
                        }
                        else
                        {
                            renamed = await AssociatedInstance.FilesystemViewModel.GetFileFromPathAsync(item.ItemPath)
                                .OnSuccess(async (t) => await FilesystemHelpers.RenameAsync(t, newName, NameCollisionOption.GenerateUniqueName, true));
                        }
                    }
                    else if (result == ContentDialogResult.Secondary)
                    {
                        if (item.PrimaryItemAttribute == StorageItemTypes.Folder)
                        {
                            renamed = await AssociatedInstance.FilesystemViewModel.GetFolderFromPathAsync(item.ItemPath)
                                .OnSuccess(async (t) => await FilesystemHelpers.RenameAsync(t, newName, NameCollisionOption.ReplaceExisting, true));
                        }
                        else
                        {
                            renamed = await AssociatedInstance.FilesystemViewModel.GetFileFromPathAsync(item.ItemPath)
                                .OnSuccess(async (t) => await FilesystemHelpers.RenameAsync(t, newName, NameCollisionOption.ReplaceExisting, true));
                        }
                    }
                }
                if (renamed)
                {
                    AssociatedInstance.NavigationToolbar.CanGoForward = false;
                    return true;
                }
            }
            return false;
        }

        public async void RestoreItem_Click(object sender, RoutedEventArgs e)
        {
            if (AssociatedInstance.ContentPage.IsItemSelected)
            {
                foreach (ListedItem listedItem in AssociatedInstance.ContentPage.SelectedItems)
                {
                    FilesystemItemType itemType = (listedItem as RecycleBinItem).PrimaryItemAttribute == StorageItemTypes.Folder ? FilesystemItemType.Directory : FilesystemItemType.File;
                    await FilesystemHelpers.RestoreFromTrashAsync(new PathWithType(
                        (listedItem as RecycleBinItem).ItemPath,
                        itemType), (listedItem as RecycleBinItem).ItemOriginalPath, true);
                }
            }
        }

        public async void CutItem_Click(object sender, RoutedEventArgs e)
        {
            DataPackage dataPackage = new DataPackage
            {
                RequestedOperation = DataPackageOperation.Move
            };
            List<IStorageItem> items = new List<IStorageItem>();
            var cut = (FilesystemResult)false;
            if (AssociatedInstance.ContentPage.IsItemSelected)
            {
                // First, reset DataGrid Rows that may be in "cut" command mode
                AssociatedInstance.ContentPage.ResetItemOpacity();

                foreach (ListedItem listedItem in AssociatedInstance.ContentPage.SelectedItems)
                {
                    // Dim opacities accordingly
                    AssociatedInstance.ContentPage.SetItemOpacity(listedItem);

                    if (listedItem.PrimaryItemAttribute == StorageItemTypes.File)
                    {
                        cut = await AssociatedInstance.FilesystemViewModel.GetFileFromPathAsync(listedItem.ItemPath)
                            .OnSuccess(t => items.Add(t));
                        if (!cut)
                        {
                            break;
                        }
                    }
                    else
                    {
                        cut = await AssociatedInstance.FilesystemViewModel.GetFolderFromPathAsync(listedItem.ItemPath)
                            .OnSuccess(t => items.Add(t));
                        if (!cut)
                        {
                            break;
                        }
                    }
                }
                if (cut.ErrorCode == FilesystemErrorCode.ERROR_NOTFOUND)
                {
                    AssociatedInstance.ContentPage.ResetItemOpacity();
                    return;
                }
                else if (cut.ErrorCode == FilesystemErrorCode.ERROR_UNAUTHORIZED)
                {
                    // Try again with fulltrust process
                    if (Connection != null)
                    {
                        var filePaths = string.Join('|', AssociatedInstance.ContentPage.SelectedItems.Select(x => x.ItemPath));
                        var result = await Connection.SendMessageAsync(new ValueSet()
                        {
                            { "Arguments", "FileOperation" },
                            { "fileop", "Clipboard" },
                            { "filepath", filePaths },
                            { "operation", (int)DataPackageOperation.Move }
                        });
                        if (result.Status == AppServiceResponseStatus.Success)
                        {
                            return;
                        }
                    }
                    AssociatedInstance.ContentPage.ResetItemOpacity();
                    return;
                }
            }
            if (!items.Any())
            {
                return;
            }
            dataPackage.SetStorageItems(items);
            Clipboard.SetContent(dataPackage);
            try
            {
                Clipboard.Flush();
            }
            catch
            {
                dataPackage = null;
            }
        }

        public string CopySourcePath;

        public async void CopyItem_ClickAsync(object sender, RoutedEventArgs e)
        {
            DataPackage dataPackage = new DataPackage
            {
                RequestedOperation = DataPackageOperation.Copy
            };
            List<IStorageItem> items = new List<IStorageItem>();

            CopySourcePath = AssociatedInstance.FilesystemViewModel.WorkingDirectory;
            var copied = (FilesystemResult)false;

            if (AssociatedInstance.ContentPage.IsItemSelected)
            {
                foreach (ListedItem listedItem in AssociatedInstance.ContentPage.SelectedItems)
                {
                    if (listedItem.PrimaryItemAttribute == StorageItemTypes.File)
                    {
                        copied = await AssociatedInstance.FilesystemViewModel.GetFileFromPathAsync(listedItem.ItemPath)
                            .OnSuccess(t => items.Add(t));
                        if (!copied)
                        {
                            break;
                        }
                    }
                    else
                    {
                        copied = await AssociatedInstance.FilesystemViewModel.GetFolderFromPathAsync(listedItem.ItemPath)
                            .OnSuccess(t => items.Add(t));
                        if (!copied)
                        {
                            break;
                        }
                    }
                }
                if (copied.ErrorCode == FilesystemErrorCode.ERROR_UNAUTHORIZED)
                {
                    // Try again with fulltrust process
                    if (Connection != null)
                    {
                        var filePaths = string.Join('|', AssociatedInstance.ContentPage.SelectedItems.Select(x => x.ItemPath));
                        var result = await Connection.SendMessageAsync(new ValueSet()
                        {
                            { "Arguments", "FileOperation" },
                            { "fileop", "Clipboard" },
                            { "filepath", filePaths },
                            { "operation", (int)DataPackageOperation.Copy }
                        });
                    }
                    return;
                }
            }

            if (items?.Count > 0)
            {
                dataPackage.SetStorageItems(items);
                Clipboard.SetContent(dataPackage);
                try
                {
                    Clipboard.Flush();
                }
                catch
                {
                    dataPackage = null;
                }
            }
        }

        public RelayCommand CopyPathOfSelectedItem => new RelayCommand(() => CopyLocation());

        private void CopyLocation()
        {
            if (AssociatedInstance.ContentPage != null)
            {
                Clipboard.Clear();
                DataPackage data = new DataPackage();
                data.SetText(AssociatedInstance.ContentPage.SelectedItem.ItemPath);
                Clipboard.SetContent(data);
                Clipboard.Flush();
            }
        }

        public RelayCommand CopyPathOfWorkingDirectory => new RelayCommand(() => CopyWorkingLocation());

        private void CopyWorkingLocation()
        {
            if (AssociatedInstance.ContentPage != null)
            {
                Clipboard.Clear();
                DataPackage data = new DataPackage();
                data.SetText(AssociatedInstance.FilesystemViewModel.WorkingDirectory);
                Clipboard.SetContent(data);
                Clipboard.Flush();
            }
        }

        private enum ImpossibleActionResponseTypes
        {
            Skip,
            Abort
        }

        public RelayCommand EmptyRecycleBin => new RelayCommand(() => EmptyRecycleBin_ClickAsync());

        public async void EmptyRecycleBin_ClickAsync()
        {
            var ConfirmEmptyBinDialog = new ContentDialog()
            {
                Title = "ConfirmEmptyBinDialogTitle".GetLocalized(),
                Content = "ConfirmEmptyBinDialogContent".GetLocalized(),
                PrimaryButtonText = "ConfirmEmptyBinDialog/PrimaryButtonText".GetLocalized(),
                SecondaryButtonText = "ConfirmEmptyBinDialog/SecondaryButtonText".GetLocalized()
            };

            ContentDialogResult result = await ConfirmEmptyBinDialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                if (Connection != null)
                {
                    var value = new ValueSet();
                    value.Add("Arguments", "RecycleBin");
                    value.Add("action", "Empty");
                    // Send request to fulltrust process to empty recyclebin
                    await Connection.SendMessageAsync(value);
                }
            }
        }

        public RelayCommand PasteItemsFromClipboard => new RelayCommand(async () => await PasteItemAsync());

        public async Task PasteItemAsync()
        {
            DataPackageView packageView = Clipboard.GetContent();
            string destinationPath = AssociatedInstance.FilesystemViewModel.WorkingDirectory;

            await FilesystemHelpers.PerformOperationTypeAsync(packageView.RequestedOperation, packageView, destinationPath, true);
            AssociatedInstance.FilesystemViewModel.IsFolderEmptyTextDisplayed = false;
        }

        public async void CreateFileFromDialogResultType(AddItemType itemType)
        {
            string currentPath = null;
            if (AssociatedInstance.ContentPage != null)
            {
                currentPath = AssociatedInstance.FilesystemViewModel.WorkingDirectory;
            }

            // Show rename dialog
            RenameDialog renameDialog = new RenameDialog();
            var renameResult = await renameDialog.ShowAsync();
            if (renameResult != ContentDialogResult.Primary)
            {
                return;
            }

            // Create file based on dialog result
            string userInput = renameDialog.storedRenameInput;
            var folderRes = await AssociatedInstance.FilesystemViewModel.GetFolderFromPathAsync(currentPath);
            FilesystemResult created = folderRes;
            if (folderRes)
            {
                switch (itemType)
                {
                    case AddItemType.Folder:
                        userInput = !string.IsNullOrWhiteSpace(userInput) ? userInput : "NewFolder".GetLocalized();
                        created = await FilesystemTasks.Wrap(async () =>
                        {
                            return await FilesystemHelpers.CreateAsync(
                                new PathWithType(Path.Combine(folderRes.Result.Path, userInput), FilesystemItemType.Directory),
                                true);
                        });
                        break;

                    case AddItemType.TextDocument:
                        userInput = !string.IsNullOrWhiteSpace(userInput) ? userInput : "NewTextDocument".GetLocalized();
                        created = await FilesystemTasks.Wrap(async () =>
                        {
                            return await FilesystemHelpers.CreateAsync(
                                new PathWithType(Path.Combine(folderRes.Result.Path, userInput + ".txt"), FilesystemItemType.File),
                                true);
                        });
                        break;

                    case AddItemType.BitmapImage:
                        userInput = !string.IsNullOrWhiteSpace(userInput) ? userInput : "NewBitmapImage".GetLocalized();
                        created = await FilesystemTasks.Wrap(async () =>
                        {
                            return await FilesystemHelpers.CreateAsync(
                                new PathWithType(Path.Combine(folderRes.Result.Path, userInput + ".bmp"), FilesystemItemType.File),
                                true);
                        });
                        break;
                }
            }
            if (created == FilesystemErrorCode.ERROR_UNAUTHORIZED)
            {
                await DialogDisplayHelper.ShowDialogAsync("AccessDeniedCreateDialog/Title".GetLocalized(), "AccessDeniedCreateDialog/Text".GetLocalized());
            }
        }

        public RelayCommand CreateNewFolder => new RelayCommand(() => NewFolder());
        public RelayCommand CreateNewTextDocument => new RelayCommand(() => NewTextDocument());
        public RelayCommand CreateNewBitmapImage => new RelayCommand(() => NewBitmapImage());

        private void NewFolder()
        {
            CreateFileFromDialogResultType(AddItemType.Folder);
        }

        private void NewTextDocument()
        {
            CreateFileFromDialogResultType(AddItemType.TextDocument);
        }

        private void NewBitmapImage()
        {
            CreateFileFromDialogResultType(AddItemType.BitmapImage);
        }

        public RelayCommand SelectAllContentPageItems => new RelayCommand(() => SelectAllItems());

        public void SelectAllItems() => AssociatedInstance.ContentPage.SelectAllItems();

        public RelayCommand InvertContentPageSelction => new RelayCommand(() => InvertAllItems());

        public void InvertAllItems() => AssociatedInstance.ContentPage.InvertSelection();

        public RelayCommand ClearContentPageSelection => new RelayCommand(() => ClearAllItems());

        public void ClearAllItems() => AssociatedInstance.ContentPage.ClearSelection();

        public async void ToggleQuickLook()
        {
            try
            {
                if (AssociatedInstance.ContentPage.IsItemSelected && !AssociatedInstance.ContentPage.IsRenamingItem)
                {
                    var clickedOnItem = AssociatedInstance.ContentPage.SelectedItem;

                    Logger.Info("Toggle QuickLook");
                    Debug.WriteLine("Toggle QuickLook");
                    if (Connection != null)
                    {
                        var value = new ValueSet();
                        value.Add("path", clickedOnItem.ItemPath);
                        value.Add("Arguments", "ToggleQuickLook");
                        await Connection.SendMessageAsync(value);
                    }
                }
            }
            catch (FileNotFoundException)
            {
                await DialogDisplayHelper.ShowDialogAsync("FileNotFoundDialog/Title".GetLocalized(), "FileNotFoundPreviewDialog/Text".GetLocalized());
                AssociatedInstance.NavigationToolbar.CanRefresh = false;
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    var ContentOwnedViewModelInstance = AssociatedInstance.FilesystemViewModel;
                    ContentOwnedViewModelInstance.RefreshItems(null);
                });
            }
        }

        public void PushJumpChar(char letter)
        {
            AssociatedInstance.FilesystemViewModel.JumpString += letter.ToString().ToLower();
        }

        public async Task<string> GetHashForFileAsync(ListedItem fileItem, string nameOfAlg, CancellationToken token, Microsoft.UI.Xaml.Controls.ProgressBar progress)
        {
            HashAlgorithmProvider algorithmProvider = HashAlgorithmProvider.OpenAlgorithm(nameOfAlg);
            StorageFile itemFromPath = await AssociatedInstance.FilesystemViewModel.GetFileFromPathAsync((fileItem as ShortcutItem)?.TargetPath ?? fileItem.ItemPath);
            if (itemFromPath == null)
            {
                return "";
            }

            Stream stream = await FilesystemTasks.Wrap(() => itemFromPath.OpenStreamForReadAsync());
            if (stream == null)
            {
                return "";
            }

            var inputStream = stream.AsInputStream();
            var str = inputStream.AsStreamForRead();
            var cap = (long)(0.5 * str.Length) / 100;
            uint capacity;
            if (cap >= uint.MaxValue)
            {
                capacity = uint.MaxValue;
            }
            else
            {
                capacity = Convert.ToUInt32(cap);
            }

            Windows.Storage.Streams.Buffer buffer = new Windows.Storage.Streams.Buffer(capacity);
            var hash = algorithmProvider.CreateHash();
            while (!token.IsCancellationRequested)
            {
                await inputStream.ReadAsync(buffer, capacity, InputStreamOptions.None);
                if (buffer.Length > 0)
                {
                    hash.Append(buffer);
                }
                else
                {
                    break;
                }
                if (progress != null)
                {
                    progress.Value = (double)str.Position / str.Length * 100;
                }
            }
            inputStream.Dispose();
            stream.Dispose();
            if (token.IsCancellationRequested)
            {
                return "";
            }
            return CryptographicBuffer.EncodeToHexString(hash.GetValueAndReset()).ToLower();
        }

        public static async Task EjectDeviceAsync(string path)
        {
            var removableDevice = new RemovableDevice(path);
            bool result = await removableDevice.EjectAsync();
            if (result)
            {
                Debug.WriteLine("Device successfully ejected");

                var toastContent = new ToastContent()
                {
                    Visual = new ToastVisual()
                    {
                        BindingGeneric = new ToastBindingGeneric()
                        {
                            Children =
                            {
                                new AdaptiveText()
                                {
                                    Text = "EjectNotificationHeader".GetLocalized()
                                },
                                new AdaptiveText()
                                {
                                    Text = "EjectNotificationBody".GetLocalized()
                                }
                            },
                            Attribution = new ToastGenericAttributionText()
                            {
                                Text = "SettingsAboutAppName".GetLocalized()
                            }
                        }
                    },
                    ActivationType = ToastActivationType.Protocol
                };

                // Create the toast notification
                var toastNotif = new ToastNotification(toastContent.GetXml());

                // And send the notification
                ToastNotificationManager.CreateToastNotifier().Show(toastNotif);
            }
            else
            {
                Debug.WriteLine("Can't eject device");

                await DialogDisplayHelper.ShowDialogAsync(
                    "EjectNotificationErrorDialogHeader".GetLocalized(),
                    "EjectNotificationErrorDialogBody".GetLocalized());
            }
        }
    }
}