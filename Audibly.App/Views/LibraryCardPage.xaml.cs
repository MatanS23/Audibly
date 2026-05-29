// Author: rstewa · https://github.com/rstewa
// Updated: 05/26/2026

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI;
using Audibly.App.Helpers;
using Audibly.App.Services;
using Audibly.App.ViewModels;
using Audibly.App.Views.ContentDialogs;
using CommunityToolkit.WinUI;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Sentry;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace Audibly.App.Views;

/// <summary>
///     An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class LibraryCardPage : Page
{
    #region AudioBookFilter enum

    public enum AudioBookFilter
    {
        InProgress,
        NotStarted,
        Completed
    }

    #endregion

    public const string ImportAudiobookText = "Import an audiobook (.m4b, mp3)";

    public const string ImportAudiobooksFromDirectoryText =
        "Import all audiobooks in a directory (recursively). Single-file audiobooks only (.m4b, mp3)";

    public const string ImportAudiobookWithMultipleFilesText =
        "Import an audiobook made up of multiple files (.m4b, mp3)";

    public const string ImportFromJsonFileText = "Import audiobooks from an Audibly export file (.audibly)";

    private readonly HashSet<AudioBookFilter> _activeFilters = new();
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private bool _restoringFilters;

    public LibraryCardPage()
    {
        InitializeComponent();

        Loaded += LibraryCardPage_Loaded;
        ViewModel.ResetFilters += ViewModelOnResetFilters;
        ViewModel.AuthorFilterChanged += ViewModelOnAuthorFilterChanged;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    /// <summary>
    ///     Gets the app-wide ViewModel instance.
    /// </summary>
    public MainViewModel ViewModel => App.ViewModel;

    /// <summary>
    ///     Gets the app-wide PlayerViewModel instance.
    /// </summary>
    public PlayerViewModel PlayerViewModel => App.PlayerViewModel;

    private void ViewModelOnResetFilters()
    {
        SelectAllFiltersCheckBox.IsChecked = false;
    }

    private async void ViewModelOnAuthorFilterChanged()
    {
        if (_activeFilters.Count > 0)
            await FilterAudiobookList();
        else
            await ResetAudiobookListAsync();
    }

    private void ViewToggleButton_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsGridView = !ViewModel.IsGridView;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsSelectMode) || ViewModel.IsSelectMode) return;
        LibraryCardScrollView.SelectionMode = ListViewSelectionMode.None;
        LibraryListView.SelectionMode = ListViewSelectionMode.None;
        LibraryCardScrollView.DeselectAll();
        LibraryListView.DeselectAll();
    }

    private void SelectModeToggle_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsSelectMode = !ViewModel.IsSelectMode;
        var mode = ViewModel.IsSelectMode ? ListViewSelectionMode.Multiple : ListViewSelectionMode.None;
        LibraryCardScrollView.SelectionMode = mode;
        LibraryListView.SelectionMode = mode;
        if (!ViewModel.IsSelectMode)
        {
            LibraryCardScrollView.DeselectAll();
            LibraryListView.DeselectAll();
        }
    }

    private void LibraryCardScrollView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.UpdateSelectedAudiobooks(e.AddedItems, e.RemovedItems);
    }

    private void LibraryListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.UpdateSelectedAudiobooks(e.AddedItems, e.RemovedItems);
    }

    private async void DeleteSelectedButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.DeleteSelectedAudiobooksAsync();
    }

    private async void LibraryCardPage_Loaded(object sender, RoutedEventArgs e)
    {
        // check if data migration already failed
        if (UserSettings.ShowDataMigrationFailedDialog)
        {
            // note: content dialog
            await DialogService.ShowDataMigrationFailedDialogAsync();

            UserSettings.NeedToImportAudiblyExport = false;
            UserSettings.ShowDataMigrationFailedDialog = false;
        }
        else if (UserSettings.NeedToImportAudiblyExport)
        {
            // let the user know that we need to migrate their data into the new database
            // todo: probably do not need this try/catch block but leaving it here for now
            try
            {
                await DialogService.ShowDataMigrationRequiredDialogAsync();
            }
            catch (Exception exception)
            {
                UserSettings.NeedToImportAudiblyExport = false;
                UserSettings.ShowDataMigrationFailedDialog = false;

                // log the error
                ViewModel.LoggingService.LogError(exception, true);

                // notify user that we failed to import their audiobooks
                ViewModel.EnqueueNotification(new Notification
                {
                    Message = "Data Migration Failed",
                    Severity = InfoBarSeverity.Error
                });
            }
        }

        UpdateSortCheckmarks();
        RestoreFiltersFromSettings();
    }

    /// <summary>
    ///     Handles the update of sort checkmarks.
    /// </summary>

    private void UpdateSortCheckmarks()
    {
        SortTitleAscItem.IsChecked = ViewModel.CurrentSort == SortOption.TitleAsc;
        SortTitleDescItem.IsChecked = ViewModel.CurrentSort == SortOption.TitleDesc;
        SortAuthorAscItem.IsChecked = ViewModel.CurrentSort == SortOption.AuthorAsc;
        SortAuthorDescItem.IsChecked = ViewModel.CurrentSort == SortOption.AuthorDesc;
    }

    /// <summary>
    ///     Restores progress filter checkboxes from UserSettings without re-triggering filter logic.
    ///     Called on page load so state persists across navigation and app restarts.
    /// </summary>
    private void RestoreFiltersFromSettings()
    {
        var saved = UserSettings.ActiveFilters;
        if (string.IsNullOrEmpty(saved)) return;

        var names = new HashSet<string>(saved.Split(','));
        _restoringFilters = true;
        _activeFilters.Clear();

        if (names.Contains("InProgress"))
        {
            _activeFilters.Add(AudioBookFilter.InProgress);
            InProgressFilterCheckBox.IsChecked = true;
        }
        if (names.Contains("NotStarted"))
        {
            _activeFilters.Add(AudioBookFilter.NotStarted);
            NotStartedFilterCheckBox.IsChecked = true;
        }
        if (names.Contains("Completed"))
        {
            _activeFilters.Add(AudioBookFilter.Completed);
            CompletedFilterCheckBox.IsChecked = true;
        }

        _restoringFilters = false;
        SetCheckedState();
    }

    private void SaveActiveFilters()
    {
        UserSettings.ActiveFilters = string.Join(",", _activeFilters.Select(f => f.ToString()));
    }

    private void SortTitleAsc_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentSort = SortOption.TitleAsc;
        UpdateSortCheckmarks();
    }

    private void SortTitleDesc_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentSort = SortOption.TitleDesc;
        UpdateSortCheckmarks();
    }

    private void SortAuthorAsc_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentSort = SortOption.AuthorAsc;
        UpdateSortCheckmarks();
    }

    private void SortAuthorDesc_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentSort = SortOption.AuthorDesc;
        UpdateSortCheckmarks();
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.GetAudiobookListAsync();
    }


    /// <summary>
    ///     Resets the audiobook list, respecting any active author filter.
    /// </summary>
    public async Task ResetAudiobookListAsync()
    {
        _activeFilters.Clear();
        UserSettings.ActiveFilters = string.Empty;

        // unchecked all the filter flyout items
        InProgressFilterCheckBox.IsChecked = false;
        NotStartedFilterCheckBox.IsChecked = false;
        CompletedFilterCheckBox.IsChecked = false;

        await _dispatcherQueue.EnqueueAsync(() =>
        {
            ViewModel.Audiobooks.Clear();
            var source = ViewModel.ActiveAuthorFilter != null
                ? ViewModel.AudiobooksForFilter.Where(a => a.Author == ViewModel.ActiveAuthorFilter)
                : (IEnumerable<AudiobookViewModel>)ViewModel.AudiobooksForFilter;
            foreach (var a in source) ViewModel.Audiobooks.Add(a);
        });
    }

    private HashSet<AudiobookViewModel> GetFilteredAudiobooks()
    {
        var source = ViewModel.ActiveAuthorFilter != null
            ? ViewModel.AudiobooksForFilter.Where(a => a.Author == ViewModel.ActiveAuthorFilter)
            : (IEnumerable<AudiobookViewModel>)ViewModel.AudiobooksForFilter;

        var matches = new HashSet<AudiobookViewModel>();
        foreach (var audiobook in source)
        {
            if (_activeFilters.Contains(AudioBookFilter.InProgress) && audiobook.Progress > 0 && !audiobook.IsCompleted)
                matches.Add(audiobook);
            if (_activeFilters.Contains(AudioBookFilter.NotStarted) && audiobook.Progress == 0 && !audiobook.IsCompleted)
                matches.Add(audiobook);
            if (_activeFilters.Contains(AudioBookFilter.Completed) && audiobook.IsCompleted)
                matches.Add(audiobook);
        }

        return matches;
    }

    /// <summary>
    ///     Filters the audiobook list based on the search text.
    /// </summary>
    private async Task FilterAudiobookList()
    {
        if (_activeFilters.Count == 0)
        {
            await ResetAudiobookListAsync();
            return;
        }

        var matches = GetFilteredAudiobooks();

        await _dispatcherQueue.EnqueueAsync(() =>
        {
            ViewModel.Audiobooks.Clear();
            foreach (var match in matches) ViewModel.Audiobooks.Add(match);
        });
    }

    private void SetCheckedState()
    {
        // Controls are null the first time this is called, so we just 
        // need to perform a null check on any one of the controls.
        if (InProgressFilterCheckBox == null) return;

        // mirror AppBarToggleButton checked appearance when any filter is active
        if (InProgressFilterCheckBox.IsChecked == true ||
            NotStartedFilterCheckBox.IsChecked == true ||
            CompletedFilterCheckBox.IsChecked == true)
        {
            FilterButton.Background = new SolidColorBrush((Color)Application.Current.Resources["SystemAccentColor"]);
            FilterButton.Foreground = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"];
        }
        else
        {
            FilterButton.Background = new SolidColorBrush(Colors.Transparent);
            FilterButton.ClearValue(ForegroundProperty);
        }

        if (InProgressFilterCheckBox.IsChecked == true &&
            NotStartedFilterCheckBox.IsChecked == true &&
            CompletedFilterCheckBox.IsChecked == true)
            SelectAllFiltersCheckBox.IsChecked = true;
        else if (InProgressFilterCheckBox.IsChecked == false &&
                 NotStartedFilterCheckBox.IsChecked == false &&
                 CompletedFilterCheckBox.IsChecked == false)
            SelectAllFiltersCheckBox.IsChecked = false;
        else
            // Set third state (indeterminate) by setting IsChecked to null.
            SelectAllFiltersCheckBox.IsChecked = null;
    }

    private async void InProgressFilterCheckBox_OnChecked(object sender, RoutedEventArgs e)
    {
        if (_restoringFilters) return;
        SetCheckedState();
        _activeFilters.Add(AudioBookFilter.InProgress);
        SaveActiveFilters();
        await FilterAudiobookList();
    }

    private async void NotStartedFilterCheckBox_OnChecked(object sender, RoutedEventArgs e)
    {
        if (_restoringFilters) return;
        SetCheckedState();
        _activeFilters.Add(AudioBookFilter.NotStarted);
        SaveActiveFilters();
        await FilterAudiobookList();
    }

    private async void CompletedFilterCheckBox_OnChecked(object sender, RoutedEventArgs e)
    {
        if (_restoringFilters) return;
        SetCheckedState();
        _activeFilters.Add(AudioBookFilter.Completed);
        SaveActiveFilters();
        await FilterAudiobookList();
    }

    private async void InProgressFilterCheckBox_OnUnchecked(object sender, RoutedEventArgs e)
    {
        if (_restoringFilters) return;
        SetCheckedState();
        _activeFilters.Remove(AudioBookFilter.InProgress);
        SaveActiveFilters();
        await FilterAudiobookList();
    }

    private async void NotStartedFilterCheckBox_OnUnchecked(object sender, RoutedEventArgs e)
    {
        if (_restoringFilters) return;
        SetCheckedState();
        _activeFilters.Remove(AudioBookFilter.NotStarted);
        SaveActiveFilters();
        await FilterAudiobookList();
    }

    private async void CompletedFilterCheckBox_OnUnchecked(object sender, RoutedEventArgs e)
    {
        if (_restoringFilters) return;
        SetCheckedState();
        _activeFilters.Remove(AudioBookFilter.Completed);
        SaveActiveFilters();
        await FilterAudiobookList();
    }

    private async void SelectAllFiltersCheckBox_OnChecked(object sender, RoutedEventArgs e)
    {
        InProgressFilterCheckBox.IsChecked =
            NotStartedFilterCheckBox.IsChecked = CompletedFilterCheckBox.IsChecked = true;
    }

    private async void SelectAllFiltersCheckBox_OnUnchecked(object sender, RoutedEventArgs e)
    {
        InProgressFilterCheckBox.IsChecked =
            NotStartedFilterCheckBox.IsChecked = CompletedFilterCheckBox.IsChecked = false;
    }

    private void SelectAllFiltersCheckBox_OnIndeterminate(object sender, RoutedEventArgs e)
    {
        if (InProgressFilterCheckBox.IsChecked == true && NotStartedFilterCheckBox.IsChecked == true &&
            CompletedFilterCheckBox.IsChecked == true)
            SelectAllFiltersCheckBox.IsChecked = false;
    }

    #region debug button

    private async void TestContentDialogButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ChangelogContentDialog
        {
            XamlRoot = App.Window.Content.XamlRoot
        };
        await dialog.ShowAsync();

        // ViewModel.ProgressDialogPrefix = "Importing";
        // ViewModel.ProgressDialogText = "A Clash of Kings";
        //
        // var dialog = new ProgressContentDialog();
        // dialog.XamlRoot = App.Window.Content.XamlRoot;
        // await dialog.ShowAsync();
        // ViewModel.MessageService.ShowDialog(DialogType.Changelog, "What's New?", Changelog.Text);
        // ViewModel.MessageService.ShowDialog(DialogType.FailedDataMigration, string.Empty, string.Empty);
    }

    private void InfoBar_OnClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        // get the notification object
        if (sender.DataContext is not Notification notification) return;
        ViewModel.OnNotificationClosed(notification);
    }

    private void TestNotificationButton_OnClick(object sender, RoutedEventArgs e)
    {
        // randomly select InfoBarSeverity
        var random = new Random();
        var severity = random.Next(0, 4);

        ViewModel.EnqueueNotification(new Notification
        {
            Message = "This is a test notification",
            Severity = severity switch
            {
                0 => InfoBarSeverity.Informational,
                1 => InfoBarSeverity.Success,
                2 => InfoBarSeverity.Warning,
                3 => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational
            }
        });
    }

    public void ThrowExceptionButton_OnClick(object sender, RoutedEventArgs e)
    {
        throw new Exception("This is a test exception");
    }

    public void RestartAppButton_OnClick(object sender, RoutedEventArgs e)
    {
        App.RestartApp();
    }

    public void HideNowPlayingBarButton_OnClick(object sender, RoutedEventArgs e)
    {
        PlayerViewModel.MediaPlayer.Pause();
        if (PlayerViewModel.NowPlaying != null)
            PlayerViewModel.NowPlaying.IsNowPlaying = false;
        PlayerViewModel.NowPlaying = null;
    }

    public void OpenAppStateFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        var filePath = ApplicationData.Current.LocalFolder.Path;
        Process p = new();
        p.StartInfo.FileName = "explorer.exe";
        p.StartInfo.Arguments = $"/open, \"{filePath}\"";
        p.Start();
    }

    private void DebugMenuKeyboardAccelerator_OnInvoked(KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.ShowDebugMenu = !ViewModel.ShowDebugMenu;
    }

    private void OpenCurrentAudiobooksAppStateFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedAudiobook = PlayerViewModel.NowPlaying;
        if (selectedAudiobook == null) return;
        var dir = Path.GetDirectoryName(selectedAudiobook.CoverImagePath);
        if (dir == null) return;
        Process p = new();
        p.StartInfo.FileName = "explorer.exe";
        p.StartInfo.Arguments = $"/open, \"{dir}\"";
        p.Start();
    }

    private void TestSentryLoggingButton_OnClick(object sender, RoutedEventArgs e)
    {
        SentrySdk.CaptureMessage("Something went wrong");
        ViewModel.EnqueueNotification(new Notification
        {
            Message = "Sentry message sent",
            Severity = InfoBarSeverity.Success
        });
    }

    private void ToggleLoadingProgressBar_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsLoading = !ViewModel.IsLoading;
    }

    #endregion
}