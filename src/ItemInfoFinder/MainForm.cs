using System;
using System.Configuration;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ItemInfoFinder
{
    public partial class MainForm : Form
    {
        private const string LibraryPath = @"D:\SteamLibrary\steamapps\";

        public MainForm()
        {
            InitializeComponent();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            var infoFinder = new InfoFinder();
            try
            {
                var steamLibraryDirectory = ConfigurationManager.AppSettings.Get("SteamLibraryDirectory");
                var seDataDir = Path.Combine(steamLibraryDirectory, Consts.SeDataDir);
                infoFinder.FindItemsInFiles(seDataDir, Consts.DataFilePattern);
                var seModsDir = Path.Combine(steamLibraryDirectory, Consts.SeModsDir);
                infoFinder.FindItemsInZipFiles(seModsDir, Consts.ModFilePattern, SearchOption.AllDirectories, Consts.ModFileInnerPath, Consts.ModFileInnerExtension);
                infoFinder.DownloadModData();
                OutputText.Text = infoFinder.GetOutputText();
                OutputText.SelectAll();
            }
            catch (Exception ex)
            {
                var sb = new StringBuilder();
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
                OutputText.Text = sb.ToString();
            }
        }
    }
}
