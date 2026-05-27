using CommonLib;
using Sunny.UI;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using XL.Tool;

namespace SetProduct
{
    public partial class RecordsFrm : Form
    {
        private string _productionDbPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "production.db");
        private XLToolClass _log = new XLToolClass();
        private string currentMode = "summary";
        private string _previousMode = null;
        private DataTable _currentData;
        private SQLiteConnection _sharedConnection;
        private readonly object _dbLock = new object();
        private UIDataGridView dgvRecords;
        private UIPanel pnlTop;
        private UIPanel pnlStats;
        private UIPanel pnlBottom;
        private UITextBox txtSearch;
        private UIComboBox cboTableType;
        private UIComboBox cboShift;
        private UIComboBox cboResult;
        private UIDatePicker dtStart;
        private UIDatePicker dtEnd;
        private UIButton btnSearch;
        private UIButton btnReset;
        private UIButton btnExport;
        private UIButton btnRefresh;
        private UILabel lblTotal;
        private UILabel lblOK;
        private UILabel lblNG;
        private UILabel lblYield;
        private UILabel lblPageInfo;
        private UIButton btnPrev;
        private UIButton btnNext;
        private int _pageSize = 50;
        private int _currentPage = 1;
        private int _totalPages = 1;

        public RecordsFrm()
        {
            InitializeComponent();
        }

        private void RecordsFrm_Load(object sender, EventArgs e)
        {
            InitializeCustomUI();
            InitializeData();
        }

        private void InitializeCustomUI()
        {
            this.BackColor = Color.FromArgb(240, 242, 245);
            this.Font = new Font("微软雅黑", 10F);
            this.Size = new Size(1400, 850);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "生产记录查询系统";

            CreateTopPanel();
            CreateStatsPanel();
            CreateDataGrid();
            CreateBottomPanel();
        }

