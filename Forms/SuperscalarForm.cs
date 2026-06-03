using System;
using System.Drawing;
using System.Windows.Forms;

namespace proiect_RISC.Forms
{
    public class SuperscalarForm : Form
    {
        public SuperscalarForm()
        {
            this.Text = "Superscalar Execution & Hazard Control";
            this.Size = new Size(1100, 700);

            var tc = new TabControl { Dock = DockStyle.Fill };
            
            // Tab 1
            var tabScoreboard = new TabPage("Scoreboard");
            var dgv1 = new DataGridView { AllowUserToAddRows = false, ReadOnly = true, Dock = DockStyle.Top, Height = 150 };
            dgv1.Columns.Add("Instruction", "Instruction"); dgv1.Columns[0].Width = 160;
            dgv1.Columns.Add("IFDone", "IF Done"); dgv1.Columns.Add("OFDone", "OF Done");
            dgv1.Columns.Add("EXDone", "EX Done"); dgv1.Columns.Add("WBDone", "WB Done");
            for(int i = 0; i < 7; i++) dgv1.Rows.Add();
            
            var dgv2 = new DataGridView { AllowUserToAddRows = false, ReadOnly = true, Dock = DockStyle.Top, Height = 150 };
            dgv2.Columns.Add("Unit", "Unit #"); dgv2.Columns.Add("Name", "Name");
            dgv2.Columns.Add("Busy", "Busy"); dgv2.Columns.Add("Rd", "Rd (dest)");
            dgv2.Columns.Add("Rs1", "Rs1"); dgv2.Columns.Add("Rs1Ready", "Rs1 Ready");
            dgv2.Columns.Add("Rs2", "Rs2"); dgv2.Columns.Add("Rs2Ready", "Rs2 Ready");
            dgv2.Rows.Add("1", "Load/Store"); dgv2.Rows.Add("2", "Inmultitor");
            dgv2.Rows.Add("3", "Sumator1"); dgv2.Rows.Add("4", "Sumator2");

            var dgv3 = new DataGridView { AllowUserToAddRows = false, ReadOnly = true, Dock = DockStyle.Top, Height = 80 };
            for(int i = 0; i < 16; i++) { dgv3.Columns.Add($"R{i}", $"R{i}"); dgv3.Columns[i].Width = 55; }
            dgv3.Rows.Add(); dgv3.Rows[0].HeaderCell.Value = "Unit #";
            
            var pnlDb = new Panel { Dock = DockStyle.Top, Height = 50 };
            pnlDb.Controls.Add(new Button { Text = "Step Scoreboard", Left = 10, Top = 10, BackColor = Color.FromArgb(0x19, 0x76, 0xD2), ForeColor = Color.White });
            pnlDb.Controls.Add(new Label { Text = "Cycle: 0", Name = "lblScoreboardClock", Left = 140, Top = 15, AutoSize = true });

            tabScoreboard.Controls.Add(pnlDb); tabScoreboard.Controls.Add(dgv3); tabScoreboard.Controls.Add(dgv2); tabScoreboard.Controls.Add(dgv1);
            tc.TabPages.Add(tabScoreboard);

            // Tab 2 & 3 Placeholder
            tc.TabPages.Add(new TabPage("Tomasulo / Reservation Stations"));
            tc.TabPages.Add(new TabPage("Prefetch Buffer / Out-of-Order"));
            
            this.Controls.Add(tc);
        }
    }
}