using System.Collections.ObjectModel;

namespace OMS.Loans.ViewModels
{
    public class MainViewModel
    {

        public MainViewModel()
        {

        }
        /// <summary>
        /// Not currently used. Here for testing.
        /// </summary>
        private ObservableCollection<object> _Children = new ObservableCollection<object>();


        public ObservableCollection<object> ChildViews
        {
            get { return _Children; }
            set
            {
                _Children = value;

            }
        }





    }
}
