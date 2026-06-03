using System;
using System.Drawing;
using System.Windows.Forms;

namespace proiect_RISC.Controls
{
    public class HazardDetectionPanel : UserControl
    {
        public HazardDetectionPanel()
        {
            this.Size = new Size(800, 600);
            this.BackColor = Color.AliceBlue;
            
            var tableLayout = new TableLayoutPanel
            {
                ColumnCount = 4,
                RowCount = 4,
                Dock = DockStyle.Top,
                Height = 300,
                Padding = new Padding(10)
            };
            
            for (int i = 0; i < 16; i++) {
                var pnl = new Panel { BorderStyle = BorderStyle.FixedSingle, Width = 60, Height = 50, Margin = new Padding(5) };
                pnl.Controls.Add(new Label { Text = $"R{i}", Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleCenter, Font = new Font(this.Font, FontStyle.Bold) });
                var dot = new Label { Name = $"pnlValidBit_R{i}", Text = "?", ForeColor = Color.FromArgb(0x4C, 0xAF, 0x50), Font = new Font(this.Font.FontFamily, 14), Top = 15, Left = 20, AutoSize = true };
                pnl.Controls.Add(dot);
                pnl.Controls.Add(new Label { Name = $"lblValidState_R{i}", Text = "Valid", Top = 35, Left = 15, AutoSize = true, Font = new Font(this.Font.FontFamily, 7) });
                tableLayout.Controls.Add(pnl);
            }
            this.Controls.Add(tableLayout);

            var dgvHazardTimeline = new DataGridView { Name = "dgvHazardTimeline", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, BackColor = Color.White };
            dgvHazardTimeline.Columns.Add("Instruction", "Instruction");
            dgvHazardTimeline.Columns.Add("Reads", "Reads");
            dgvHazardTimeline.Columns.Add("Writes", "Writes");
            dgvHazardTimeline.Columns.Add("RAW", "RAW?");
            dgvHazardTimeline.Columns.Add("WAR", "WAR?");
            dgvHazardTimeline.Columns.Add("WAW", "WAW?");
            dgvHazardTimeline.Columns.Add("Action", "Action");
            dgvHazardTimeline.Columns.Add("Stalls", "Stalls");
            this.Controls.Add(dgvHazardTimeline);

            var pnlForwardingPaths = new Panel { Name = "pnlForwardingPaths", Dock = DockStyle.Bottom, Height = 100, BackColor = Color.White };
            pnlForwardingPaths.Paint += (s, e) => { /* Stub */ };
            pnlForwardingPaths.Controls.Add(new Label { Name = "lblFwdEX_EX", Text = "EX?EX forward: inactive", Top = 80, Left = 10, AutoSize = true });
            pnlForwardingPaths.Controls.Add(new Label { Name = "lblFwdMEM_EX", Text = "MEM?EX forward: inactive", Top = 80, Left = 200, AutoSize = true });
            this.Controls.Add(pnlForwardingPaths);
        }
    }
}