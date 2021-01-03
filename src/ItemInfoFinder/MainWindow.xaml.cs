using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace ItemInfoFinder
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static readonly DependencyProperty OutputProperty =
            DependencyProperty.Register("OutputText", typeof(string), typeof(MainWindow), new PropertyMetadata(String.Empty));

        private readonly BackgroundWorker _loadDataWorker;

        public MainWindow()
        {
            _loadDataWorker = new BackgroundWorker();
            _loadDataWorker.WorkerSupportsCancellation = true;
            _loadDataWorker.WorkerReportsProgress = false;
            _loadDataWorker.DoWork += LoadDataWorker_DoWork;
            _loadDataWorker.RunWorkerCompleted += LoadDataWorker_RunWorkerCompleted;

            InitializeComponent();

            DataContext = this;
        }

        private void LoadDataWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            OutputText = (string)e.Result;
        }

        private void LoadDataWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            const string steamLibraryDirectory = @"D:\SteamLibrary\steamapps";
            var infoFinder = new InfoFinder();
            var infoFileFinder = new InfoFileFinder(steamLibraryDirectory);
            var sb = new StringBuilder();
            try
            {
                var modIds = new List<long>();
                foreach (var dt in infoFileFinder.EnumerateDataFiles())
                {
                    var addModId = infoFinder.ProcessFile(dt.Open(), dt.ModId)
                        && dt.ModId > 0L
                        && modIds.Count is int c
                        && (c == 0 || modIds[c - 1] != dt.ModId);

                    if (addModId)
                        modIds.Add(dt.ModId);
                }

                var mods = WorkshopItemInfo.GetWorkshopItemInfo(modIds);
                infoFinder.GetOutputText(sb, mods);
            }
            catch (Exception ex)
            {
                sb.AppendLine(ex.Message);
                sb.AppendLine();
                sb.AppendLine(ex.StackTrace);
                if (ex.InnerException != null)
                {
                    sb.AppendLine();
                    sb.AppendLine(ex.InnerException.Message);
                    sb.AppendLine();
                    sb.AppendLine(ex.InnerException.StackTrace);
                }
            }
            e.Result = sb.ToString();
        }

        public string OutputText
        {
            get { return (string)GetValue(OutputProperty); }
            set { SetValue(OutputProperty, value); }
        }


        private void Window_Initialized(object sender, EventArgs e)
        {
            _loadDataWorker.RunWorkerAsync();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _loadDataWorker.Dispose();
        }
    }
}
