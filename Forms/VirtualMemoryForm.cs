using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using proiect_RISC.Models;

namespace proiect_RISC.Forms
{
    public class VirtualMemoryForm : Form
    {
        private readonly PipelineSimulator _simulator;

        private DataGridView _dgvTlb;
        private DataGridView _dgvAccessLog;
        private DataGridView _dgvCases;
        private RichTextBox _rtbLog;

        private NumericUpDown _numTlbLatency, _numCacheLatency, _numMemLatency;
        private NumericUpDown _numTlbEntries, _numVirtualPages, _numPhysFrames;
        private NumericUpDown _numCacheAssoc;
        private ComboBox _cmbPageSize, _cmbCacheSets, _cmbCacheBlock, _cmbTlbReplace, _cmbCacheReplace;
        private CheckBox _chkVmStalls;
        private Label _lblVmStallStats;

        private Label _lblConfig;
        private Label _lblTlbStats;
        private Label _lblCacheStats;
        private Label _lblFaultStats;
        private Label _lblCycleStats;

        public VirtualMemoryForm() : this(null) { }

        public VirtualMemoryForm(PipelineSimulator simulator)
        {
            _simulator = simulator;
            this.Text = "Virtual Memory, TLB & MMU Simulator";
            this.Size = new Size(1180, 740);
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimumSize = new Size(1000, 640);

            var tc = new TabControl { Dock = DockStyle.Fill };
            tc.TabPages.Add(BuildTranslationTab());
            tc.TabPages.Add(BuildCasesTab());
            tc.TabPages.Add(BuildConfigTab());
            this.Controls.Add(tc);

            if (_simulator != null)
            {
                _simulator.CycleCompleted += OnSimulatorCycleCompleted;
                this.FormClosed += (s, e) => _simulator.CycleCompleted -= OnSimulatorCycleCompleted;
                this.Shown += (s, e) => RefreshAll();
            }
        }

        private MMU Mmu => _simulator?.VirtualMemory?.Mmu;

        private TabPage BuildTranslationTab()
        {
            var tab = new TabPage("TLB & Address Translation");

            var pnlTop = new Panel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(8), MinimumSize = new Size(0, 96) };
            _lblConfig = new Label { Left = 10, Top = 10, AutoSize = true, Font = new Font("Consolas", 8) };
            _lblTlbStats = new Label { Left = 10, Top = 38, AutoSize = true, Font = new Font("Consolas", 8) };
            _lblCacheStats = new Label { Left = 260, Top = 38, AutoSize = true, Font = new Font("Consolas", 8) };
            _lblFaultStats = new Label { Left = 560, Top = 38, AutoSize = true, Font = new Font("Consolas", 8) };
            _lblCycleStats = new Label { Left = 10, Top = 64, AutoSize = true, Font = new Font("Consolas", 8), ForeColor = Color.DarkRed };
            pnlTop.Controls.Add(_lblConfig);
            pnlTop.Controls.Add(_lblTlbStats);
            pnlTop.Controls.Add(_lblCacheStats);
            pnlTop.Controls.Add(_lblFaultStats);
            pnlTop.Controls.Add(_lblCycleStats);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal
            };
            split.HandleCreated += (s, e) =>
            {
                try
                {
                    split.Panel1MinSize = 80;
                    split.Panel2MinSize = 120;
                    int desired = 220;
                    int max = split.Height - split.Panel2MinSize;
                    int min = split.Panel1MinSize;
                    if (max > min)
                        split.SplitterDistance = Math.Max(min, Math.Min(desired, max));
                }
                catch { }
            };

