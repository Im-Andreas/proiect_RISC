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
            var tab = new TabPage("Latency Configuration");

            var grp = new GroupBox { Text = "Fixed Latencies (cycles)", Dock = DockStyle.Top, Height = 200, Padding = new Padding(12) };

            grp.Controls.Add(new Label { Text = "TLB access:", Left = 20, Top = 30, AutoSize = true });
            _numTlbLatency = new NumericUpDown { Left = 180, Top = 27, Width = 90, Minimum = 0, Maximum = 10000, Value = 1 };
            grp.Controls.Add(_numTlbLatency);

            grp.Controls.Add(new Label { Text = "Cache access:", Left = 20, Top = 66, AutoSize = true });
            _numCacheLatency = new NumericUpDown { Left = 180, Top = 63, Width = 90, Minimum = 0, Maximum = 10000, Value = 1 };
            grp.Controls.Add(_numCacheLatency);

            grp.Controls.Add(new Label { Text = "Main Memory access:", Left = 20, Top = 102, AutoSize = true });
            _numMemLatency = new NumericUpDown { Left = 180, Top = 99, Width = 90, Minimum = 0, Maximum = 100000, Value = 100 };
            grp.Controls.Add(_numMemLatency);

            var btnApply = new Button
            {
                Text = "Apply Latencies",
                Left = 20,
                Top = 140,
                Width = 150,
                Height = 30,
                BackColor = Color.FromArgb(0x19, 0x76, 0xD2),
                ForeColor = Color.White
            };
            btnApply.Click += (s, e) =>
            {
                Mmu?.SetLatencies((int)_numTlbLatency.Value, (int)_numCacheLatency.Value, (int)_numMemLatency.Value);
                RefreshAll();
            };
            grp.Controls.Add(btnApply);

            grp.Controls.Add(new Label
            {
                Text = "Note: applied latencies affect the cost of subsequent accesses.\nRe-run the program (Reset + Run) to recompute all costs.",
                Left = 300,
                Top = 30,
                AutoSize = true,
                ForeColor = Color.Gray
            });

            tab.Controls.Add(grp);
            return tab;
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
            _dgvAccessLog.Rows.Clear();
            foreach (var r in Mmu.AccessLog)
            {
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
            if (_dgvAccessLog.Rows.Count > 0)
                _dgvAccessLog.FirstDisplayedScrollingRowIndex = _dgvAccessLog.Rows.Count - 1;
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
            _lblCycleStats.Text = $"Total accesses: {Mmu.TotalAccesses} | Total simulation cycles: {Mmu.SimulationCycles}";
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