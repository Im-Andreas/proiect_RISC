using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using proiect_RISC.Forms;
using proiect_RISC.Models;

namespace proiect_RISC
{
    public partial class Form1 : Form
    {
        // Simulation components
        private readonly PipelineSimulator _simulator = new PipelineSimulator();
        private readonly InstructionParser _parser = new InstructionParser();
        private System.Windows.Forms.Timer _autoRunTimer;
        private bool _isProgramLoaded = false;

        // UI Controls (stored for easy access)
        private DataGridView dgvMemory;
        private DataGridView dgvRegisters;
        private DataGridView dgvSpaceTime;
        private RichTextBox rtbHazardLog;
        private Label lblClockCycle;
        private Label lblStalls;
        private Label lblPC;
        private TextBox txtPC;
        private CheckBox chkForwarding;
        private CheckBox chkHazardDetection;
        private Button btnNextClock;
        private Button btnRunToEnd;
        private Button btnReset;
        private Button btnAddRow;
        private Button btnRemoveRow;
        private Button btnClearAll;

        // Pipeline stage labels
        private Label lblIF_Content;
        private Label lblDEC_Content;
        private Label lblEX_Content;
        private Label lblMEM_Content;
        private Label lblWB_Content;

        // Cache stats labels
        private Label lblICacheStats;
        private Label lblDCacheStats;
        private Label lblCacheMetrics;
        private CacheForm _activeCacheForm;

        public Form1()
        {
            InitializeComponent();
            InitializeMainUI();
            InitializeLogic();
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
            this.dgvMemory = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false };
            this.dgvMemory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Address", Width = 80, ReadOnly = true });
            this.dgvMemory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Instruction", Width = 200 });
            this.dgvMemory.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Comment", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            
            var pnlMemButtons = new Panel { Dock = DockStyle.Bottom, Height = 40 };
            this.btnAddRow = new Button { Text = "Add Row", Left = 5, Top = 5 };
            this.btnRemoveRow = new Button { Text = "Remove Row", Left = 85, Top = 5 };
            this.btnClearAll = new Button { Text = "Clear All", Left = 165, Top = 5 };
            this.btnAddRow.Click += btnAddRow_Click;
            this.btnRemoveRow.Click += btnRemoveRow_Click;
            this.btnClearAll.Click += btnClearAll_Click;
            pnlMemButtons.Controls.Add(this.btnAddRow);
            pnlMemButtons.Controls.Add(this.btnRemoveRow);
            pnlMemButtons.Controls.Add(this.btnClearAll);
            pnlMemButtons.Controls.Add(new Label { Text = "Format: OPCODE Rd, Rs1, Rs2 | Rs1, Imm", ForeColor = Color.Gray, Left = 250, Top = 10, AutoSize = true });
            grpMemory.Controls.Add(this.dgvMemory);
            grpMemory.Controls.Add(pnlMemButtons);

            var grpRegisters = new GroupBox { Text = "Registers", Dock = DockStyle.Top, Height = 250, Padding = new Padding(6) };
            this.dgvRegisters = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false };
            this.dgvRegisters.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Register", Width = 60, ReadOnly = true });
            this.dgvRegisters.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value (hex)", Width = 100, ReadOnly = true });
            this.dgvRegisters.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value (dec)", Width = 100, ReadOnly = true });
            this.dgvRegisters.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Valid", Width = 50, ReadOnly = true });
            for(int i = 0; i < 16; i++) this.dgvRegisters.Rows.Add($"R{i}", "0x00000000", "0", true);
            grpRegisters.Controls.Add(this.dgvRegisters);

            var grpControl = new GroupBox { Text = "Control", Dock = DockStyle.Fill, Padding = new Padding(6) };
            grpControl.Controls.Add(new Label { Text = "PC:", Left = 10, Top = 30, AutoSize = true });
            this.txtPC = new TextBox { Text = "0x0000", Left = 50, Top = 27, Width = 80 };
            this.btnNextClock = new Button { Text = "▶ Next Clock", Left = 10, Top = 60, Width = 150, Height = 35, BackColor = Color.LightGreen, Font = new Font(this.Font, FontStyle.Bold) };
            this.btnRunToEnd = new Button { Text = "⏭ Run to End", Left = 10, Top = 105, Width = 150, Height = 28, BackColor = Color.LightBlue };
            this.btnReset = new Button { Text = "⏹ Reset", Left = 10, Top = 140, Width = 150, Height = 28, BackColor = Color.LightPink };
            this.chkForwarding = new CheckBox { Text = "Enable Forwarding", Left = 180, Top = 60, Checked = true, AutoSize = true };
            this.chkHazardDetection = new CheckBox { Text = "Hazard Detection (Valid Bits)", Left = 180, Top = 90, Checked = true, AutoSize = true };
            this.lblClockCycle = new Label { Text = "0", Left = 260, Top = 120, AutoSize = true, Font = new Font(this.Font, FontStyle.Bold) };
            this.lblStalls = new Label { Text = "0", Left = 270, Top = 145, AutoSize = true };
            