            var grpTlb = new GroupBox { Text = "TLB (fully associative)", Dock = DockStyle.Fill, Padding = new Padding(6) };
            _dgvTlb = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false };
            _dgvTlb.Columns.Add("Way", "Entry");
            _dgvTlb.Columns.Add("Valid", "Valid");
            _dgvTlb.Columns.Add("VirtualPage", "Virtual Page (VPN)");
            _dgvTlb.Columns.Add("Frame", "Frame (PFN)");
            _dgvTlb.Columns.Add("LastUsed", "Last Used");
            grpTlb.Controls.Add(_dgvTlb);
            split.Panel1.Controls.Add(grpTlb);

            var grpLog = new GroupBox { Text = "Access Log (driven by loaded program)", Dock = DockStyle.Fill, Padding = new Padding(6) };
            _dgvAccessLog = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false };
            _dgvAccessLog.Columns.Add("Index", "#");
            _dgvAccessLog.Columns.Add("Cycle", "Cycle");
            _dgvAccessLog.Columns.Add("Type", "Type");
            _dgvAccessLog.Columns.Add("VA", "Virtual Addr");
            _dgvAccessLog.Columns.Add("VPN", "VPN");
            _dgvAccessLog.Columns.Add("Offset", "Offset");
            _dgvAccessLog.Columns.Add("PFN", "PFN");
            _dgvAccessLog.Columns.Add("PA", "Physical Addr");
            _dgvAccessLog.Columns.Add("TLB", "TLB");
            _dgvAccessLog.Columns.Add("PTE", "PTE");
            _dgvAccessLog.Columns.Add("Data", "Data");
            _dgvAccessLog.Columns.Add("Case", "Case");
            _dgvAccessLog.Columns.Add("Cost", "Cost (cyc)");
            grpLog.Controls.Add(_dgvAccessLog);

            var pnlLogBottom = new Panel { Dock = DockStyle.Bottom, Height = 150, MinimumSize = new Size(0, 100) };
            _rtbLog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 8) };
            var splitterLine = new Splitter { Dock = DockStyle.Bottom, Height = 4 };
            pnlLogBottom.Controls.Add(_rtbLog);
            grpLog.Controls.Add(pnlLogBottom);
            grpLog.Controls.Add(splitterLine);

            split.Panel2.Controls.Add(grpLog);

            tab.Controls.Add(split);
            tab.Controls.Add(pnlTop);

            return tab;
        }

        private TabPage BuildCasesTab()
        {
            var tab = new TabPage("6 Access Cases");

            var grp = new GroupBox { Text = "The 6 Core Cases (address source x data source)", Dock = DockStyle.Fill, Padding = new Padding(8) };
            _dgvCases = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false };
            _dgvCases.Columns.Add("Case", "Case");
            _dgvCases.Columns.Add("Translation", "Translation");
            _dgvCases.Columns.Add("Data", "Data");
            _dgvCases.Columns.Add("Description", "Description");
            _dgvCases.Columns.Add("Count", "Times Hit");
            _dgvCases.Columns.Add("TotalCost", "Total Cost");
            _dgvCases.Columns["Description"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            var cases = new (int n, string t, string d, string desc)[]
            {
                (1, "TLB Hit", "Cache Hit", "Return data immediately. Lowest latency."),
                (2, "TLB Hit", "Cache Miss (MP)", "Fetch data from MP, load into L1 cache, return data."),
                (3, "TLB Miss, PTE in Cache", "Cache Hit", "Fetch PTE from cache -> update TLB -> fetch data from cache."),
                (4, "TLB Miss, PTE in Cache", "Cache Miss (MP)", "Fetch PTE from cache -> update TLB -> fetch data from MP -> load into cache."),
                (5, "TLB Miss, PTE in MP", "Cache Hit", "Fetch PTE from MP -> load PTE into cache -> update TLB -> fetch data from cache."),
                (6, "TLB Miss, PTE in MP", "Cache Miss (MP)", "Worst case: PTE from MP + data from MP, both loaded into cache.")
            };
            foreach (var c in cases)
            {
                int idx = _dgvCases.Rows.Add(c.n, c.t, c.d, c.desc, 0, 0);
                _dgvCases.Rows[idx].DefaultCellStyle.BackColor = CaseColor(c.n);
            }

            grp.Controls.Add(_dgvCases);
            tab.Controls.Add(grp);

            return tab;
        }

        private TabPage BuildConfigTab()
        {
            var tab = new TabPage("Configurare");
            var outer = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

            // ── Structura MMU ────────────────────────────────────────────────────
            var grpStruct = new GroupBox { Text = "Structura MMU (se aplică la Reset)", Left = 8, Top = 8, Width = 700, Height = 230, Padding = new Padding(10) };

            int col1 = 20, col2 = 180, col3 = 380, col4 = 520, row = 28;

            grpStruct.Controls.Add(new Label { Text = "Dimensiune pagină (B):", Left = col1, Top = row, AutoSize = true });
            _cmbPageSize = new ComboBox { Left = col2, Top = row - 3, Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (int v in new[] { 16, 32, 64, 128, 256, 512 }) _cmbPageSize.Items.Add(v);
            _cmbPageSize.SelectedItem = Mmu?.PageSizeBytes ?? 64;
            grpStruct.Controls.Add(_cmbPageSize);

            grpStruct.Controls.Add(new Label { Text = "Pagini virtuale:", Left = col3, Top = row, AutoSize = true });
            _numVirtualPages = new NumericUpDown { Left = col4, Top = row - 3, Width = 90, Minimum = 4, Maximum = 1024, Value = Mmu?.NumVirtualPages ?? 64 };
            grpStruct.Controls.Add(_numVirtualPages);

            row += 36;
            grpStruct.Controls.Add(new Label { Text = "Cadre fizice:", Left = col1, Top = row, AutoSize = true });
            _numPhysFrames = new NumericUpDown { Left = col2, Top = row - 3, Width = 90, Minimum = 2, Maximum = 256, Value = Mmu?.NumPhysicalFrames ?? 16 };
            grpStruct.Controls.Add(_numPhysFrames);

            grpStruct.Controls.Add(new Label { Text = "Intrări TLB:", Left = col3, Top = row, AutoSize = true });
            _numTlbEntries = new NumericUpDown { Left = col4, Top = row - 3, Width = 90, Minimum = 1, Maximum = 64, Value = Mmu?.Tlb.NumEntries ?? 4 };
            grpStruct.Controls.Add(_numTlbEntries);

            row += 36;
            grpStruct.Controls.Add(new Label { Text = "Politică înlocuire TLB:", Left = col1, Top = row, AutoSize = true });
            _cmbTlbReplace = new ComboBox { Left = col2, Top = row - 3, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbTlbReplace.Items.AddRange(new object[] { "LRU", "Random" });
            _cmbTlbReplace.SelectedIndex = 0;
            grpStruct.Controls.Add(_cmbTlbReplace);

            row += 36;
            grpStruct.Controls.Add(new Label { Text = "Cache seturi:", Left = col1, Top = row, AutoSize = true });
            _cmbCacheSets = new ComboBox { Left = col2, Top = row - 3, Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (int v in new[] { 1, 2, 4, 8, 16, 32 }) _cmbCacheSets.Items.Add(v);
            _cmbCacheSets.SelectedItem = Mmu?.DataCache.NumSets ?? 8;
            grpStruct.Controls.Add(_cmbCacheSets);

            grpStruct.Controls.Add(new Label { Text = "Asociativitate cache:", Left = col3, Top = row, AutoSize = true });
            _numCacheAssoc = new NumericUpDown { Left = col4, Top = row - 3, Width = 90, Minimum = 1, Maximum = 16, Value = Mmu?.DataCache.Associativity ?? 2 };
            grpStruct.Controls.Add(_numCacheAssoc);

            row += 36;
            grpStruct.Controls.Add(new Label { Text = "Bloc cache (B):", Left = col1, Top = row, AutoSize = true });
            _cmbCacheBlock = new ComboBox { Left = col2, Top = row - 3, Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (int v in new[] { 4, 8, 16, 32, 64 }) _cmbCacheBlock.Items.Add(v);
            _cmbCacheBlock.SelectedItem = Mmu?.DataCache.BlockSizeBytes ?? 16;
            grpStruct.Controls.Add(_cmbCacheBlock);

            grpStruct.Controls.Add(new Label { Text = "Politică înlocuire cache:", Left = col3, Top = row, AutoSize = true });
            _cmbCacheReplace = new ComboBox { Left = col4, Top = row - 3, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbCacheReplace.Items.AddRange(new object[] { "LRU", "Random" });
            _cmbCacheReplace.SelectedIndex = 0;
            grpStruct.Controls.Add(_cmbCacheReplace);

            var btnApplyStruct = new Button
            {
                Text = "Aplică structura",
                Left = col1, Top = row + 40, Width = 150, Height = 30,
                BackColor = Color.FromArgb(0x19, 0x76, 0xD2), ForeColor = Color.White
            };
            btnApplyStruct.Click += (s, e) => ApplyMmuStructure();
            grpStruct.Controls.Add(btnApplyStruct);

            grpStruct.Controls.Add(new Label
            {
                Text = "Notă: modificarea structurii resetează TLB, cache și logul de acces.",
                Left = col2 + 10, Top = row + 45, AutoSize = true, ForeColor = Color.Gray
            });

            outer.Controls.Add(grpStruct);

            // ── Latențe ─────────────────────────────────────────────────────────
            var grpLat = new GroupBox { Text = "Latențe fixe (cicli)", Left = 8, Top = 250, Width = 700, Height = 160, Padding = new Padding(10) };
            int lr = 28;
            grpLat.Controls.Add(new Label { Text = "Acces TLB:", Left = 20, Top = lr, AutoSize = true });
            _numTlbLatency = new NumericUpDown { Left = 180, Top = lr - 3, Width = 90, Minimum = 0, Maximum = 10000, Value = Mmu?.Latencies.TlbCycles ?? 1 };
            grpLat.Controls.Add(_numTlbLatency);

            lr += 36;
            grpLat.Controls.Add(new Label { Text = "Acces cache:", Left = 20, Top = lr, AutoSize = true });
            _numCacheLatency = new NumericUpDown { Left = 180, Top = lr - 3, Width = 90, Minimum = 0, Maximum = 10000, Value = Mmu?.Latencies.CacheCycles ?? 1 };
            grpLat.Controls.Add(_numCacheLatency);

            lr += 36;
            grpLat.Controls.Add(new Label { Text = "Acces memorie principală:", Left = 20, Top = lr, AutoSize = true });
            _numMemLatency = new NumericUpDown { Left = 180, Top = lr - 3, Width = 90, Minimum = 0, Maximum = 100000, Value = Mmu?.Latencies.MainMemoryCycles ?? 100 };
            grpLat.Controls.Add(_numMemLatency);

            var btnApplyLat = new Button
            {
                Text = "Aplică latențe",
                Left = 20, Top = lr + 36, Width = 150, Height = 30,
                BackColor = Color.FromArgb(0x19, 0x76, 0xD2), ForeColor = Color.White
            };
            btnApplyLat.Click += (s, e) =>
            {
                Mmu?.SetLatencies((int)_numTlbLatency.Value, (int)_numCacheLatency.Value, (int)_numMemLatency.Value);
                RefreshAll();
            };
            grpLat.Controls.Add(btnApplyLat);
            outer.Controls.Add(grpLat);

            // ── Stall-uri VM în pipeline ─────────────────────────────────────────
            var grpVmStall = new GroupBox { Text = "Stall-uri TLB în pipeline", Left = 8, Top = 422, Width = 700, Height = 120, Padding = new Padding(10) };
            _chkVmStalls = new CheckBox
            {
                Text = "Activează stall-uri TLB în pipeline (cicli penalizare pentru TLB miss)",
                Left = 20, Top = 28, Width = 600, Height = 24,
                Checked = _simulator?.VmStallsEnabled ?? true
            };
            _chkVmStalls.CheckedChanged += (s, e) =>
            {
                if (_simulator != null) _simulator.VmStallsEnabled = _chkVmStalls.Checked;
            };
            grpVmStall.Controls.Add(_chkVmStalls);

            _lblVmStallStats = new Label
            {
                Left = 20, Top = 58, AutoSize = true,
                Font = new Font("Consolas", 8.5f), ForeColor = Color.DarkRed
            };
            grpVmStall.Controls.Add(_lblVmStallStats);

            grpVmStall.Controls.Add(new Label
            {
                Text = "Costul stall = latență_cache (PTE în cache) sau latență_cache + latență_MP (PTE în MP).\nSe adaugă la penalizarea de DCache miss — modele ortogonale.",
                Left = 20, Top = 80, AutoSize = true, ForeColor = Color.Gray
            });
            outer.Controls.Add(grpVmStall);

            tab.Controls.Add(outer);
            return tab;
        }

        private void ApplyMmuStructure()
        {
            if (Mmu == null) return;
            try
            {
                int pageSize = (int)(_cmbPageSize.SelectedItem ?? 64);
                int virtPages = (int)_numVirtualPages.Value;
                int physFrames = (int)_numPhysFrames.Value;
                int tlbEntries = (int)_numTlbEntries.Value;
                int cacheSets = (int)(_cmbCacheSets.SelectedItem ?? 8);
                int cacheAssoc = (int)_numCacheAssoc.Value;
                int cacheBlock = (int)(_cmbCacheBlock.SelectedItem ?? 16);
                var tlbRepl = _cmbTlbReplace.SelectedIndex == 0 ? ReplacementPolicy.LRU : ReplacementPolicy.Random;
                var cacheRepl = _cmbCacheReplace.SelectedIndex == 0 ? ReplacementPolicy.LRU : ReplacementPolicy.Random;

                Mmu.Configure(pageSize, virtPages, physFrames, tlbEntries, cacheSets, cacheAssoc, cacheBlock, tlbRepl, cacheRepl);
                Mmu.SetLatencies((int)_numTlbLatency.Value, (int)_numCacheLatency.Value, (int)_numMemLatency.Value);
                RefreshAll();
                MessageBox.Show("Structura MMU aplicată cu succes.\nResetează și rulează programul din nou pentru a vedea efectele.", "Aplicat", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Eroare la configurare: {ex.Message}", "Eroare", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnSimulatorCycleCompleted(PipelineState state)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { Invoke(new Action(RefreshAll)); } catch { }
                return;
            }
            RefreshAll();
        }

        private void RefreshAll()
        {
            if (Mmu == null) return;
            RefreshConfig();
            RefreshTlb();
            RefreshAccessLog();
            RefreshCaseCounts();
            RefreshStats();
            RefreshTextLog();
        }

        private void RefreshConfig()
        {
            _lblConfig.Text = $"Page: {Mmu.PageSizeBytes}B | Virtual pages: {Mmu.NumVirtualPages} | Frames: {Mmu.NumPhysicalFrames} | " +
                              $"TLB: {Mmu.Tlb.NumEntries} entries | L1 Cache: {Mmu.DataCache.NumSets}s x {Mmu.DataCache.Associativity}w x {Mmu.DataCache.BlockSizeBytes}B | " +
                              $"Latencies: TLB={Mmu.Latencies.TlbCycles} Cache={Mmu.Latencies.CacheCycles} MP={Mmu.Latencies.MainMemoryCycles}";
        }

        private void RefreshTlb()
        {
            _dgvTlb.Rows.Clear();
            var entries = Mmu.Tlb.Entries;
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                int idx = _dgvTlb.Rows.Add(
                    i,
                    e.Valid,
                    e.Valid ? e.VirtualPage.ToString() : "-",
                    e.Valid ? e.FrameNumber.ToString() : "-",
                    e.Valid ? e.LastUsedCycle.ToString() : "-");
                if (e.Valid) _dgvTlb.Rows[idx].DefaultCellStyle.BackColor = Color.Honeydew;
            }
        }

        private void RefreshAccessLog()
        {
            var log = Mmu.AccessLog;
            if (log.Count < _dgvAccessLog.Rows.Count) _dgvAccessLog.Rows.Clear();
            for (int i = _dgvAccessLog.Rows.Count; i < log.Count; i++)
            {
                var r = log[i];
                int idx = _dgvAccessLog.Rows.Add(
                    r.Index,
                    r.ClockCycle,
                    r.AccessType,
                    $"0x{r.VirtualAddress:X4}",
                    r.VirtualPage,
                    r.PageOffset,
                    r.FrameNumber,
                    $"0x{r.PhysicalAddress:X4}",
                    r.TlbHit ? "HIT" : "MISS",
                    r.TlbHit ? "-" : (r.PteInCache ? "cache" : "MP"),
                    r.DataInCache ? "cache" : "MP",
                    (int)r.Case,
                    r.CyclesCost);
                _dgvAccessLog.Rows[idx].DefaultCellStyle.BackColor = CaseColor((int)r.Case);
            }
        }

        private void RefreshCaseCounts()
        {
            for (int i = 0; i < _dgvCases.Rows.Count; i++)
            {
                _dgvCases.Rows[i].Cells["Count"].Value = Mmu.CaseCounts[i + 1];
                _dgvCases.Rows[i].Cells["TotalCost"].Value = Mmu.CaseCycles[i + 1];
            }
        }

        private void RefreshStats()
        {
            _lblTlbStats.Text = $"TLB: {Mmu.TlbHits} hits / {Mmu.TlbMisses} miss ({Mmu.Tlb.HitRate:P0})";
            _lblCacheStats.Text = $"Cache: {Mmu.CacheHits} hits / {Mmu.CacheMisses} miss ({Mmu.DataCache.HitRate:P0})";
            _lblFaultStats.Text = $"Page faults: {Mmu.PageFaults}";
            int pipelineCycle = _simulator?.ClockCycle ?? 0;
            _lblCycleStats.Text = $"Total accesses: {Mmu.TotalAccesses} | Latență cumulată MMU: {Mmu.SimulationCycles} cicli (≠ clock cycles pipeline={pipelineCycle})";
            if (_lblVmStallStats != null && _simulator != null)
                _lblVmStallStats.Text = $"Stall-uri TLB pipeline: IF={_simulator.VmInstStallCycles} cicli  |  MEM={_simulator.VmDataStallCycles} cicli  |  Total={_simulator.VmInstStallCycles + _simulator.VmDataStallCycles} cicli";
        }

        private void RefreshTextLog()
        {
            _rtbLog.Clear();
            foreach (var r in Mmu.AccessLog)
            {
                _rtbLog.SelectionColor = CaseTextColor((int)r.Case);
                _rtbLog.AppendText($"[#{r.Index}] cyc {r.ClockCycle} 0x{r.VirtualAddress:X4} -> {r.Description}\n");
            }
            _rtbLog.SelectionColor = _rtbLog.ForeColor;
            _rtbLog.SelectionStart = _rtbLog.Text.Length;
            _rtbLog.ScrollToCaret();
        }

        private static Color CaseColor(int c)
        {
            switch (c)
            {
                case 1: return Color.FromArgb(0xE8, 0xF5, 0xE9);
                case 2: return Color.FromArgb(0xFF, 0xF9, 0xC4);
                case 3: return Color.FromArgb(0xE3, 0xF2, 0xFD);
                case 4: return Color.FromArgb(0xFF, 0xE0, 0xB2);
                case 5: return Color.FromArgb(0xF3, 0xE5, 0xF5);
                case 6: return Color.FromArgb(0xFF, 0xCD, 0xD2);
                default: return Color.White;
            }
        }

        private static Color CaseTextColor(int c)
        {
            switch (c)
            {
                case 1: return Color.DarkGreen;
                case 6: return Color.DarkRed;
                default: return Color.Black;
            }
        }
    }
}