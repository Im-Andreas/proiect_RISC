using System;
using System.Drawing;
using System.Windows.Forms;

namespace proiect_RISC.Forms
{
    public class CacheForm : Form
    {
        public CacheForm()
        {
            this.Text = "Cache Memory Hierarchy";
            this.Size = new Size(1200, 750);
            
            var tc = new TabControl { Dock = DockStyle.Fill };
            tc.TabPages.Add(new TabPage("Cache Overview (Black-Box)"));
            tc.TabPages.Add(new TabPage("Cache Configuration & Internals"));
            tc.TabPages.Add(new TabPage("Write Policies & LRU Algorithms"));
            this.Controls.Add(tc);
        }
    }
}