            this.btnNextClock.Click += btnNextClock_Click;
            this.btnRunToEnd.Click += btnRunToEnd_Click;
            this.btnReset.Click += btnReset_Click;
            this.chkForwarding.CheckedChanged += chkForwarding_CheckedChanged;
            this.chkHazardDetection.CheckedChanged += chkHazardDetection_CheckedChanged;
            
            grpControl.Controls.Add(this.txtPC);
            grpControl.Controls.Add(this.btnNextClock);
            grpControl.Controls.Add(this.btnRunToEnd);
            grpControl.Controls.Add(this.btnReset);
            grpControl.Controls.Add(this.chkForwarding);
            grpControl.Controls.Add(this.chkHazardDetection);
            grpControl.Controls.Add(new Label { Text = "Clock Cycle:", Left = 180, Top = 120, AutoSize = true });
            grpControl.Controls.Add(this.lblClockCycle);
            grpControl.Controls.Add(new Label { Text = "Stalls Inserted:", Left = 180, Top = 145, AutoSize = true });
            grpControl.Controls.Add(this.lblStalls);

            splitMain.Panel1.Controls.Add(grpControl);
            splitMain.Panel1.Controls.Add(grpRegisters);
            splitMain.Panel1.Controls.Add(grpMemory);

            // Right Panel Controls
            var grpPipeline = new GroupBox { Text = "Pipeline Stages", Dock = DockStyle.Top, Height = 300, Padding = new Padding(6) };
            var pnlPipeline = new Panel { Dock = DockStyle.Fill };
            string[] stages = { "IF", "DEC/OF", "EX", "MEM", "WB" };
            string[] stageKeys = { "IF", "DEC", "EX", "MEM", "WB" };
            
