using System;
using System.Drawing;
using System.Windows.Forms;

namespace proiect_RISC.Forms
{
    public class VirtualMemoryForm : Form
    {
        public VirtualMemoryForm()
        {
            this.Text = "Virtual Memory & TLB Simulator";
            this.Size = new Size(1150, 720);
            
            var tc = new TabControl { Dock = DockStyle.Fill };
            tc.TabPages.Add(new TabPage("TLB & Address Translation"));
            tc.TabPages.Add(new TabPage("6 Access Cases (Curs 7, p.16)"));
            tc.TabPages.Add(new TabPage("Page Table & Page Fault"));
            this.Controls.Add(tc);
        }
    }
}