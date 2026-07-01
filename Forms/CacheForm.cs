using System;
using System.Drawing;
using System.Windows.Forms;
using proiect_RISC.Models;

namespace proiect_RISC.Forms
{
    public class CacheForm : Form
    {
        private readonly CacheBlackBox _cache = new CacheBlackBox();
        private readonly SetAssociativeCache _saCache = new SetAssociativeCache(4, 2, 16);
        private readonly WritePolicyCache _wpCache = new WritePolicyCache(4, 2, 16);

        private DataGridView _dgvLog;
        private TextBox _txtAddress;
        private Label _lblTotal, _lblHits, _lblMisses, _lblHitRate, _lblMissRate;

        private NumericUpDown _numSets, _numWays, _numBlockSize;
        private ComboBox _cmbSaReplacement;
        private TextBox _txtSaAddress;
        private DataGridView _dgvSaLog;
        private DataGridView _dgvSaContents;
        private Label _lblSaTotal, _lblSaHits, _lblSaMisses, _lblSaHitRate;

        private NumericUpDown _numWpSets, _numWpWays, _numWpBlockSize;
        private ComboBox _cmbWritePolicy, _cmbWriteMissPolicy, _cmbWpReplacement;
        private TextBox _txtWpAddress;
        private RadioButton _rbWpRead, _rbWpWrite;
        private DataGridView _dgvWpLog, _dgvWpContents;
        private Label _lblWpTotal, _lblWpReads, _lblWpWrites, _lblWpHits, _lblWpMisses, _lblWpHitRate;
        private Label _lblWpMemReads, _lblWpMemWrites, _lblWpWriteBacks;

        public CacheForm()
        {
            this.Text = "Cache Memory Hierarchy";
            this.Size = new Size(1200, 750);

            var tc = new TabControl { Dock = DockStyle.Fill };

            var tabBlackBox = new TabPage("Cache Overview (Black-Box)");
            BuildBlackBoxTab(tabBlackBox);
            tc.TabPages.Add(tabBlackBox);

            var tabSetAssoc = new TabPage("Cache Configuration & Internals");
            BuildSetAssociativeTab(tabSetAssoc);
            tc.TabPages.Add(tabSetAssoc);

            var tabWritePolicy = new TabPage("Write Policies (Through / Back)");
            BuildWritePolicyTab(tabWritePolicy);
            tc.TabPages.Add(tabWritePolicy);

            this.Controls.Add(tc);
        }

        private void BuildBlackBoxTab(TabPage tab)
        {
            var pnlLeft = new Panel { Dock = DockStyle.Left, Width = 300, Padding = new Padding(12) };

            var lblTitle = new Label
            {
                Text = "Memory Access (Black-Box)",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                AutoSize = true,
                Top = 12,
                Left = 12
            };

            var lblHelp = new Label
            {
                Text = "Enter an address (decimal or 0x...) and press Access.\n" +
                       "First time you see an address = MISS.\n" +
                       "Next time the same address = HIT.",
                AutoSize = true,
                MaximumSize = new Size(270, 0),
                Top = 45,
                Left = 12,
                ForeColor = Color.DimGray
            };

            _txtAddress = new TextBox { Top = 110, Left = 12, Width = 160, Text = "0x0100" };

            var btnAccess = new Button
            {
                Text = "Access",
                Top = 108,
                Left = 180,
                Width = 90,
                BackColor = Color.FromArgb(0x19, 0x76, 0xD2),
                ForeColor = Color.White
            };
            btnAccess.Click += (s, e) => DoAccess();
            _txtAddress.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { DoAccess(); e.SuppressKeyPress = true; } };

            var lblSeq = new Label { Text = "Or a sequence (comma/space separated):", AutoSize = true, Top = 150, Left = 12 };
            var txtSeq = new TextBox { Top = 172, Left = 12, Width = 258, Text = "0x100, 0x104, 0x100, 0x108, 0x104" };
            var btnRunSeq = new Button { Text = "Run sequence", Top = 200, Left = 12, Width = 160 };
            btnRunSeq.Click += (s, e) => RunSequence(txtSeq.Text);

            var btnReset = new Button { Text = "Reset", Top = 200, Left = 180, Width = 90 };
            btnReset.Click += (s, e) => { _cache.Reset(); RefreshUI(); };

            pnlLeft.Controls.AddRange(new Control[]
            { lblTitle, lblHelp, _txtAddress, btnAccess, lblSeq, txtSeq, btnRunSeq, btnReset });