            for (int i = 0; i < 5; i++)
            {
                var stagePanel = new Panel { BorderStyle = BorderStyle.FixedSingle, Width = 100, Height = 120, Left = 10 + i * 140, Top = 50, BackColor = Color.WhiteSmoke };
                stagePanel.Controls.Add(new Label { Text = stages[i], Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleCenter, Font = new Font(this.Font, FontStyle.Bold) });
                
                Label contentLabel = new Label { Text = "---", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
                stagePanel.Controls.Add(contentLabel);
                
                // Assign to class fields
                if (i == 0) this.lblIF_Content = contentLabel;
                else if (i == 1) this.lblDEC_Content = contentLabel;
                else if (i == 2) this.lblEX_Content = contentLabel;
                else if (i == 3) this.lblMEM_Content = contentLabel;
                else if (i == 4) this.lblWB_Content = contentLabel;
                
                pnlPipeline.Controls.Add(stagePanel);
                if (i < 4) pnlPipeline.Controls.Add(new Label { Text = "→", Left = 115 + i * 140, Top = 100, AutoSize = true, Font = new Font(this.Font.FontFamily, 14, FontStyle.Bold) });
            }
            grpPipeline.Controls.Add(pnlPipeline);

            var grpSpaceTime = new GroupBox { Text = "Pipeline Diagram (Space-Time)", Dock = DockStyle.Top, Height = 200, Padding = new Padding(6) };
            this.dgvSpaceTime = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false };
            this.dgvSpaceTime.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Instruction", Width = 180, Frozen = true });
            for(int i = 1; i <= 200; i++) this.dgvSpaceTime.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = $"T{i}", Width = 40 });
            grpSpaceTime.Controls.Add(this.dgvSpaceTime);

            // Cache stats panel (between pipeline and space-time)
            var grpCacheStats = new GroupBox { Text = "Cache Statistics (Live)", Dock = DockStyle.Top, Height = 90, Padding = new Padding(6) };
            this.lblICacheStats = new Label { Text = "ICache: --", AutoSize = true, Left = 8, Top = 18, Font = new Font("Consolas", 8) };
            this.lblDCacheStats = new Label { Text = "DCache: --", AutoSize = true, Left = 8, Top = 36, Font = new Font("Consolas", 8) };
            this.lblCacheMetrics = new Label { Text = "Cicli: --", AutoSize = true, Left = 8, Top = 54, Font = new Font("Consolas", 8), ForeColor = Color.DarkRed };

            var btnOpenCache = new Button
            {
                Text = "Open Cache View",
                Left = 600, Top = 18, Width = 130, Height = 44,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.FromArgb(0x19, 0x76, 0xD2),
                ForeColor = Color.White
            };
            btnOpenCache.Click += (s, e) =>
            {
                if (_activeCacheForm == null || _activeCacheForm.IsDisposed)
                    _activeCacheForm = new CacheForm(_simulator);
                _activeCacheForm.Show(this);
                _activeCacheForm.BringToFront();
            };
            grpCacheStats.Controls.Add(this.lblICacheStats);
            grpCacheStats.Controls.Add(this.lblDCacheStats);
            grpCacheStats.Controls.Add(this.lblCacheMetrics);
            grpCacheStats.Controls.Add(btnOpenCache);

            var grpHazardLog = new GroupBox { Text = "Hazard Log", Dock = DockStyle.Fill, Padding = new Padding(6) };
            this.rtbHazardLog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9), Text = "[Hazard Detection Log]\n" };
            var pnlHazardBottom = new Panel { Dock = DockStyle.Bottom, Height = 30 };
            pnlHazardBottom.Controls.Add(new CheckBox { Text = "Auto-scroll", Checked = true, Left = 5, Top = 5, AutoSize = true });
            grpHazardLog.Controls.Add(this.rtbHazardLog);
            grpHazardLog.Controls.Add(pnlHazardBottom);

            splitMain.Panel2.Controls.Add(grpHazardLog);
            splitMain.Panel2.Controls.Add(grpSpaceTime);
            splitMain.Panel2.Controls.Add(grpCacheStats);
            splitMain.Panel2.Controls.Add(grpPipeline);

            var menu = new MenuStrip();
            
            var fileItem = new ToolStripMenuItem("File");
            fileItem.DropDownItems.AddRange(new ToolStripItem[] { new ToolStripMenuItem("New Session"), new ToolStripSeparator(), new ToolStripMenuItem("Load Program..."), new ToolStripMenuItem("Save Program..."), new ToolStripSeparator(), new ToolStripMenuItem("Exit") });
            
            var viewItem = new ToolStripMenuItem("View");
            var pipelineViewItem = new ToolStripMenuItem("Pipeline View");
            pipelineViewItem.Click += (s, e) => MessageBox.Show("The Pipeline View is the main active window.", "View", MessageBoxButtons.OK, MessageBoxIcon.Information);
            var cacheViewItem = new ToolStripMenuItem("Cache View");
            cacheViewItem.Click += (s, e) =>
            {
                if (_activeCacheForm == null || _activeCacheForm.IsDisposed)
                    _activeCacheForm = new CacheForm(_simulator);
                _activeCacheForm.Show(this);
                _activeCacheForm.BringToFront();
            };
            var virtualMemoryViewItem = new ToolStripMenuItem("Virtual Memory View");
            virtualMemoryViewItem.Click += (s, e) => new VirtualMemoryForm(_simulator).Show(this);
            viewItem.DropDownItems.AddRange(new ToolStripItem[] { pipelineViewItem, cacheViewItem, virtualMemoryViewItem, new ToolStripSeparator(), new ToolStripMenuItem("Reset Layout") });
            
            var simItem = new ToolStripMenuItem("Simulation");
            var settingsItem = new ToolStripMenuItem("Settings...");
            settingsItem.Click += (s, e) => { using (var dlg = new SettingsForm()) dlg.ShowDialog(this); };
            simItem.DropDownItems.AddRange(new ToolStripItem[] { new ToolStripMenuItem("Next Clock"), new ToolStripMenuItem("Run to End"), new ToolStripMenuItem("Reset"), new ToolStripSeparator(), settingsItem });
            
            var extItem = new ToolStripMenuItem("Extensions");
            var scoreboardItem = new ToolStripMenuItem("Scoreboard Table");
            scoreboardItem.Click += (s, e) => new SuperscalarForm().Show(this);
            var tomasuloItem = new ToolStripMenuItem("Tomasulo / Reservation Stations");
            tomasuloItem.Click += (s, e) => new SuperscalarForm().Show(this);
            var prefetchItem = new ToolStripMenuItem("Prefetch Buffer");
            prefetchItem.Click += (s, e) => {
                var form = new SuperscalarForm();
                // Select the 3rd tab (Prefetch Buffer)
                if (form.Controls[0] is TabControl tc && tc.TabCount >= 3)
                    tc.SelectedIndex = 2;
                form.Show(this);
            };
            extItem.DropDownItems.AddRange(new ToolStripItem[] { scoreboardItem, tomasuloItem, prefetchItem });

            var helpItem = new ToolStripMenuItem("Help");
            helpItem.DropDownItems.Add(new ToolStripMenuItem("About"));

            menu.Items.AddRange(new ToolStripItem[] { fileItem, viewItem, simItem, extItem, helpItem });
            this.MainMenuStrip = menu;

            this.Controls.Add(splitMain);
            this.Controls.Add(menu);

            // Wire up menu items
            foreach (ToolStripMenuItem item in menu.Items)
            {
                if (item.Text == "File")
                {
                    foreach (ToolStripItem si in item.DropDownItems)
                    {
                        if (si.Text == "New Session") si.Click += (s, e) => btnReset_Click(s, e);
                        else if (si.Text == "Load Program...") si.Click += (s, e) => LoadProgramFromFile();
                        else if (si.Text == "Save Program...") si.Click += (s, e) => SaveProgramToFile();
                        else if (si.Text == "Exit") si.Click += (s, e) => this.Close();
                    }
                    
                    // Add Load Demo submenu
                    var loadDemoMenu = new ToolStripMenuItem("Load Demo Program");
                    foreach (var kvp in DemoPrograms.GetProgramMenu())
                    {
                        string key = kvp.Key;
                        var demoItem = new ToolStripMenuItem(kvp.Value);
                        demoItem.Click += (s, e) => LoadDemoProgram(key);
                        loadDemoMenu.DropDownItems.Add(demoItem);
                    }
                    item.DropDownItems.Insert(2, loadDemoMenu);
                }
                else if (item.Text == "Simulation")
                {
                    foreach (ToolStripItem si in item.DropDownItems)
                    {
                        if (si.Text == "Next Clock") si.Click += (s, e) => btnNextClock_Click(s, e);
                        else if (si.Text == "Run to End") si.Click += (s, e) => btnRunToEnd_Click(s, e);
                        else if (si.Text == "Reset") si.Click += (s, e) => btnReset_Click(s, e);
                    }
                }
                else if (item.Text == "Help")
                {
                    foreach (ToolStripItem si in item.DropDownItems)
                    {
                        if (si.Text == "About") si.Click += (s, e) => 
                            MessageBox.Show("RISC Pipeline Simulator\nVersion 1.0\n\nSimulator de pipeline RISC pe 5 stagii cu:\n- Detectie hazarduri RAW prin biti de validare\n- Forwarding hardware (EX→EX, MEM→EX)\n- Diagrama spatiu-timp\n- Extensii: Scoreboard, Tomasulo, Cache, TLB", 
                                "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
        }

        // -------------------------------------------------------
        // INITIALIZATION
        // -------------------------------------------------------
        private void InitializeLogic()
        {
            // Subscribe to simulator events
            _simulator.CycleCompleted += OnCycleCompleted;
            _simulator.Registers.RegisterChanged += OnRegisterChanged;

            // Timer for auto-run
            _autoRunTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _autoRunTimer.Tick += (s, e) =>
            {
                if (!_simulator.IsHalted)
                    ExecuteStep();
                else
                {
                    _autoRunTimer.Stop();
                    btnRunToEnd.Enabled = true;
                    btnNextClock.Enabled = true;
                }
            };

            // Initialize memory grid with default program
            InitializeMemoryGrid();
        }

        private void InitializeMemoryGrid()
        {
            if (dgvMemory == null) return;
            dgvMemory.Rows.Clear();
            for (int i = 0; i < 8; i++)
                dgvMemory.Rows.Add($"0x{(0x0100 + i * 4):X4}", "", "");
        }

        // -------------------------------------------------------
        // EVENT HANDLERS - BUTTONS
        // -------------------------------------------------------
        private void btnNextClock_Click(object sender, EventArgs e)
        {
            if (!_isProgramLoaded) LoadProgramFromGrid();
            if (_simulator.IsHalted)
            {
                MessageBox.Show("Simulatorul a ajuns la HALT. Apasa Reset pentru a reporni.", "HALT", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ExecuteStep();
        }

        private void btnRunToEnd_Click(object sender, EventArgs e)
        {
            if (!_isProgramLoaded) LoadProgramFromGrid();
            if (_simulator.IsHalted)
            {
                MessageBox.Show("Simulatorul a ajuns la HALT. Apasa Reset pentru a reporni.", "HALT", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _autoRunTimer.Start();
            btnRunToEnd.Enabled = false;
            btnNextClock.Enabled = false;
        }

        private void btnReset_Click(object sender, EventArgs e)
        {
            _autoRunTimer.Stop();
            _simulator.Reset();
            _isProgramLoaded = false;

            // Reset UI
            ClearPipelineStages();
            ClearSpaceTimeDiagram();
            if (rtbHazardLog != null)
                rtbHazardLog.Text = "[Hazard Detection Log]\n";
            if (lblClockCycle != null)
                lblClockCycle.Text = "0";
            if (lblStalls != null)
                lblStalls.Text = "0";
            if (txtPC != null)
                txtPC.Text = "0x0000";

            // Reset registers display
            for (int i = 0; i < 16; i++)
            {
                dgvRegisters.Rows[i].Cells[1].Value = "0x00000000";
                dgvRegisters.Rows[i].Cells[2].Value = "0";
                dgvRegisters.Rows[i].Cells[3].Value = true;
            }

            btnRunToEnd.Enabled = true;
            btnNextClock.Enabled = true;

            // Reset cache stats display
            if (lblICacheStats != null) lblICacheStats.Text = "ICache: --";
            if (lblDCacheStats != null) lblDCacheStats.Text = "DCache: --";
            if (lblCacheMetrics != null) lblCacheMetrics.Text = "Cicli: — | Rulați simularea pentru metrici.";
        }

        private void btnAddRow_Click(object sender, EventArgs e)
        {
            if (dgvMemory.Rows.Count == 0)
            {
                dgvMemory.Rows.Add("0x0100", "", "");
            }
            else
            {
                var lastAddr = ParseHexAddress(dgvMemory.Rows[dgvMemory.Rows.Count - 1].Cells[0].Value?.ToString() ?? "0x0100");
                dgvMemory.Rows.Add($"0x{(lastAddr + 4):X4}", "", "");
            }
        }

        private void btnRemoveRow_Click(object sender, EventArgs e)
        {
            if (dgvMemory.SelectedRows.Count > 0)
            {
                foreach (DataGridViewRow row in dgvMemory.SelectedRows)
                {
                    if (!row.IsNewRow)
                        dgvMemory.Rows.Remove(row);
                }
            }
        }

        private void btnClearAll_Click(object sender, EventArgs e)
        {
            dgvMemory.Rows.Clear();
            InitializeMemoryGrid();
        }

        private void chkForwarding_CheckedChanged(object sender, EventArgs e)
        {
            _simulator.ForwardingEnabled = chkForwarding.Checked;
        }

        private void chkHazardDetection_CheckedChanged(object sender, EventArgs e)
        {
            _simulator.HazardDetectionEnabled = chkHazardDetection.Checked;
        }

        // -------------------------------------------------------
        // PROGRAM LOADING
        // -------------------------------------------------------
        private void LoadProgramFromGrid()
        {
            var rows = GetProgramRowsFromGrid().ToList();
            if (rows.Count == 0)
            {
                MessageBox.Show("Programul este gol. Adauga instructiuni in tabelul Program Memory.", "Eroare", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var program = _parser.ParseProgram(rows);

            // Validate all instructions
            var errors = program.Where(i => i.Opcode == Opcode.UNKNOWN).ToList();
            if (errors.Any())
            {
                var errMsg = "Instructiuni invalide detectate:\n" + string.Join("\n", errors.Select(e => $"[0x{e.Address:X4}] {e.RawText}"));
                MessageBox.Show(errMsg, "Eroare de parsare", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            uint startAddr = rows.First().address;
            _simulator.LoadProgram(program, startAddr);
            _isProgramLoaded = true;

            InitializeSpaceTimeDiagram(program);

            if (rtbHazardLog != null)
            {
                int realInstrCount = program.Count(i => i.Opcode != Opcode.NOP && i.Opcode != Opcode.UNKNOWN);
                rtbHazardLog.AppendText($"Program incarcat: {realInstrCount} instructiuni, start la 0x{startAddr:X4}\n");
            }
        }

        private IEnumerable<(uint address, string text)> GetProgramRowsFromGrid()
        {
            foreach (DataGridViewRow row in dgvMemory.Rows)
            {
                if (row.IsNewRow) continue;
                var addrText = row.Cells[0].Value?.ToString() ?? "0";
                var instrText = row.Cells[1].Value?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(instrText)) continue;
                uint addr = ParseHexAddress(addrText);
                yield return (addr, instrText);
            }
        }

        private uint ParseHexAddress(string text)
        {
            text = text.Trim().ToUpperInvariant();
            if (text.StartsWith("0X"))
                return Convert.ToUInt32(text.Substring(2), 16);
            if (uint.TryParse(text, out uint val))
                return val;
            return 0;
        }

        // -------------------------------------------------------
        // DEMO PROGRAM LOADING
        // -------------------------------------------------------
        private void LoadDemoProgram(string key)
        {
            btnReset_Click(null, null);

            var rows = DemoPrograms.GetProgram(key);
            dgvMemory.Rows.Clear();
            foreach (var (addr, instr, comment) in rows)
                dgvMemory.Rows.Add($"0x{addr:X4}", instr, comment);

            _isProgramLoaded = false;

            if (key == "nota5_hazard_raw")
            {
                MessageBox.Show("ATENTIE: Pentru a vedea stall-urile, dezactiveaza 'Enable Forwarding' inainte de a apasa Next Clock!",
                    "Demo Hazard RAW", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // -------------------------------------------------------
        // FILE OPERATIONS
        // -------------------------------------------------------
        private void LoadProgramFromFile()
        {
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Assembly Files (*.asm)|*.asm|Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
                openFileDialog.Title = "Incarca Program RISC";
                openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        btnReset_Click(null, null);

                        var lines = System.IO.File.ReadAllLines(openFileDialog.FileName);
                        dgvMemory.Rows.Clear();

                        uint currentAddress = 0x0100;
                        foreach (var line in lines)
                        {
                            var trimmed = line.Trim();
                            if (string.IsNullOrWhiteSpace(trimmed)) continue;
                            
                            // Liniile care incep cu ; sunt comentarii pure - le afișăm
                            if (trimmed.StartsWith(";"))
                            {
                                dgvMemory.Rows.Add("", "", trimmed.Substring(1).Trim());
                                continue;
                            }

                            string instruction = trimmed;
                            string comment = "";

                            // STEP 1: Extrage comentariul (ÎNTÂI!) pentru a evita parsarea greșită a 0x din comentariu
                            int commentIndex = instruction.IndexOfAny(new[] { ';', '\u003B', '\uFF1B', '\u061B' });
                            if (commentIndex >= 0)
                            {
                                comment = instruction.Substring(commentIndex + 1).Trim();
                                instruction = instruction.Substring(0, commentIndex).Trim();
                            }

                            // STEP 2: Parsează adresa din instrucțiune daca exista
                            // Suporta: "0x0100 OPCODE" sau "0x0100: OPCODE" sau "0x0100\tOPCODE" sau doar "OPCODE"
                            
                            // Verifica pentru format cu ":"
                            if (instruction.Contains(":"))
                            {
                                var colonIndex = instruction.IndexOf(':');
                                var addressPart = instruction.Substring(0, colonIndex).Trim();
                                instruction = instruction.Substring(colonIndex + 1).Trim();

                                if (addressPart.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (uint.TryParse(addressPart.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out uint parsedAddr))
                                        currentAddress = parsedAddr;
                                }
                            }
                            else
                            {
                                // Verifica pentru format cu spații/tab-uri: "0x0100 OPCODE" sau "0x0100\tOPCODE"
                                var tokens = System.Text.RegularExpressions.Regex.Split(instruction, @"\s+");
                                if (tokens.Length > 0 && tokens[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                                {
                                    string addressPart = tokens[0];
                                    if (uint.TryParse(addressPart.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out uint parsedAddr))
                                    {
                                        currentAddress = parsedAddr;
                                        // Reconstituie instrucțiunea fără adresă
                                        instruction = string.Join(" ", tokens.Skip(1)).Trim();
                                    }
                                }
                            }

                            // Verifica daca instructiunea e vida dupa stergerea adresei si comentariului
                            if (string.IsNullOrWhiteSpace(instruction)) continue;

                            dgvMemory.Rows.Add($"0x{currentAddress:X4}", instruction, comment);
                            currentAddress += 4;
                        }

                        _isProgramLoaded = false;
                        
                        // Încarcă imediat programul din grid în simulator
                        LoadProgramFromGrid();
                        
                        MessageBox.Show($"Program incarcat cu succes din:\n{openFileDialog.FileName}\n\n{dgvMemory.Rows.Count} instructiuni gasite.",
                            "Incarcare Reusita", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Eroare la incarcarea fisierului:\n{ex.Message}",
                            "Eroare", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void SaveProgramToFile()
        {
            using (var saveFileDialog = new SaveFileDialog())
            {
                saveFileDialog.Filter = "Assembly Files (*.asm)|*.asm|Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
                saveFileDialog.Title = "Salveaza Program RISC";
                saveFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                saveFileDialog.FileName = "program.asm";

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var lines = new List<string>();
                        foreach (DataGridViewRow row in dgvMemory.Rows)
                        {
                            if (row.IsNewRow) continue;
                            var addr = row.Cells[0].Value?.ToString() ?? "";
                            var instr = row.Cells[1].Value?.ToString() ?? "";
                            var comment = row.Cells[2].Value?.ToString() ?? "";

                            if (string.IsNullOrWhiteSpace(instr)) continue;

                            var line = $"{addr}: {instr}";
                            if (!string.IsNullOrWhiteSpace(comment))
                                line += $" ; {comment}";
                            lines.Add(line);
                        }

                        System.IO.File.WriteAllLines(saveFileDialog.FileName, lines);
                        MessageBox.Show($"Program salvat cu succes in:\n{saveFileDialog.FileName}",
                            "Salvare Reusita", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Eroare la salvarea fisierului:\n{ex.Message}",
                            "Eroare", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        // -------------------------------------------------------
        // SIMULATION EXECUTION
        // -------------------------------------------------------
        private void ExecuteStep()
        {
            var state = _simulator.Step();
            // OnCycleCompleted will be called automatically via event
        }

        private void OnCycleCompleted(PipelineState state)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<PipelineState>(OnCycleCompleted), state);
                return;
            }

            UpdateUI(state);
        }

        private void UpdateUI(PipelineState state)
        {
            // Update clock and stats
            if (lblClockCycle != null)
                lblClockCycle.Text = state.ClockCycle.ToString();
            if (lblStalls != null)
                lblStalls.Text = state.TotalStalls.ToString();
            if (txtPC != null)
                txtPC.Text = $"0x{state.PC:X4}";

            // Update pipeline stages
            UpdatePipelineStages(state);

            // Update space-time diagram
            UpdateSpaceTimeDiagram(state);

            // Update hazard log
            AppendHazardLog(state);

            // Update cache statistics
            UpdateCacheStats();
        }

        private void UpdateCacheStats()
        {
            if (lblICacheStats == null || lblDCacheStats == null) return;
            var ic = _simulator.InstructionCache;
            var dc = _simulator.DataCache;
            int iStall = _simulator.ICacheStallCycles;
            int dStall = _simulator.DCacheStallCycles;
            int hazStall = _simulator.TotalStalls;
            int totalCycles = _simulator.ClockCycle;
            int instrCount = _simulator.SpaceTimeTable.Count;

            lblICacheStats.Text = ic.TotalAccesses == 0
                ? $"ICache ({ic.NumSets}s×{ic.Associativity}w×{ic.BlockSizeBytes}B {ic.ReplacementPolicy} | penalty={_simulator.ICacheMissPenalty}): —"
                : $"ICache ({ic.NumSets}s×{ic.Associativity}w | pen={_simulator.ICacheMissPenalty}): {ic.TotalAccesses} acc | {ic.Hits} hits ({ic.HitRate:P0}) | {ic.Misses} miss → +{iStall} cicli stall";

            lblDCacheStats.Text = dc.TotalAccesses == 0
                ? $"DCache ({dc.NumSets}s×{dc.Associativity}w | pen={_simulator.DCacheMissPenalty}): —"
                : $"DCache ({dc.NumSets}s×{dc.Associativity}w | pen={_simulator.DCacheMissPenalty}): {dc.TotalAccesses} acc | {dc.Hits} hits ({dc.HitRate:P0}) | {dc.Misses} miss → +{dStall} cicli stall";

            if (lblCacheMetrics != null)
            {
                if (totalCycles == 0 || instrCount == 0)
                {
                    lblCacheMetrics.Text = "Cicli: — | Rulați simularea pentru metrici.";
                }
                else
                {
                    int pipelineCycles = totalCycles - hazStall - iStall - dStall;
                    double cpiReal = (double)totalCycles / instrCount;
                    double cpiIdeal = (double)pipelineCycles / instrCount;
                    int hypotheticalAllMiss = pipelineCycles + hazStall
                        + ic.TotalAccesses * _simulator.ICacheMissPenalty
                        + dc.TotalAccesses * _simulator.DCacheMissPenalty;
                    int saved = hypotheticalAllMiss - totalCycles;
                    lblCacheMetrics.Text =
                        $"Cicli totali: {totalCycles} = {pipelineCycles} pipeline + {hazStall} hazard + {iStall} ICache + {dStall} DCache stall | " +
                        $"CPI: {cpiReal:F2} (ideal fără miss: {cpiIdeal:F2}) | Cache economisește ~{saved} cicli vs. no-cache";
                }
            }
        }

        private void UpdatePipelineStages(PipelineState state)
        {
            if (lblIF_Content != null)
                lblIF_Content.Text = state.StageIF != null ? state.StageIF.ToShortString() : "---";
            if (lblDEC_Content != null)
                lblDEC_Content.Text = state.StageDEC != null ? state.StageDEC.ToShortString() : "---";
            if (lblEX_Content != null)
                lblEX_Content.Text = state.StageEX != null ? state.StageEX.ToShortString() : "---";
            if (lblMEM_Content != null)
                lblMEM_Content.Text = state.StageMEM != null ? state.StageMEM.ToShortString() : "---";
            if (lblWB_Content != null)
                lblWB_Content.Text = state.StageWB != null ? state.StageWB.ToShortString() : "---";
        }

        private void ClearPipelineStages()
        {
            if (lblIF_Content != null) lblIF_Content.Text = "---";
            if (lblDEC_Content != null) lblDEC_Content.Text = "---";
            if (lblEX_Content != null) lblEX_Content.Text = "---";
            if (lblMEM_Content != null) lblMEM_Content.Text = "---";
            if (lblWB_Content != null) lblWB_Content.Text = "---";
        }

        private void InitializeSpaceTimeDiagram(List<RISCInstruction> program)
        {
            dgvSpaceTime.Rows.Clear();
            foreach (var instr in program)
            {
                dgvSpaceTime.Rows.Add(instr.ToShortString());
            }
        }

        private void UpdateSpaceTimeDiagram(PipelineState state)
        {
            // Update based on SpaceTimeTable
            foreach (var entry in _simulator.SpaceTimeTable)
            {
                if (entry.InstructionIndex < dgvSpaceTime.Rows.Count)
                {
                    var row = dgvSpaceTime.Rows[entry.InstructionIndex];
                    string stage = entry.GetStage(state.ClockCycle);
                    if (!string.IsNullOrEmpty(stage) && state.ClockCycle < dgvSpaceTime.Columns.Count)
                    {
                        row.Cells[state.ClockCycle].Value = stage;
                        // Color code: IF=lightblue, DEC=yellow, EX=orange, MEM=lightgreen, WB=pink
                        // stall=red, cache=amber (cache miss penalty stall)
                        switch (stage)
                        {
                            case "IF":
                                row.Cells[state.ClockCycle].Style.BackColor = Color.LightBlue;
                                break;
                            case "DEC":
                                row.Cells[state.ClockCycle].Style.BackColor = Color.Yellow;
                                break;
                            case "EX":
                                row.Cells[state.ClockCycle].Style.BackColor = Color.Orange;
                                break;
                            case "MEM":
                                row.Cells[state.ClockCycle].Style.BackColor = Color.LightGreen;
                                break;
                            case "WB":
                                row.Cells[state.ClockCycle].Style.BackColor = Color.Pink;
                                break;
                            case "stall":
                                row.Cells[state.ClockCycle].Style.BackColor = Color.FromArgb(255, 100, 100);
                                row.Cells[state.ClockCycle].Style.ForeColor = Color.White;
                                break;
                            case "cache":
                                row.Cells[state.ClockCycle].Style.BackColor = Color.FromArgb(255, 165, 0);
                                row.Cells[state.ClockCycle].Style.ForeColor = Color.White;
                                row.Cells[state.ClockCycle].Value = "CS";
                                break;
                            default:
                                row.Cells[state.ClockCycle].Style.BackColor = Color.White;
                                break;
                        }
                    }
                }
            }
        }

        private void ClearSpaceTimeDiagram()
        {
            dgvSpaceTime.Rows.Clear();
        }

        private void AppendHazardLog(PipelineState state)
        {
            if (rtbHazardLog == null) return;

            // CRITICAL: Afișează LOG-UL COMPLET cu [WB], [EX], [MEM], etc.
            if (!string.IsNullOrWhiteSpace(state.LogText ?? state.LogMessage))
            {
                rtbHazardLog.AppendText((state.LogText ?? state.LogMessage) + "\n");
            }

            if (state.ActiveHazard != null && state.ActiveHazard.HasHazard)
            {
                rtbHazardLog.SelectionColor = Color.Red;
                rtbHazardLog.AppendText($"[Ciclu {state.ClockCycle}] HAZARD: {state.ActiveHazard.Description}\n");
                rtbHazardLog.SelectionColor = rtbHazardLog.ForeColor;
            }

            if (state.ActiveForwarding != null && state.ActiveForwarding.IsActive)
            {
                rtbHazardLog.SelectionColor = Color.Green;
                rtbHazardLog.AppendText($"[Ciclu {state.ClockCycle}] FORWARDING: {state.ActiveForwarding.Description}\n");
                rtbHazardLog.SelectionColor = rtbHazardLog.ForeColor;
            }

            // Auto-scroll
            rtbHazardLog.SelectionStart = rtbHazardLog.Text.Length;
            rtbHazardLog.ScrollToCaret();
        }

        private void OnRegisterChanged(int regIndex)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<int>(OnRegisterChanged), regIndex);
                return;
            }

            if (dgvRegisters == null || regIndex < 0 || regIndex >= dgvRegisters.Rows.Count)
                return;

            var value = _simulator.Registers.Read(regIndex);
            var valid = _simulator.Registers.IsValid(regIndex);

            dgvRegisters.Rows[regIndex].Cells[1].Value = $"0x{value:X8}";
            dgvRegisters.Rows[regIndex].Cells[2].Value = value.ToString();
            dgvRegisters.Rows[regIndex].Cells[3].Value = valid;

            // Highlight invalid registers
            dgvRegisters.Rows[regIndex].DefaultCellStyle.BackColor = valid ? Color.White : Color.LightYellow;
        }
    }
}
