using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
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
                infoFinder.FindItemsInFiles(@"C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers\Content\Data", "*.sbc");
                infoFinder.FindItemsInZipFiles(@"C:\Program Files (x86)\Steam\steamapps\workshop\content\244850", "*_legacy.bin", SearchOption.AllDirectories, @"data\", ".sbc");
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