        private void CreateTopPanel()
        {
            pnlTop = new UIPanel
            {
                Dock = DockStyle.Top,
                Height = 145,
                FillColor = Color.White,
                RectColor = Color.FromArgb(220, 223, 230),
                Radius = 0
            };
            this.Controls.Add(pnlTop);

            int y = 15;
            int x = 20;

            var lblType = new UILabel
            {
                Text = "查看类型：",
                Location = new Point(x, y),
                Size = new Size(80, 30),
                ForeColor = Color.FromArgb(51, 51, 51),
                Font = new Font("微软雅黑", 10F),
                BackColor = Color.Transparent
            };
            pnlTop.Controls.Add(lblType);
            x += 85;

            cboTableType = new UIComboBox
            {
                Location = new Point(x, y),
                Size = new Size(180, 35),
                FillColor = Color.White,
                Font = new Font("微软雅黑", 10F),
                DropDownStyle = UIDropDownStyle.DropDownList
            };
            cboTableType.Items.AddRange(new[] { "汇总报表（主表）", "详细记录（副表）" });
            cboTableType.SelectedIndex = 0;
            cboTableType.SelectedIndexChanged += CboTableType_SelectedIndexChanged;
            pnlTop.Controls.Add(cboTableType);
            x += 200;

            var lblStart = new UILabel
            {
                Text = "开始日期：",
                Location = new Point(x, y),
                Size = new Size(80, 30),
                ForeColor = Color.FromArgb(51, 51, 51),
                Font = new Font("微软雅黑", 10F),
                BackColor = Color.Transparent
            };
            pnlTop.Controls.Add(lblStart);
            x += 85;

            dtStart = new UIDatePicker
            {
                Location = new Point(x, y),
                Size = new Size(180, 35),
                FillColor = Color.White,
                Font = new Font("微软雅黑", 10F),
                Value = DateTime.Now.AddDays(-7)
            };
            pnlTop.Controls.Add(dtStart);
            x += 200;

            var lblEnd = new UILabel
            {
                Text = "结束日期：",
                Location = new Point(x, y),
                Size = new Size(80, 30),
                ForeColor = Color.FromArgb(51, 51, 51),
                Font = new Font("微软雅黑", 10F),
                BackColor = Color.Transparent
            };
            pnlTop.Controls.Add(lblEnd);
            x += 85;

            dtEnd = new UIDatePicker
            {
                Location = new Point(x, y),
                Size = new Size(180, 35),
                FillColor = Color.White,
                Font = new Font("微软雅黑", 10F),
                Value = DateTime.Now
            };
            pnlTop.Controls.Add(dtEnd);

            y = 58;
            x = 20;

            var lblShift = new UILabel
            {
                Text = "班次选择：",
                Location = new Point(x, y),
                Size = new Size(80, 30),
                ForeColor = Color.FromArgb(51, 51, 51),
                Font = new Font("微软雅黑", 10F),
                BackColor = Color.Transparent
            };
            pnlTop.Controls.Add(lblShift);
            x += 85;

            cboShift = new UIComboBox
            {
                Location = new Point(x, y),
                Size = new Size(120, 35),
                FillColor = Color.White,
                Font = new Font("微软雅黑", 10F),
                DropDownStyle = UIDropDownStyle.DropDownList
            };
            cboShift.Items.AddRange(new[] { "全部班次", "早班", "中班", "夜班" });
            cboShift.SelectedIndex = 0;
            pnlTop.Controls.Add(cboShift);
            x += 140;

            var lblResult = new UILabel
            {
                Text = "检测结果：",
                Location = new Point(x, y),
                Size = new Size(80, 30),
                ForeColor = Color.FromArgb(51, 51, 51),
                Font = new Font("微软雅黑", 10F),
                BackColor = Color.Transparent
            };
            pnlTop.Controls.Add(lblResult);
            x += 85;

            cboResult = new UIComboBox
            {
                Location = new Point(x, y),
                Size = new Size(120, 35),
                FillColor = Color.White,
                Font = new Font("微软雅黑", 10F),
                DropDownStyle = UIDropDownStyle.DropDownList
            };
            cboResult.Items.AddRange(new[] { "全部结果", "OK", "NG" });
            cboResult.SelectedIndex = 0;
            pnlTop.Controls.Add(cboResult);
            x += 140;

            var lblSearch = new UILabel
            {
                Text = "关键词：",
                Location = new Point(x, y),
                Size = new Size(80, 30),
                ForeColor = Color.FromArgb(51, 51, 51),
                Font = new Font("微软雅黑", 10F),
                BackColor = Color.Transparent
            };
            pnlTop.Controls.Add(lblSearch);
            x += 85;

            txtSearch = new UITextBox
            {
                Location = new Point(x, y),
                Size = new Size(220, 35),
                FillColor = Color.White,
                Watermark = "请输入SKU或流水号...",
                Font = new Font("微软雅黑", 10F)
            };
            pnlTop.Controls.Add(txtSearch);

            y = 58;
            x = this.Width - 850;
            var formulaLabel = new UILabel
            {
                Text = "💡 良率计算公式：良率 = OK合格数 ÷（总检测数 - 被剔除的连续爆管异常数量）",
                Location = new Point(x, y),
                Size = new Size(550, 35),
                ForeColor = Color.FromArgb(255, 140, 0),
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlTop.Controls.Add(formulaLabel);

            y = 102;
            x = this.Width - 380;

            btnSearch = new UIButton
            {
                Text = "🔍 查询",
                Location = new Point(x, y),
                Size = new Size(110, 38),
                FillColor = Color.FromArgb(24, 144, 255),
                RectColor = Color.FromArgb(24, 144, 255),
                ForeColor = Color.White,
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                Radius = 6
            };
            btnSearch.Click += BtnSearch_Click;
            pnlTop.Controls.Add(btnSearch);
            x += 120;

            btnReset = new UIButton
            {
                Text = "🔄 重置",
                Location = new Point(x, y),
                Size = new Size(110, 38),
                FillColor = Color.FromArgb(144, 147, 153),
                RectColor = Color.FromArgb(144, 147, 153),
                ForeColor = Color.White,
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                Radius = 6
            };
            btnReset.Click += BtnReset_Click;
            pnlTop.Controls.Add(btnReset);
            x += 120;

            btnExport = new UIButton
            {
                Text = "📥 导出",
                Location = new Point(x, y),
                Size = new Size(110, 38),
                FillColor = Color.FromArgb(103, 194, 58),
                RectColor = Color.FromArgb(103, 194, 58),
                ForeColor = Color.White,
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                Radius = 6
            };
            btnExport.Click += BtnExport_Click;
            pnlTop.Controls.Add(btnExport);
			x += 120;

			btnRefresh = new UIButton
			{
			Text = "🔄 刷新",
			Location = new Point(x, y),
			Size = new Size(110, 38),
			FillColor = Color.FromArgb(255, 140, 0),
			RectColor = Color.FromArgb(255, 140, 0),
			ForeColor = Color.White,
			Font = new Font("微软雅黑", 10F, FontStyle.Bold),
			Radius = 6
			};
			btnRefresh.Click += BtnRefresh_Click;
			pnlTop.Controls.Add(btnRefresh);
        }

        private void CreateStatsPanel()
        {
            pnlStats = new UIPanel
            {
                Dock = DockStyle.Top,
                Height = 100,
                FillColor = Color.FromArgb(248, 250, 252),
                RectColor = Color.FromArgb(220, 223, 230),
                Radius = 0
            };
            this.Controls.Add(pnlStats);

            int panelWidth = (this.Width - 100) / 4;
            int y = 20;
            int x = 20;

            CreateStatCard(x, y, panelWidth, "总检数量", "0", Color.FromArgb(0, 122, 255), ref lblTotal);
            x += panelWidth + 10;

            CreateStatCard(x, y, panelWidth, "合格数量", "0", Color.FromArgb(0, 200, 0), ref lblOK);
            x += panelWidth + 10;

            CreateStatCard(x, y, panelWidth, "不合格数量", "0", Color.FromArgb(255, 69, 0), ref lblNG);
            x += panelWidth + 10;

            CreateStatCard(x, y, panelWidth, "良品率", "0.00%", Color.FromArgb(255, 140, 0), ref lblYield);
        }

        private void CreateStatCard(int x, int y, int width, string title, string value, Color color, ref UILabel valueLabel)
        {
            var card = new UIPanel
            {
                Location = new Point(x, y),
                Size = new Size(width, 55),
                FillColor = Color.White,
                RectColor = Color.FromArgb(228, 233, 242),
                Radius = 8
            };
            pnlStats.Controls.Add(card);

            var lblTitle = new UILabel
            {
                Text = title,
                Location = new Point(15, 8),
                Size = new Size(width - 30, 20),
                ForeColor = Color.FromArgb(144, 147, 153),
                Font = new Font("微软雅黑", 9F),
                BackColor = Color.Transparent
            };
            card.Controls.Add(lblTitle);

            valueLabel = new UILabel
            {
                Text = value,
                Location = new Point(15, 26),
                Size = new Size(width - 30, 26),
                ForeColor = color,
                Font = new Font("微软雅黑", 16F, FontStyle.Bold),
                BackColor = Color.Transparent
            };
            card.Controls.Add(valueLabel);
        }

        private void CreateBottomPanel()
        {
            pnlBottom = new UIPanel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                FillColor = Color.White,
                RectColor = Color.FromArgb(220, 223, 230),
                Radius = 0
            };
            this.Controls.Add(pnlBottom);

            int centerX = this.Width / 2;

            btnPrev = new UIButton
            {
                Text = "◀ 上一页",
                Location = new Point(centerX - 220, 12),
                Size = new Size(100, 36),
                FillColor = Color.FromArgb(24, 144, 255),
                RectColor = Color.FromArgb(24, 144, 255),
                ForeColor = Color.White,
                Font = new Font("微软雅黑", 10F),
                Radius = 6
            };
            btnPrev.Click += BtnPrev_Click;
            pnlBottom.Controls.Add(btnPrev);

            lblPageInfo = new UILabel
            {
                Text = "第 1 / 1 页",
                Location = new Point(centerX - 50, 16),
                Size = new Size(100, 28),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("微软雅黑", 11F),
                ForeColor = Color.FromArgb(51, 51, 51),
                BackColor = Color.Transparent
            };
            pnlBottom.Controls.Add(lblPageInfo);

            btnNext = new UIButton
            {
                Text = "下一页 ▶",
                Location = new Point(centerX + 120, 12),
                Size = new Size(100, 36),
                FillColor = Color.FromArgb(24, 144, 255),
                RectColor = Color.FromArgb(24, 144, 255),
                ForeColor = Color.White,
                Font = new Font("微软雅黑", 10F),
                Radius = 6
            };
            btnNext.Click += BtnNext_Click;
            pnlBottom.Controls.Add(btnNext);
        }

