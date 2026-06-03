using System;
using System.Drawing;
using System.Windows.Forms;

namespace proiect_RISC.Forms
{
    public class SettingsForm : Form
    {
        public SettingsForm()
        {
            this.Text = "Simulator Settings";
            this.Size = new Size(500, 450);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            
            var tc = new TabControl { Dock = DockStyle.Fill };
            tc.TabPages.Add(new TabPage("General"));
            tc.TabPages.Add(new TabPage("Extensions"));
            tc.TabPages.Add(new TabPage("About / Info"));
            this.Controls.Add(tc);
            
            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 40 };
            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Top = 5, Left = 300 };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Top = 5, Left = 380 };
            pnlBottom.Controls.Add(btnOk);
            pnlBottom.Controls.Add(btnCancel);
            this.Controls.Add(pnlBottom);
        }
    }
}