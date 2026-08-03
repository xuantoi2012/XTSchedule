using XTSchedule.UI.ViewModels;

namespace XTSchedule.UI.Views;

public partial class MainWindow
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
