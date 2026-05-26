// Author: rstewa · https://github.com/rstewa
// Updated: 05/26/2026

using System.Threading.Tasks;
using Audibly.App.Extensions;
using Audibly.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Audibly.App.Views.ControlPages;

public sealed partial class MoreInfoDialogContent : Page
{
    public AudiobookViewModel AudiobookViewModel { get; set; }
    public string Description { get; set; }

    public MoreInfoDialogContent(AudiobookViewModel audiobookViewModel)
    {
        AudiobookViewModel = audiobookViewModel;
        Description = audiobookViewModel.Description.FormatText();
        InitializeComponent();
    }

    public async Task SaveChangesAsync()
    {
        var newTitle = TitleTextBox.Text.Trim();
        var newAuthor = AuthorTextBox.Text.Trim();

        if (newTitle != AudiobookViewModel.Title)
            AudiobookViewModel.Title = newTitle;
        if (newAuthor != AudiobookViewModel.Author)
            AudiobookViewModel.Author = newAuthor;

        await AudiobookViewModel.SaveAsync();
        await App.ViewModel.GetAudiobookListAsync();
    }
}