            var pnlStats = new Panel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(12, 8, 12, 8) };
            _lblTotal = MakeStat("Accesses: 0", 12);
            _lblHits = MakeStat("Hits: 0", 160);
            _lblMisses = MakeStat("Misses: 0", 300);
            _lblHitRate = MakeStat("Hit rate: 0%", 450);
            _lblMissRate = MakeStat("Miss rate: 0%", 640);
            pnlStats.Controls.AddRange(new Control[] { _lblTotal, _lblHits, _lblMisses, _lblHitRate, _lblMissRate });

            _dgvLog = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _dgvLog.Columns.Add("Nr", "#");
            _dgvLog.Columns.Add("Addr", "Address");
            _dgvLog.Columns.Add("Result", "Result");

            tab.Controls.Add(_dgvLog);
            tab.Controls.Add(pnlStats);
            tab.Controls.Add(pnlLeft);
        }

        private void BuildSetAssociativeTab(TabPage tab)
        {
            var pnlLeft = new Panel { Dock = DockStyle.Left, Width = 300, Padding = new Padding(12) };

            var lblCfgTitle = new Label
            {
                Text = "Cache Parameters (Set-Associative)",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                AutoSize = true,
                Top = 12,
                Left = 12
            };

            var lblSets = new Label { Text = "Number of sets (power of 2):", AutoSize = true, Top = 45, Left = 12 };
            _numSets = new NumericUpDown { Top = 65, Left = 12, Width = 100, Minimum = 1, Maximum = 1024, Value = 4 };

            var lblWays = new Label { Text = "Associativity (ways):", AutoSize = true, Top = 95, Left = 12 };
            _numWays = new NumericUpDown { Top = 115, Left = 12, Width = 100, Minimum = 1, Maximum = 16, Value = 2 };

            var lblBlock = new Label { Text = "Block size (bytes, power of 2):", AutoSize = true, Top = 145, Left = 12 };
            _numBlockSize = new NumericUpDown { Top = 165, Left = 12, Width = 100, Minimum = 1, Maximum = 1024, Value = 16 };

            var lblReplacement = new Label { Text = "Replacement policy:", AutoSize = true, Top = 195, Left = 12 };
            _cmbSaReplacement = new ComboBox { Top = 215, Left = 12, Width = 258, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbSaReplacement.Items.Add("Random");
            _cmbSaReplacement.Items.Add("LRU (exact)");
            _cmbSaReplacement.Items.Add("LRU (approximate / clock)");
            _cmbSaReplacement.SelectedIndex = 0;

            var btnApply = new Button { Text = "Apply Parameters (Reset)", Top = 250, Left = 12, Width = 258 };
            btnApply.Click += (s, e) => ApplySaConfig();

            var lblAccess = new Label { Text = "Address to access:", AutoSize = true, Top = 290, Left = 12 };
            _txtSaAddress = new TextBox { Top = 310, Left = 12, Width = 160, Text = "0x0100" };
            var btnSaAccess = new Button
            {
                Text = "Access",
                Top = 308,
                Left = 180,
                Width = 90,
                BackColor = Color.FromArgb(0x19, 0x76, 0xD2),
                ForeColor = Color.White
            };
            btnSaAccess.Click += (s, e) => DoSaAccess();
            _txtSaAddress.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { DoSaAccess(); e.SuppressKeyPress = true; } };

            var btnSaReset = new Button { Text = "Reset Statistics", Top = 345, Left = 12, Width = 258 };
            btnSaReset.Click += (s, e) => { _saCache.Reset(); RefreshSaUI(); };

            pnlLeft.Controls.AddRange(new Control[]
            {
                lblCfgTitle, lblSets, _numSets, lblWays, _numWays, lblBlock, _numBlockSize,
                lblReplacement, _cmbSaReplacement, btnApply, lblAccess, _txtSaAddress, btnSaAccess, btnSaReset
            });

            var pnlStats = new Panel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(12, 8, 12, 8) };
            _lblSaTotal = MakeStat("Accesses: 0", 12);
            _lblSaHits = MakeStat("Hits: 0", 160);
            _lblSaMisses = MakeStat("Misses: 0", 300);
            _lblSaHitRate = MakeStat("Hit rate: 0%", 450);
            pnlStats.Controls.AddRange(new Control[] { _lblSaTotal, _lblSaHits, _lblSaMisses, _lblSaHitRate });

            _dgvSaLog = new DataGridView
            {
                Dock = DockStyle.Top,
                Height = 260,
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _dgvSaLog.Columns.Add("Nr", "#");
            _dgvSaLog.Columns.Add("Addr", "Address");
            _dgvSaLog.Columns.Add("Tag", "Tag");
            _dgvSaLog.Columns.Add("SetIdx", "Set");
            _dgvSaLog.Columns.Add("Offset", "Offset");
            _dgvSaLog.Columns.Add("Way", "Way");
            _dgvSaLog.Columns.Add("Result", "Result");

            _dgvSaContents = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _dgvSaContents.Columns.Add("Set", "Set #");
            _dgvSaContents.Columns.Add("Way", "Way #");
            _dgvSaContents.Columns.Add("Valid", "Valid");
            _dgvSaContents.Columns.Add("TagVal", "Tag");

            tab.Controls.Add(_dgvSaContents);
            tab.Controls.Add(_dgvSaLog);
            tab.Controls.Add(pnlStats);
            tab.Controls.Add(pnlLeft);

            RefreshSaUI();
        }

        private void BuildWritePolicyTab(TabPage tab)
        {
            var pnlLeft = new Panel { Dock = DockStyle.Left, Width = 320, Padding = new Padding(12) };

            var lblTitle = new Label
            {
                Text = "Write Policies (Through / Back)",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                AutoSize = true,
                Top = 12,
                Left = 12
            };

            var lblSets = new Label { Text = "Number of sets (power of 2):", AutoSize = true, Top = 45, Left = 12 };
            _numWpSets = new NumericUpDown { Top = 65, Left = 12, Width = 100, Minimum = 1, Maximum = 1024, Value = 4 };

            var lblWays = new Label { Text = "Associativity (ways):", AutoSize = true, Top = 95, Left = 12 };
            _numWpWays = new NumericUpDown { Top = 115, Left = 12, Width = 100, Minimum = 1, Maximum = 16, Value = 2 };

            var lblBlock = new Label { Text = "Block size (bytes, power of 2):", AutoSize = true, Top = 145, Left = 12 };
            _numWpBlockSize = new NumericUpDown { Top = 165, Left = 12, Width = 100, Minimum = 1, Maximum = 1024, Value = 16 };

            var lblWritePolicy = new Label { Text = "Write hit policy:", AutoSize = true, Top = 195, Left = 12 };
            _cmbWritePolicy = new ComboBox { Top = 215, Left = 12, Width = 290, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbWritePolicy.Items.Add("Write-Through");
            _cmbWritePolicy.Items.Add("Write-Back");
            _cmbWritePolicy.SelectedIndex = 0;

            var lblMissPolicy = new Label { Text = "Write miss policy:", AutoSize = true, Top = 245, Left = 12 };
            _cmbWriteMissPolicy = new ComboBox { Top = 265, Left = 12, Width = 290, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbWriteMissPolicy.Items.Add("Write-Allocate");
            _cmbWriteMissPolicy.Items.Add("No-Write-Allocate");
            _cmbWriteMissPolicy.SelectedIndex = 0;

            var lblWpReplacement = new Label { Text = "Replacement policy:", AutoSize = true, Top = 295, Left = 12 };
            _cmbWpReplacement = new ComboBox { Top = 315, Left = 12, Width = 290, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbWpReplacement.Items.Add("Random");
            _cmbWpReplacement.Items.Add("LRU (exact)");
            _cmbWpReplacement.Items.Add("LRU (approximate / clock)");
            _cmbWpReplacement.SelectedIndex = 0;

            var btnApply = new Button { Text = "Apply Parameters (Reset)", Top = 350, Left = 12, Width = 290 };
            btnApply.Click += (s, e) => ApplyWpConfig();

            var lblAccess = new Label { Text = "Address to access:", AutoSize = true, Top = 390, Left = 12 };
            _txtWpAddress = new TextBox { Top = 410, Left = 12, Width = 160, Text = "0x0100" };

            _rbWpRead = new RadioButton { Text = "Read", Top = 412, Left = 180, Width = 60, Checked = true };
            _rbWpWrite = new RadioButton { Text = "Write", Top = 412, Left = 245, Width = 60 };

            var btnWpAccess = new Button
            {
                Text = "Access",
                Top = 440,
                Left = 12,
                Width = 160,
                BackColor = Color.FromArgb(0x19, 0x76, 0xD2),
                ForeColor = Color.White
            };
            btnWpAccess.Click += (s, e) => DoWpAccess();
            _txtWpAddress.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { DoWpAccess(); e.SuppressKeyPress = true; } };

            var btnWpReset = new Button { Text = "Reset Statistics", Top = 440, Left = 180, Width = 122 };
            btnWpReset.Click += (s, e) => { _wpCache.Reset(); RefreshWpUI(); };

            var lblSeq = new Label { Text = "Sequence (R0x100 read, W0x104 write):", AutoSize = true, Top = 480, Left = 12 };
            var txtSeq = new TextBox { Top = 500, Left = 12, Width = 290, Text = "W0x100, R0x100, W0x110, R0x120, W0x100" };
            var btnRunSeq = new Button { Text = "Run sequence", Top = 528, Left = 12, Width = 290 };
            btnRunSeq.Click += (s, e) => RunWpSequence(txtSeq.Text);

            pnlLeft.Controls.AddRange(new Control[]
            {
                lblTitle, lblSets, _numWpSets, lblWays, _numWpWays, lblBlock, _numWpBlockSize,
                lblWritePolicy, _cmbWritePolicy, lblMissPolicy, _cmbWriteMissPolicy,
                lblWpReplacement, _cmbWpReplacement, btnApply,
                lblAccess, _txtWpAddress, _rbWpRead, _rbWpWrite, btnWpAccess, btnWpReset,
                lblSeq, txtSeq, btnRunSeq
            });

            var pnlStats = new Panel { Dock = DockStyle.Bottom, Height = 70, Padding = new Padding(12, 8, 12, 8) };
            _lblWpTotal = MakeStat("Accesses: 0", 12);
            _lblWpReads = MakeStat("Reads: 0", 150);
            _lblWpWrites = MakeStat("Writes: 0", 270);
            _lblWpHits = MakeStat("Hits: 0", 390);
            _lblWpMisses = MakeStat("Misses: 0", 500);
            _lblWpHitRate = MakeStat("Hit rate: 0%", 620);
            _lblWpMemReads = MakeStatRow2("Mem reads: 0", 12);
            _lblWpMemWrites = MakeStatRow2("Mem writes: 0", 150);
            _lblWpWriteBacks = MakeStatRow2("Write-backs: 0", 300);
            pnlStats.Controls.AddRange(new Control[]
            {
                _lblWpTotal, _lblWpReads, _lblWpWrites, _lblWpHits, _lblWpMisses, _lblWpHitRate,
                _lblWpMemReads, _lblWpMemWrites, _lblWpWriteBacks
            });

            _dgvWpLog = new DataGridView
            {
                Dock = DockStyle.Top,
                Height = 280,
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _dgvWpLog.Columns.Add("Nr", "#");
            _dgvWpLog.Columns.Add("Op", "Op");
            _dgvWpLog.Columns.Add("Addr", "Address");
            _dgvWpLog.Columns.Add("Tag", "Tag");
            _dgvWpLog.Columns.Add("SetIdx", "Set");
            _dgvWpLog.Columns.Add("Way", "Way");
            _dgvWpLog.Columns.Add("Result", "Result");
            _dgvWpLog.Columns.Add("MemAction", "Memory Action");

            _dgvWpContents = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _dgvWpContents.Columns.Add("Set", "Set #");
            _dgvWpContents.Columns.Add("Way", "Way #");
            _dgvWpContents.Columns.Add("Valid", "Valid");
            _dgvWpContents.Columns.Add("Dirty", "Dirty");
            _dgvWpContents.Columns.Add("TagVal", "Tag");

            tab.Controls.Add(_dgvWpContents);
            tab.Controls.Add(_dgvWpLog);
            tab.Controls.Add(pnlStats);
            tab.Controls.Add(pnlLeft);

            RefreshWpUI();
        }

        private void ApplySaConfig()
        {
            try
            {
                ReplacementPolicy policy;
                if (_cmbSaReplacement.SelectedIndex == 1) policy = ReplacementPolicy.LRU;
                else if (_cmbSaReplacement.SelectedIndex == 2) policy = ReplacementPolicy.LRUApprox;
                else policy = ReplacementPolicy.Random;
                _saCache.Configure((int)_numSets.Value, (int)_numWays.Value, (int)_numBlockSize.Value, policy);
                RefreshSaUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Invalid Parameters", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void DoSaAccess()
        {
            if (TryParseAddress(_txtSaAddress.Text, out uint addr))
            {
                _saCache.Access(addr);
                RefreshSaUI();
            }
            else
            {
                MessageBox.Show("Invalid address. Use decimal (256) or hex (0x100).",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RefreshSaUI()
        {
            _dgvSaLog.Rows.Clear();
            foreach (var a in _saCache.AccessLog)
            {
                int rowIdx = _dgvSaLog.Rows.Add(
                    a.Index, $"0x{a.Address:X4}", $"0x{a.Tag:X}", a.SetIndex, a.Offset, a.WayUsed,
                    a.IsHit ? "HIT" : (a.WasEviction ? "MISS (evict)" : "MISS"));
                _dgvSaLog.Rows[rowIdx].Cells["Result"].Style.BackColor =
                    a.IsHit ? Color.FromArgb(0xC8, 0xE6, 0xC9) : Color.FromArgb(0xFF, 0xCD, 0xD2);
            }
            if (_dgvSaLog.Rows.Count > 0)
                _dgvSaLog.FirstDisplayedScrollingRowIndex = _dgvSaLog.Rows.Count - 1;

            _dgvSaContents.Rows.Clear();
            for (int s = 0; s < _saCache.NumSets; s++)
            {
                var set = _saCache.GetSet(s);
                for (int w = 0; w < set.Associativity; w++)
                {
                    var line = set.Ways[w];
                    _dgvSaContents.Rows.Add(s, w, line.Valid ? "1" : "0", line.Valid ? $"0x{line.Tag:X}" : "-");
                }
            }

            _lblSaTotal.Text = $"Accesses: {_saCache.TotalAccesses}";
            _lblSaHits.Text = $"Hits: {_saCache.Hits}";
            _lblSaMisses.Text = $"Misses: {_saCache.Misses}";
            _lblSaHitRate.Text = $"Hit rate: {_saCache.HitRate:P1}";
        }

        private Label MakeStat(string text, int left) => new Label
        {
            Text = text,
            AutoSize = true,
            Top = 16,
            Left = left,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };

        private Label MakeStatRow2(string text, int left) => new Label
        {
            Text = text,
            AutoSize = true,
            Top = 42,
            Left = left,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x55, 0x55, 0x55)
        };

        private void ApplyWpConfig()
        {
            try
            {
                var policy = _cmbWritePolicy.SelectedIndex == 1 ? WritePolicy.WriteBack : WritePolicy.WriteThrough;
                var missPolicy = _cmbWriteMissPolicy.SelectedIndex == 1 ? WriteMissPolicy.NoWriteAllocate : WriteMissPolicy.WriteAllocate;
                ReplacementPolicy replacementPolicy;
                if (_cmbWpReplacement.SelectedIndex == 1) replacementPolicy = ReplacementPolicy.LRU;
                else if (_cmbWpReplacement.SelectedIndex == 2) replacementPolicy = ReplacementPolicy.LRUApprox;
                else replacementPolicy = ReplacementPolicy.Random;
                _wpCache.Configure((int)_numWpSets.Value, (int)_numWpWays.Value, (int)_numWpBlockSize.Value, policy, missPolicy, replacementPolicy);
                RefreshWpUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Invalid Parameters", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void DoWpAccess()
        {
            if (TryParseAddress(_txtWpAddress.Text, out uint addr))
            {
                var op = _rbWpWrite.Checked ? MemoryOperation.Write : MemoryOperation.Read;
                _wpCache.Access(addr, op);
                RefreshWpUI();
            }
            else
            {
                MessageBox.Show("Invalid address. Use decimal (256) or hex (0x100).",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RunWpSequence(string raw)
        {
            var tokens = raw.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var t in tokens)
            {
                var token = t.Trim();
                var op = MemoryOperation.Read;
                if (token.StartsWith("W", StringComparison.OrdinalIgnoreCase))
                {
                    op = MemoryOperation.Write;
                    token = token.Substring(1);
                }
                else if (token.StartsWith("R", StringComparison.OrdinalIgnoreCase) &&
                         !token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    token = token.Substring(1);
                }

                if (TryParseAddress(token, out uint addr))
                    _wpCache.Access(addr, op);
            }
            RefreshWpUI();
        }

        private void RefreshWpUI()
        {
            _dgvWpLog.Rows.Clear();
            foreach (var a in _wpCache.AccessLog)
            {
                string memAction = "";
                if (a.WroteBackVictim) memAction = "Write-back victim + load";
                else if (a.LoadedFromMemory && a.WroteToMemory) memAction = "Load + write-through";
                else if (a.LoadedFromMemory) memAction = "Load from memory";
                else if (a.WroteToMemory && !a.IsHit) memAction = "Write to memory (no-allocate)";
                else if (a.WroteToMemory) memAction = "Write-through to memory";
                else if (a.MarkedDirty) memAction = "Marked dirty (cache only)";

                int rowIdx = _dgvWpLog.Rows.Add(
                    a.Index,
                    a.Operation == MemoryOperation.Write ? "W" : "R",
                    $"0x{a.Address:X4}",
                    $"0x{a.Tag:X}",
                    a.SetIndex,
                    a.WayUsed >= 0 ? a.WayUsed.ToString() : "-",
                    a.IsHit ? "HIT" : (a.WasEviction ? "MISS (evict)" : "MISS"),
                    memAction);
                _dgvWpLog.Rows[rowIdx].Cells["Result"].Style.BackColor =
                    a.IsHit ? Color.FromArgb(0xC8, 0xE6, 0xC9) : Color.FromArgb(0xFF, 0xCD, 0xD2);
            }
            if (_dgvWpLog.Rows.Count > 0)
                _dgvWpLog.FirstDisplayedScrollingRowIndex = _dgvWpLog.Rows.Count - 1;

            _dgvWpContents.Rows.Clear();
            for (int s = 0; s < _wpCache.NumSets; s++)
            {
                var set = _wpCache.GetSet(s);
                for (int w = 0; w < set.Associativity; w++)
                {
                    var line = set.Ways[w];
                    int rowIdx = _dgvWpContents.Rows.Add(
                        s, w,
                        line.Valid ? "1" : "0",
                        line.Dirty ? "1" : "0",
                        line.Valid ? $"0x{line.Tag:X}" : "-");
                    if (line.Valid && line.Dirty)
                        _dgvWpContents.Rows[rowIdx].Cells["Dirty"].Style.BackColor = Color.FromArgb(0xFF, 0xE0, 0xB2);
                }
            }

            _lblWpTotal.Text = $"Accesses: {_wpCache.TotalAccesses}";
            _lblWpReads.Text = $"Reads: {_wpCache.Reads}";
            _lblWpWrites.Text = $"Writes: {_wpCache.Writes}";
            _lblWpHits.Text = $"Hits: {_wpCache.Hits}";
            _lblWpMisses.Text = $"Misses: {_wpCache.Misses}";
            _lblWpHitRate.Text = $"Hit rate: {_wpCache.HitRate:P1}";
            _lblWpMemReads.Text = $"Mem reads: {_wpCache.MemoryReads}";
            _lblWpMemWrites.Text = $"Mem writes: {_wpCache.MemoryWrites}";
            _lblWpWriteBacks.Text = $"Write-backs: {_wpCache.WriteBacks}";
        }

        private void DoAccess()
        {
            if (TryParseAddress(_txtAddress.Text, out uint addr))
            {
                _cache.Access(addr);
                RefreshUI();
            }
            else
            {
                MessageBox.Show("Invalid address. Use decimal (256) or hex (0x100).",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RunSequence(string raw)
        {
            var tokens = raw.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var t in tokens)
                if (TryParseAddress(t, out uint addr))
                    _cache.Access(addr);
            RefreshUI();
        }

        private bool TryParseAddress(string text, out uint address)
        {
            text = text.Trim();
            try
            {
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    address = Convert.ToUInt32(text.Substring(2), 16);
                else
                    address = Convert.ToUInt32(text, 10);
                return true;
            }
            catch { address = 0; return false; }
        }

        private void RefreshUI()
        {
            _dgvLog.Rows.Clear();
            foreach (var a in _cache.AccessLog)
            {
                int rowIdx = _dgvLog.Rows.Add(a.Index, $"0x{a.Address:X4}", a.IsHit ? "HIT" : "MISS");
                _dgvLog.Rows[rowIdx].Cells["Result"].Style.BackColor =
                    a.IsHit ? Color.FromArgb(0xC8, 0xE6, 0xC9)
                            : Color.FromArgb(0xFF, 0xCD, 0xD2);
            }
            if (_dgvLog.Rows.Count > 0)
                _dgvLog.FirstDisplayedScrollingRowIndex = _dgvLog.Rows.Count - 1;

            _lblTotal.Text = $"Accesses: {_cache.TotalAccesses}";
            _lblHits.Text = $"Hits: {_cache.Hits}";
            _lblMisses.Text = $"Misses: {_cache.Misses}";
            _lblHitRate.Text = $"Hit rate: {_cache.HitRate:P1}";
            _lblMissRate.Text = $"Miss rate: {_cache.MissRate:P1}";
        }
    }
}