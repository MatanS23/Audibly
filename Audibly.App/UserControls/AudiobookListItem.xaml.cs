// Author: MatanS23 (fork of rstewa · https://github.com/rstewa)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Audibly.App.Services;
using Audibly.App.ViewModels;
using Audibly.Models;
using CommunityToolkit.WinUI;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ColorHelper = CommunityToolkit.WinUI.Helpers.ColorHelper;

namespace Audibly.App.UserControls;

public sealed partial class AudiobookListItem : UserControl
{
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

    public AudiobookListItem()
    {
        InitializeComponent();
    }

    private static PlayerViewModel PlayerViewModel => App.PlayerViewModel;
    private MainViewModel ViewModel => App.ViewModel;

    private void AudiobookListItem_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        PlayOverlayGrid.Visibility = Visibility.Visible;
        ButtonTile.Background = new SolidColorBrush(ColorHelper.ToColor("#393939"));
    }

    private void AudiobookListItem_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (MenuFlyout.IsOpen) return;
        PlayOverlayGrid.Visibility = Visibility.Collapsed;
        ButtonTile.Background = new SolidColorBrush(Colors.Transparent);
    }

    private void MenuFlyout_Closed(object sender, object e)
    {
        PlayOverlayGrid.Visibility = Visibility.Collapsed;
        ButtonTile.Background = new SolidColorBrush(Colors.Transparent);
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        await _dispatcherQueue.EnqueueAsync(async () =>
        {
            await PlayerViewModel.OpenAudiobook(audiobook);
        });
    }

    private void ShowInFileExplorer_OnClick(object sender, RoutedEventArgs e)
    {
        Process p = new();
        p.StartInfo.FileName = "explorer.exe";
        p.StartInfo.Arguments = $"/select, \"{FilePath}\"";
        p.Start();
    }

    private async void DeleteAudiobook_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        ViewModel.SelectedAudiobook = audiobook;
        await ViewModel.DeleteAudiobookAsync();
    }

    private void ButtonTile_OnRightTapped(object sender, RightTappedRoutedEventArgs? e)
    {
        if (e is null) return;
        var myOption = new FlyoutShowOptions { ShowMode = FlyoutShowMode.Transient };
        MenuFlyout.ShowAt(ButtonTile, myOption);
    }

    private void OpenInAppFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        var dir = Path.GetDirectoryName(audiobook.CoverImagePath);
        if (dir == null) return;
        Process p = new();
        p.StartInfo.FileName = "explorer.exe";
        p.StartInfo.Arguments = $"/open, \"{dir}\"";
        p.Start();
    }

    private async void MoreInfo_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        MenuFlyout.Hide();
        await DialogService.ShowMoreInfoDialogAsync(audiobook);
    }

    private async void MarkAsCompleted_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        audiobook.IsCompleted = true;
        await audiobook.SaveAsync();
    }

    private async void MarkAsIncomplete_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        audiobook.IsCompleted = false;
        await audiobook.SaveAsync();
    }

    private void ExportMetadataToJson_OnClick(object sender, RoutedEventArgs e)
    {
        var audiobook = ViewModel.Audiobooks.FirstOrDefault(a => a.Id == Id);
        if (audiobook == null) return;
        ViewModel.AppDataService.ExportMetadataAsync(audiobook.SourcePaths)
            .ContinueWith(task =>
            {
                if (task.IsFaulted) App.ViewModel.LoggingService.LogError(task.Exception, true);
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    #region dependency properties

    public Guid Id
    {
        get => (Guid)GetValue(IdProperty);
        set => SetValue(IdProperty, value);
    }
    public static readonly DependencyProperty IdProperty =
        DependencyProperty.Register(nameof(Id), typeof(Guid), typeof(AudiobookListItem),
            new PropertyMetadata(Guid.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(AudiobookListItem),
            new PropertyMetadata(null));

    public string Author
    {
        get => (string)GetValue(AuthorProperty);
        set => SetValue(AuthorProperty, value);
    }
    public static readonly DependencyProperty AuthorProperty =
        DependencyProperty.Register(nameof(Author), typeof(string), typeof(AudiobookListItem),
            new PropertyMetadata(null));

    public object Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(object), typeof(AudiobookListItem),
            new PropertyMetadata(null));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }
    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(AudiobookListItem),
            new PropertyMetadata(0.0, ProgressPropertyChangedCallback));

    public string FormatProgress(double progress) => $"{(int)progress}%";
    private static void ProgressPropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // reserved for future progress indicator logic
    }

    public bool IsCompleted
    {
        get => (bool)GetValue(IsCompletedProperty);
        set => SetValue(IsCompletedProperty, value);
    }
    public static readonly DependencyProperty IsCompletedProperty =
        DependencyProperty.Register(nameof(IsCompleted), typeof(bool), typeof(AudiobookListItem),
            new PropertyMetadata(false, IsCompletedPropertyChangedCallback));

    private static void IsCompletedPropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not AudiobookListItem item) return;
        var isCompleted = (bool)e.NewValue;
        item.MarkAsCompletedButton.Visibility = isCompleted ? Visibility.Collapsed : Visibility.Visible;
        item.MarkAsIncompleteButton.Visibility = isCompleted ? Visibility.Visible : Visibility.Collapsed;
    }

    public int SourcePathsCount
    {
        get => (int)GetValue(SourcePathsCountProperty);
        set => SetValue(SourcePathsCountProperty, value);
    }
    public static readonly DependencyProperty SourcePathsCountProperty =
        DependencyProperty.Register(nameof(SourcePathsCount), typeof(int), typeof(AudiobookListItem),
            new PropertyMetadata(0));

    public List<SourceFile> SourcePaths
    {
        get => (List<SourceFile>)GetValue(SourcePathsProperty);
        set => SetValue(SourcePathsProperty, value);
    }
    public static readonly DependencyProperty SourcePathsProperty =
        DependencyProperty.Register(nameof(SourcePaths), typeof(List<SourceFile>), typeof(AudiobookListItem),
            new PropertyMetadata(null));

    public string FilePath
    {
        get => (string)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }
    public static readonly DependencyProperty FilePathProperty =
        DependencyProperty.Register(nameof(FilePath), typeof(string), typeof(AudiobookListItem),
            new PropertyMetadata(null));

    #endregion
}