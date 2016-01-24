using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ItemInfoFinder
{
    public partial class MainForm : Form
    {
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
                infoFinder.FindItemsInFiles(@"D:\SteamLibrary\steamapps\common\SpaceEngineers\Content\Data", "*.sbc");
                infoFinder.FindItemsInZipFiles(@"C:\Users\Sinus\AppData\Roaming\SpaceEngineers\Mods", "*.sbm", @"data\", ".sbc");
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