        private void CreateDataGrid()
        {
            dgvRecords = new UIDataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                BackgroundColor = Color.FromArgb(248, 250, 252),
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.Single,
                GridColor = Color.FromArgb(228, 233, 242),
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(250, 251, 252),
                    ForeColor = Color.FromArgb(51, 51, 51),
                    SelectionBackColor = Color.FromArgb(205, 232, 255),
                    SelectionForeColor = Color.FromArgb(51, 51, 51),
                    Padding = new Padding(8, 4, 8, 4),
                    Font = new Font("微软雅黑", 10F)
                },
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.White,
                    ForeColor = Color.FromArgb(51, 51, 51),
                    SelectionBackColor = Color.FromArgb(205, 232, 255),
                    SelectionForeColor = Color.FromArgb(51, 51, 51),
                    Padding = new Padding(8, 4, 8, 4),
                    Font = new Font("微软雅黑", 10F)
                },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(248, 250, 252),
                    ForeColor = Color.FromArgb(96, 98, 102),
                    Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    Padding = new Padding(8)
                },
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                RowTemplate = new DataGridViewRow { Height = 40 },
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            this.Controls.Add(dgvRecords);
            dgvRecords.BringToFront();
        }

        private async void InitializeData()
        {
            try
            {
                string checkDetailSql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='production_records_detail'";
                string checkSummarySql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='production_records_summary'";

                var checkDetail = ExecuteProdQuery(checkDetailSql);
                var checkSummary = ExecuteProdQuery(checkSummarySql);

                bool hasDetailTable = checkDetail.Rows.Count > 0 && Convert.ToInt32(checkDetail.Rows[0][0]) > 0;
                bool hasSummaryTable = checkSummary.Rows.Count > 0 && Convert.ToInt32(checkSummary.Rows[0][0]) > 0;

                if (!hasDetailTable || !hasSummaryTable)
                {
                    CreateTables();
                }

			await Task.Delay(500);
			_ = LoadData();
            }
            catch (Exception ex)
            {
                _log.SaveLog($"加载数据异常: {ex.Message}");
                MessageBox.Show($"加载数据异常：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

		private SQLiteConnection GetProductionConnection()
		{
			lock (_dbLock)
			{
				if (_sharedConnection == null)
				{
					_sharedConnection = new SQLiteConnection(
						@"Data Source=" + _productionDbPath + ";Journal Mode=WAL;Cache Size=2000;Synchronous=Normal;wal_autocheckpoint=10000;");
					_sharedConnection.Open();
				}
				else if (_sharedConnection.State != ConnectionState.Open)
					_sharedConnection.Open();
				return _sharedConnection;
			}
		}

		private DataTable ExecuteProdQuery(string sql, params SQLiteParameter[] parameters)
        {
            lock (_dbLock)
            {
                using (var cmd = new SQLiteCommand(sql, GetProductionConnection()))
                {
                    if (parameters != null && parameters.Length > 0)
                        cmd.Parameters.AddRange(parameters);
                    var adapter = new SQLiteDataAdapter(cmd);
                    var dt = new DataTable();
                    adapter.Fill(dt);
                    return dt;
                }
            }
        }

        private bool ExecuteProdNonQuery(string sql, params SQLiteParameter[] parameters)
        {
            try
            {
                lock (_dbLock)
                {
                    using (var cmd = new SQLiteCommand(sql, GetProductionConnection()))
                    {
                        if (parameters != null && parameters.Length > 0)
                            cmd.Parameters.AddRange(parameters);
                        cmd.ExecuteNonQuery();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.SaveLog($"数据库操作异常: {ex.Message}");
                return false;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            lock (_dbLock) { _sharedConnection?.Close(); _sharedConnection?.Dispose(); _sharedConnection = null; }
            _currentData?.Dispose(); _currentData = null;
        }

        private void CreateTables()
        {
            try
            {
                string createDetail = @"
                    CREATE TABLE IF NOT EXISTS production_records_detail (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        p_time DATETIME NOT NULL,
                        p_date TEXT NOT NULL,
                        p_shift TEXT NOT NULL,
                        p_shift_date TEXT NOT NULL,
                        sku TEXT NOT NULL,
                        sequence_id INTEGER,
                        final_result TEXT NOT NULL,
                        cam1_result INTEGER,
                        cam2_result INTEGER,
                        cam3_result INTEGER,
                        cam4_result INTEGER,
                        cam5_result INTEGER,
                        ng_异物 INTEGER DEFAULT 0,
                        ng_管盖有无 INTEGER DEFAULT 0,
                        ng_管口圆度 INTEGER DEFAULT 0,
                        ng_正面工号缺失 INTEGER DEFAULT 0,
                        ng_背面工号缺失 INTEGER DEFAULT 0,
                        ng_PCode INTEGER DEFAULT 0,
                        ng_色标对中 INTEGER DEFAULT 0,
                        ng_爆管 INTEGER DEFAULT 0,
                        ng_斜口 INTEGER DEFAULT 0,
                        ng_未剪断 INTEGER DEFAULT 0,
                        defect_detail TEXT,
                        defect_count INTEGER DEFAULT 0,
                        is_excluded INTEGER DEFAULT 0,
                        excluded_reason TEXT,
                        created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                    )";

                string createSummary = @"
                    CREATE TABLE IF NOT EXISTS production_records_summary (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        p_date TEXT NOT NULL,
                        p_shift TEXT NOT NULL,
                        sku TEXT NOT NULL,
                        total_count INTEGER DEFAULT 0,
                        ok_count INTEGER DEFAULT 0,
                        ng_count INTEGER DEFAULT 0,
                        ng_异物 INTEGER DEFAULT 0,
                        ng_管盖有无 INTEGER DEFAULT 0,
                        ng_管口圆度 INTEGER DEFAULT 0,
                        ng_正面工号缺失 INTEGER DEFAULT 0,
                        ng_背面工号缺失 INTEGER DEFAULT 0,
                        ng_PCode INTEGER DEFAULT 0,
                        ng_色标对中 INTEGER DEFAULT 0,
                        ng_爆管 INTEGER DEFAULT 0,
                        ng_斜口 INTEGER DEFAULT 0,
                        ng_未剪断 INTEGER DEFAULT 0,
                        ng_混合多种缺陷 INTEGER DEFAULT 0,
                        continuous_exclude_count INTEGER DEFAULT 0,
                        yield_rate REAL DEFAULT 0,
                        summary_date TEXT DEFAULT CURRENT_DATE,
                        UNIQUE(p_date, p_shift, sku)
                    )";

                ExecuteProdNonQuery(createDetail);
                ExecuteProdNonQuery(createSummary);
                _log.SaveLog("数据库表创建完成");
            }
            catch (Exception ex)
            {
                _log.SaveLog($"创建表异常: {ex.Message}");
            }
        }

        private void CboTableType_SelectedIndexChanged(object sender, EventArgs e)
        {
            bool isDetail = cboTableType.SelectedIndex == 1;
            currentMode = isDetail ? "detail" : "summary";
            cboResult.Visible = isDetail;
            _ = LoadData();
        }

		private async Task LoadData()
		{
			try
			{
				this.Cursor = Cursors.WaitCursor;

				var parameters = new List<SQLiteParameter>();
                string sql;

                if (currentMode == "summary")
                {
                    sql = "SELECT * FROM production_records_summary WHERE 1=1";

                    DateTime startDate = dtStart.Value.Date;
                    DateTime endDate = dtEnd.Value.Date;

                    sql += " AND p_date >= @startDate AND p_date <= @endDate";
                    parameters.Add(new SQLiteParameter("@startDate", startDate.ToString("yyyy-MM-dd")));
                    parameters.Add(new SQLiteParameter("@endDate", endDate.ToString("yyyy-MM-dd")));

                    if (cboShift.SelectedIndex > 0)
                    {
                        string shiftText = cboShift.SelectedItem.ToString();
                        sql += " AND p_shift = @shift";
                        parameters.Add(new SQLiteParameter("@shift", shiftText.Replace("班次", "")));
                    }

                    if (!string.IsNullOrWhiteSpace(txtSearch.Text))
                    {
                        sql += " AND (sku LIKE @search OR p_date LIKE @search)";
                        parameters.Add(new SQLiteParameter("@search", $"%{txtSearch.Text}%"));
                    }

                    sql += " ORDER BY p_date DESC, p_shift, sku LIMIT 5000";
                }
                else
                {
                    sql = "SELECT * FROM production_records_detail WHERE 1=1";

                    DateTime startDate = dtStart.Value.Date;
                    DateTime endDate = dtEnd.Value.Date.AddDays(1).AddSeconds(-1);

                    sql += " AND p_time >= @startTime AND p_time <= @endTime";
                    parameters.Add(new SQLiteParameter("@startTime", startDate));
                    parameters.Add(new SQLiteParameter("@endTime", endDate));

                    if (cboShift.SelectedIndex > 0)
                    {
                        string shiftText = cboShift.SelectedItem.ToString();
                        sql += " AND p_shift = @shift";
                        parameters.Add(new SQLiteParameter("@shift", shiftText.Replace("班次", "")));
                    }

                    if (!string.IsNullOrWhiteSpace(txtSearch.Text))
                    {
                        sql += " AND (sku LIKE @search OR CAST(sequence_id AS TEXT) LIKE @search OR defect_detail LIKE @search)";
                        parameters.Add(new SQLiteParameter("@search", $"%{txtSearch.Text}%"));
                    }

                    if (cboResult.SelectedIndex > 0)
                    {
                        sql += " AND final_result = @result";
                        parameters.Add(new SQLiteParameter("@result", cboResult.SelectedItem.ToString()));
                    }

						sql += " ORDER BY p_time DESC LIMIT 3000";
					}

					_currentData = await Task.Run(() => ExecuteProdQuery(sql, parameters.ToArray()));

					if (_currentData != null && _currentData.Rows.Count > 0)
					{
						_totalPages = (int)Math.Ceiling((double)_currentData.Rows.Count / _pageSize);
						_currentPage = 1;
						await Task.Run(() => UpdateStatistics());
						await RefreshGridDataAsync();
					}
					else
					{
						dgvRecords.DataSource = null;
						dgvRecords.Columns.Clear();
						UpdateStatistics();
					}
				}
				catch (Exception ex)
				{
					_log.SaveLog($"加载数据异常: {ex.Message}");
					MessageBox.Show($"加载数据异常：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
				finally
				{
					this.Cursor = Cursors.Default;
				}
			}

		private void RefreshGridData()
        {
            if (_currentData == null || _currentData.Rows.Count == 0)
            {
                dgvRecords.DataSource = null;
                dgvRecords.Columns.Clear();
                return;
            }
            if (_previousMode != currentMode)
            {
                _previousMode = currentMode;
                ConfigureGridColumns();
            }
            var oldDS = dgvRecords.DataSource as DataTable;
            var displayData = _currentData.Clone();
            int start = (_currentPage - 1) * _pageSize;
            int end = Math.Min(start + _pageSize, _currentData.Rows.Count);
            for (int i = start; i < end; i++)
                displayData.ImportRow(_currentData.Rows[i]);
            dgvRecords.DataSource = displayData;
            if (oldDS != null && oldDS != displayData) oldDS.Dispose();
            UpdatePageInfo();
        }

        private async Task RefreshGridDataAsync()
        {
            if (_currentData == null || _currentData.Rows.Count == 0)
            {
                dgvRecords.DataSource = null;
                dgvRecords.Columns.Clear();
                return;
            }
            if (_previousMode != currentMode)
            {
                _previousMode = currentMode;
                ConfigureGridColumns();
            }
            int start = (_currentPage - 1) * _pageSize;
            int end = Math.Min(start + _pageSize, _currentData.Rows.Count);
            var displayData = await Task.Run(() =>
            {
                var dt = _currentData.Clone();
                for (int i = start; i < end; i++)
                    dt.ImportRow(_currentData.Rows[i]);
                return dt;
            });
            var oldDS = dgvRecords.DataSource as DataTable;
            dgvRecords.DataSource = displayData;
            if (oldDS != null && oldDS != displayData) oldDS.Dispose();
            UpdatePageInfo();
        }

        private void ConfigureGridColumns()
        {
            dgvRecords.AutoGenerateColumns = false;
            dgvRecords.Columns.Clear();
            dgvRecords.CellFormatting -= DgvRecords_SummaryCellFormatting;
            dgvRecords.CellFormatting -= DgvRecords_DetailCellFormatting;

            if (currentMode == "summary")
            {
                AddSummaryColumns();
                dgvRecords.CellFormatting += DgvRecords_SummaryCellFormatting;
            }
            else
            {
                AddDetailColumns();
                dgvRecords.CellFormatting += DgvRecords_DetailCellFormatting;
            }
        }

        private void AddSummaryColumns()
        {
            var columns = new List<Tuple<string, string, int>>
            {
                Tuple.Create("p_date", "统计日期", 110),
                Tuple.Create("p_shift", "班次", 70),
                Tuple.Create("sku", "SKU", 130),
                Tuple.Create("total_count", "总检数", 80),
                Tuple.Create("ok_count", "OK数", 80),
                Tuple.Create("ng_count", "NG数", 80),
                Tuple.Create("ng_异物", "管内异物", 80),
                Tuple.Create("ng_管盖有无", "管盖有无", 80),
                Tuple.Create("ng_管口圆度", "管口圆度", 80),
                Tuple.Create("ng_正面工号缺失", "正面缺失", 90),
                Tuple.Create("ng_背面工号缺失", "背面缺失", 90),
                Tuple.Create("ng_PCode", "P-Code", 80),
                Tuple.Create("ng_色标对中", "色标对中", 80),
                Tuple.Create("ng_爆管", "爆管", 70),
                Tuple.Create("ng_斜口", "斜口", 70),
                Tuple.Create("ng_未剪断", "未剪断", 80),
                Tuple.Create("ng_混合多种缺陷", "混合缺陷", 90),
                Tuple.Create("continuous_exclude_count", "连续爆管剔除", 100),
                Tuple.Create("yield_rate", "良率%", 80)
            };

            foreach (var col in columns)
            {
                if (_currentData.Columns.Contains(col.Item1))
                {
                    dgvRecords.Columns.Add(new DataGridViewTextBoxColumn
                    {
                        Name = col.Item1,
                        HeaderText = col.Item2,
                        DataPropertyName = col.Item1,
                        Width = col.Item3,
                        ReadOnly = true,
                        DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
                    });
                }
            }
        }

        private void AddDetailColumns()
        {
            var columns = new List<Tuple<string, string, int>>
            {
                Tuple.Create("p_time", "检测时间", 170),
                Tuple.Create("p_date", "检测日期", 110),
                Tuple.Create("p_shift", "班次", 70),
                Tuple.Create("sku", "SKU", 130),
                Tuple.Create("sequence_id", "流水号", 90),
                Tuple.Create("final_result", "结果", 70),
                Tuple.Create("defect_detail", "缺陷详情", 350),
                Tuple.Create("defect_count", "缺陷数", 80)
            };

            foreach (var col in columns)
            {
                if (_currentData.Columns.Contains(col.Item1))
                {
                    dgvRecords.Columns.Add(new DataGridViewTextBoxColumn
                    {
                        Name = col.Item1,
                        HeaderText = col.Item2,
                        DataPropertyName = col.Item1,
                        Width = col.Item3,
                        ReadOnly = true,
                        DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
                    });
                }
            }
        }

        private void DgvRecords_SummaryCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.ColumnIndex >= 0 && e.ColumnIndex < dgvRecords.Columns.Count && dgvRecords.Columns[e.ColumnIndex].Name == "yield_rate" && e.Value != null)
            {
                if (decimal.TryParse(e.Value.ToString(), out decimal yield))
                {
                    e.Value = yield.ToString("F2");
                    e.FormattingApplied = true;
                }
            }
        }

        private void DgvRecords_DetailCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.ColumnIndex >= 0 && e.ColumnIndex < dgvRecords.Columns.Count)
            {
                string colName = dgvRecords.Columns[e.ColumnIndex].Name;

                if (colName == "final_result" && e.Value != null)
                {
                    string result = e.Value.ToString();
                    if (result == "OK")
                    {
                        e.CellStyle.BackColor = Color.FromArgb(230, 250, 230);
                        e.CellStyle.ForeColor = Color.FromArgb(103, 194, 58);
                    }
                    else if (result == "NG")
                    {
                        e.CellStyle.BackColor = Color.FromArgb(253, 242, 242);
                        e.CellStyle.ForeColor = Color.FromArgb(245, 108, 108);
                    }
                }
            }
        }

        private void UpdateStatistics()
        {
            if (_currentData == null || _currentData.Rows.Count == 0)
            {
                lblTotal.Text = "0";
                lblOK.Text = "0";
                lblNG.Text = "0";
                lblYield.Text = "0.00%";
                return;
            }

            int total = 0, ok = 0, ng = 0, excludeCount = 0;

            if (currentMode == "summary")
            {
                foreach (DataRow row in _currentData.Rows)
                {
                    total += Convert.ToInt32(row["total_count"]);
                    ok += Convert.ToInt32(row["ok_count"]);
                    ng += Convert.ToInt32(row["ng_count"]);
                    if (_currentData.Columns.Contains("continuous_exclude_count"))
                    {
                        excludeCount += Convert.ToInt32(row["continuous_exclude_count"]);
                    }
                }
            }
            else
            {
                foreach (DataRow row in _currentData.Rows)
                {
                    total++;
                    if (row["final_result"].ToString() == "OK") ok++;
                    else ng++;
                    if (_currentData.Columns.Contains("is_excluded"))
                    {
                        if (Convert.ToInt32(row["is_excluded"]) == 1)
                        {
                            excludeCount++;
                        }
                    }
                }
            }

            lblTotal.Text = total.ToString("N0");
            lblOK.Text = ok.ToString("N0");
            lblNG.Text = ng.ToString("N0");

            double effectiveCount = total - excludeCount;
            double yield = effectiveCount > 0 ? (double)ok / effectiveCount * 100 : 0;
            lblYield.Text = yield.ToString("F2") + "%";
        }

        private void UpdatePageInfo()
        {
            lblPageInfo.Text = $"第 {_currentPage} / {_totalPages} 页";
            btnPrev.Enabled = _currentPage > 1;
            btnNext.Enabled = _currentPage < _totalPages;
        }

        private void BtnSearch_Click(object sender, EventArgs e)
        {
            _ = LoadData();
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            dtStart.Value = DateTime.Now.AddDays(-7);
            dtEnd.Value = DateTime.Now;
            cboShift.SelectedIndex = 0;
            cboResult.SelectedIndex = 0;
            txtSearch.Text = "";
            _ = LoadData();
        }

        private void BtnPrev_Click(object sender, EventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                RefreshGridData();
            }
        }

        private void BtnNext_Click(object sender, EventArgs e)
        {
            if (_currentPage < _totalPages)
            {
                _currentPage++;
                RefreshGridData();
            }
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            if (_currentData == null || _currentData.Rows.Count == 0)
            {
                MessageBox.Show("没有数据可导出！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "CSV文件|*.csv",
                FileName = $"生产记录_{currentMode}_{DateTime.Now:yyyyMMddHHmmss}",
                DefaultExt = ".csv"
            };
            if (sfd.ShowDialog() != DialogResult.OK) return;

            try
            {
                var headers = GetExportHeaders();
                using (StreamWriter sw = new StreamWriter(sfd.FileName, false, System.Text.Encoding.UTF8))
                {
                    sw.WriteLine(string.Join(",", headers.Select(h => h.Item2)));

                    foreach (DataRow row in _currentData.Rows)
                    {
                        List<string> values = new List<string>();
                        foreach (var header in headers)
                        {
                            string val = row[header.Item1]?.ToString() ?? "";

                            if (val.Contains(",") || val.Contains("\"") || val.Contains("\n"))
                            {
                                val = "\"" + val.Replace("\"", "\"\"") + "\"";
                            }
                            values.Add(val);
                        }
                        sw.WriteLine(string.Join(",", values));
                    }
                }
                MessageBox.Show("导出成功！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnRefresh_Click(object sender, EventArgs e)
        {
            _ = LoadData();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
            else
            {
                base.OnFormClosing(e);
            }
        }

        private List<Tuple<string, string>> GetExportHeaders()
        {
            if (currentMode == "summary")
            {
                return new List<Tuple<string, string>>
                {
                    Tuple.Create("p_date", "统计日期"),
                    Tuple.Create("p_shift", "班次"),
                    Tuple.Create("sku", "SKU"),
                    Tuple.Create("total_count", "总检数"),
                    Tuple.Create("ok_count", "OK数"),
                    Tuple.Create("ng_count", "NG数"),
                    Tuple.Create("ng_异物", "管内异物"),
                    Tuple.Create("ng_管盖有无", "管盖有无"),
                    Tuple.Create("ng_管口圆度", "管口圆度"),
                    Tuple.Create("ng_正面工号缺失", "正面工号缺失"),
                    Tuple.Create("ng_背面工号缺失", "背面工号缺失"),
                    Tuple.Create("ng_PCode", "P-Code"),
                    Tuple.Create("ng_色标对中", "色标对中"),
                    Tuple.Create("ng_爆管", "爆管"),
                    Tuple.Create("ng_斜口", "斜口"),
                    Tuple.Create("ng_未剪断", "未剪断"),
                    Tuple.Create("ng_混合多种缺陷", "混合缺陷"),
                    Tuple.Create("continuous_exclude_count", "连续爆管剔除"),
                    Tuple.Create("yield_rate", "良率%")
                };
            }
            else
            {
                return new List<Tuple<string, string>>
                {
                    Tuple.Create("p_time", "检测时间"),
                    Tuple.Create("p_date", "检测日期"),
                    Tuple.Create("p_shift", "班次"),
                    Tuple.Create("sku", "SKU"),
                    Tuple.Create("sequence_id", "流水号"),
                    Tuple.Create("final_result", "结果"),
                    Tuple.Create("defect_detail", "缺陷详情"),
                    Tuple.Create("defect_count", "缺陷数")
                };
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (btnPrev != null && lblPageInfo != null && btnNext != null)
            {
                int centerX = this.Width / 2;
                btnPrev.Location = new Point(centerX - 220, 12);
                lblPageInfo.Location = new Point(centerX - 50, 16);
                btnNext.Location = new Point(centerX + 120, 12);
            }
        }
    }
}
