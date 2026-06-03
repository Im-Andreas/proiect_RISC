using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace proiect_RISC
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            InitializeMainUI();
        }

        private void InitializeMainUI()
        {
            this.Text = "RISC Pipeline Simulator";
            this.MinimumSize = new Size(1400, 800);

            var splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = (int)(this.Width * 0.4)
            };

            // Left Panel Controls
            var grpMemory = new GroupBox { Text = "Program Memory", Dock = DockStyle.Top, Height = 300, Padding = new Padding(6) };
            var dgvMemory = new DataGridView { Name = "dgvMemory", Dock = DockStyle.Fill, AllowUserToAddRows = false };
            dgvMemory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Address", Width = 80, ReadOnly = true });
            dgvMemory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Instruction", Width = 200 });
            dgvMemory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Comment", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            
            var pnlMemButtons = new Panel { Dock = DockStyle.Bottom, Height = 40 };
            pnlMemButtons.Controls.Add(new Button { Text = "Add Row", Left = 5, Top = 5 });
            pnlMemButtons.Controls.Add(new Button { Text = "Remove Row", Left = 85, Top = 5 });
            pnlMemButtons.Controls.Add(new Button { Text = "Clear All", Left = 165, Top = 5 });
            pnlMemButtons.Controls.Add(new Label { Text = "Format: OPCODE Rd, Rs1, Rs2 | Rs1, Imm", ForeColor = Color.Gray, Left = 250, Top = 10, AutoSize = true });
            grpMemory.Controls.Add(dgvMemory);
            grpMemory.Controls.Add(pnlMemButtons);

            var grpRegisters = new GroupBox { Text = "Registers", Dock = DockStyle.Top, Height = 250, Padding = new Padding(6) };
            var dgvRegisters = new DataGridView { Name = "dgvRegisters", Dock = DockStyle.Fill, AllowUserToAddRows = false };
            dgvRegisters.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Register", Width = 60, ReadOnly = true });
            dgvRegisters.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value (hex)", Width = 100, ReadOnly = true });
            dgvRegisters.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value (dec)", Width = 100, ReadOnly = true });
            dgvRegisters.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Valid", Width = 50, ReadOnly = true });
            for(int i = 0; i < 16; i++) dgvRegisters.Rows.Add($"R{i}", "0x00000000", "0", true);
            grpRegisters.Controls.Add(dgvRegisters);

            var grpControl = new GroupBox { Text = "Control", Dock = DockStyle.Fill, Padding = new Padding(6) };
            grpControl.Controls.Add(new Label { Text = "PC:", Left = 10, Top = 30, AutoSize = true });
            grpControl.Controls.Add(new TextBox { Name = "txtPC", Text = "0x0000", Left = 50, Top = 27, Width = 80 });
            grpControl.Controls.Add(new Button { Text = "▶ Next Clock", Left = 10, Top = 60, Width = 150, Height = 35, BackColor = Color.LightGreen, Font = new Font(this.Font, FontStyle.Bold) });
            grpControl.Controls.Add(new Button { Text = "⏭ Run to End", Left = 10, Top = 105, Width = 150, Height = 28, BackColor = Color.LightBlue });
            grpControl.Controls.Add(new Button { Text = "⏹ Reset", Left = 10, Top = 140, Width = 150, Height = 28, BackColor = Color.LightPink });
            grpControl.Controls.Add(new CheckBox { Text = "Enable Forwarding", Left = 180, Top = 60, Checked = true, AutoSize = true });
            grpControl.Controls.Add(new CheckBox { Text = "Hazard Detection (Valid Bits)", Left = 180, Top = 90, Checked = true, AutoSize = true });
            grpControl.Controls.Add(new Label { Text = "Clock Cycle:", Left = 180, Top = 120, AutoSize = true });
            grpControl.Controls.Add(new Label { Name = "lblClockCycle", Text = "0", Left = 260, Top = 120, AutoSize = true, Font = new Font(this.Font, FontStyle.Bold) });
            grpControl.Controls.Add(new Label { Text = "Stalls Inserted:", Left = 180, Top = 145, AutoSize = true });
            grpControl.Controls.Add(new Label { Name = "lblStalls", Text = "0", Left = 270, Top = 145, AutoSize = true });

            splitMain.Panel1.Controls.Add(grpControl);
            splitMain.Panel1.Controls.Add(grpRegisters);
            splitMain.Panel1.Controls.Add(grpMemory);

            // Right Panel Controls
            var grpPipeline = new GroupBox { Text = "Pipeline Stages", Dock = DockStyle.Top, Height = 300, Padding = new Padding(6) };
            var pnlPipeline = new Panel { Name = "pnlPipeline", Dock = DockStyle.Fill };
            string[] stages = { "IF", "DEC/OF", "EX", "MEM", "WB" };
            for (int i = 0; i < 5; i++)
            {
                var stagePanel = new Panel { BorderStyle = BorderStyle.FixedSingle, Width = 100, Height = 120, Left = 10 + i * 140, Top = 50, BackColor = Color.WhiteSmoke };
                stagePanel.Controls.Add(new Label { Text = stages[i], Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleCenter, Font = new Font(this.Font, FontStyle.Bold) });
                stagePanel.Controls.Add(new Label { Name = $"lbl{stages[i].Split('/')[0]}_Content", Text = "---", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
                pnlPipeline.Controls.Add(stagePanel);
                if (i < 4) pnlPipeline.Controls.Add(new Label { Text = "→", Left = 115 + i * 140, Top = 100, AutoSize = true, Font = new Font(this.Font.FontFamily, 14, FontStyle.Bold) });
            }
            grpPipeline.Controls.Add(pnlPipeline);

            var grpSpaceTime = new GroupBox { Text = "Pipeline Diagram (Space-Time)", Dock = DockStyle.Top, Height = 200, Padding = new Padding(6) };
            var dgvSpaceTime = new DataGridView { Name = "dgvSpaceTime", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false };
            dgvSpaceTime.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Instruction", Width = 180, Frozen = true });
            for(int i = 1; i <= 20; i++) dgvSpaceTime.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = $"T{i}", Width = 40 });
            grpSpaceTime.Controls.Add(dgvSpaceTime);

            var grpHazardLog = new GroupBox { Text = "Hazard Log", Dock = DockStyle.Fill, Padding = new Padding(6) };
            var rtbHazardLog = new RichTextBox { Name = "rtbHazardLog", Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9), Text = "[Hazard Detection Log]\n" };
            var pnlHazardBottom = new Panel { Dock = DockStyle.Bottom, Height = 30 };
            pnlHazardBottom.Controls.Add(new CheckBox { Text = "Auto-scroll", Checked = true, Left = 5, Top = 5, AutoSize = true });
            grpHazardLog.Controls.Add(rtbHazardLog);
            grpHazardLog.Controls.Add(pnlHazardBottom);

            splitMain.Panel2.Controls.Add(grpHazardLog);
            splitMain.Panel2.Controls.Add(grpSpaceTime);
            splitMain.Panel2.Controls.Add(grpPipeline);

            var menu = new MenuStrip();
            var fileItem = new ToolStripMenuItem("File");
            fileItem.DropDownItems.AddRange(new ToolStripItem[] { new ToolStripMenuItem("New Session"), new ToolStripSeparator(), new ToolStripMenuItem("Load Program..."), new ToolStripMenuItem("Save Program..."), new ToolStripSeparator(), new ToolStripMenuItem("Exit") });
            var simItem = new ToolStripMenuItem("Simulation");
            simItem.DropDownItems.AddRange(new ToolStripItem[] { new ToolStripMenuItem("Settings...") });
            menu.Items.AddRange(new ToolStripItem[] { fileItem, simItem });
            this.MainMenuStrip = menu;

            this.Controls.Add(splitMain);
            this.Controls.Add(menu);
        }
    }
}
