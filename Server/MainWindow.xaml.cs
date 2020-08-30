using System.Windows;

namespace Server
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        ChatServer cs;
        public MainWindow()
        {
            InitializeComponent();
            cs = new ChatServer();
            this.DataContext = cs;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            cs.SwitchServerState();
        }
    }
}
