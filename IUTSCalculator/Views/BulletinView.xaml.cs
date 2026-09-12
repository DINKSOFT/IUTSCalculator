using System.Windows.Controls;
using IUTSCalculator.Services;
using IUTSCalculator.ViewModels;

namespace IUTSCalculator.Views
{
    /// <summary>
    /// Logique d'interaction pour BulletinView.xaml (UserControl)
    /// </summary>
    public partial class BulletinView : UserControl
    {
        public BulletinView()
        {
            InitializeComponent();
        }

        // Si vous souhaitez injecter le ViewModel manuellement :
        public BulletinView(DataService dataService, BulletinService bulletinService, BulletinExportService bulletinExportService)
            : this()
        {
            DataContext = new BulletinViewModel(dataService, bulletinService, bulletinExportService);
        }
    }
}
