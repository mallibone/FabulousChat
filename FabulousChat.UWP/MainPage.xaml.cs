using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace FabulousChat.UWP
{
    public sealed partial class MainPage
    {
        public MainPage()
        {
            InitializeComponent();
            LoadApplication(new FabulousChat.App());
        }
    }
}
