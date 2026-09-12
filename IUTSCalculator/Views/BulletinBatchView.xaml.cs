using System.Windows.Controls;
using IUTSCalculator.Services;
using IUTSCalculator.ViewModels;

namespace IUTSCalculator.Views
{
    /// <summary>
    /// Logique d'interaction pour BulletinBatchView.xaml (UserControl)
    /// </summary>
    public partial class BulletinBatchView : UserControl
    {
        public BulletinBatchView()
        {
            InitializeComponent();
        }

        // Si vous souhaitez injecter le ViewModel manuellement :
        public BulletinBatchView(DataService dataService, BulletinService bulletinService, BulletinExportService bulletinExportService)
            : this()
        {
            DataContext = new BulletinBatchViewModel(dataService, bulletinService, bulletinExportService);
        }
    }
}
