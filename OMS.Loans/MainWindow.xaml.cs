using DevExpress.Xpf.Core;
using System.Collections.ObjectModel;

namespace OMS.Loans
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : ThemedWindow
    {

        public ObservableCollection<object> Panels = new ObservableCollection<object>();

        public MainWindow()
        {
            InitializeComponent();
        }
    }
}
