
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace DS4Windows.Forms
{
    public partial class SaveWhere : Form
    {
        private bool multisaves;
        public SaveWhere(bool multisavespots)
        {
            InitializeComponent();
            Icon = Properties.Resources.DS4W;
            multisaves = multisavespots;
            lbMultiSaves.Visible = multisaves;
            cBDeleteOther.Visible = multisaves;
            if (multisaves)
                lbPickWhere.Text += Properties.Resources.OtherFileLocation;
            if (API.ExePathNeedsAdmin)
                bnPrgmFolder.Enabled = false;
        }

        private void bnPrgmFolder_Click(object sender, EventArgs e)
        {
            API.AppDataPath = API.ExePath;
            if (multisaves && !cBDeleteOther.Checked)
            {
                try { Directory.Delete(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\DS4Windows", true); }
                catch { }
            }
            else if (!multisaves)
                Save(API.ProfileExePath);
            Close();
        }

        private void bnAppdataFolder_Click(object sender, EventArgs e)
        {

            if (multisaves && !cBDeleteOther.Checked)
                try
                {
                    Directory.Delete($"{API.ExePath}\\Profiles", true);
                    File.Delete(API.ProfileExePath);
                    File.Delete(API.AutoProfileExePath);
                }
                catch (UnauthorizedAccessException) { MessageBox.Show("Cannot Delete old settings, please manaully delete", "DS4Windows"); }
            else if (!multisaves)
                Save(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\DS4Windows\\Profiles.xml");
            API.AppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\DS4Windows";
            Close();
        }

        public bool Save(String path)
        {
            Boolean Saved = true;
            XmlDocument m_Xdoc = new XmlDocument();
            try
            {
                XmlNode Node;

                m_Xdoc.RemoveAll();

                Node = m_Xdoc.CreateXmlDeclaration("1.0", "utf-8", String.Empty);
                m_Xdoc.AppendChild(Node);

                Node = m_Xdoc.CreateComment(String.Format(" Profile Configuration Data. {0} ", DateTime.Now));
                m_Xdoc.AppendChild(Node);

                Node = m_Xdoc.CreateWhitespace("\r\n");
                m_Xdoc.AppendChild(Node);

                Node = m_Xdoc.CreateNode(XmlNodeType.Element, "Profile", null);

                m_Xdoc.AppendChild(Node);

                m_Xdoc.Save(path);
            }
            catch { Saved = false; }

            return Saved;
        }

        private void SaveWhere_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (String.IsNullOrEmpty(API.AppDataPath))
                if (MessageBox.Show(Properties.Resources.ALocactionNeeded, Properties.Resources.CloseDS4W,
             MessageBoxButtons.YesNo) == DialogResult.No)
                    e.Cancel = true;
        }
    }
}
