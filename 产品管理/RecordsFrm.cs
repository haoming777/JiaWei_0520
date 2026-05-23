using CommonLib;
using Sunny.UI;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using XL.Tool;

namespace SetProduct
{
	public partial class RecordsFrm : Form
	{
		public RecordsFrm()
		{
			InitializeComponent();
			ReplaceDataGridView();
			CreatePagination();
		}

		private string _productionDbPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "production.db");
		private XLToolClass _log = new XLToolClass();

		private SQLiteConnection GetProductionConnection()
		{
			return new SQLiteConnection(@"Data Source = " + _productionDbPath);
		}

		private DataTable ExecuteProdQuery(string sql, params SQLiteParameter[] parameters)
		{
			using (var conn = GetProductionConnection())
			{
				using (var cmd = new SQLiteCommand(sql, conn))
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
				using (var conn = GetProductionConnection())
				{
					conn.Open();
					using (var cmd = new SQLiteCommand(sql, conn))
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

		private string currentMode = "summary";
		private DataTable _currentData;

		// 自定义表格控件
		private CustomDataGridView customGrid;

		// 缺陷筛选控件
		private FlowLayoutPanel flpDefect;
		private List<UICheckBox> _defectChecks = new List<UICheckBox>();
		private UIPanel pnlDefectFilter;
		private UIButton btnDefectAll, btnDefectClear;

		// 分页相关
		private int _pageSize = 100;
		private int _currentPage = 1;
		private int _totalPages = 1;
		private UIPanel pnlPage;
		private UILabel lblPageInfo;
		private UIButton btnPrev, btnNext;

		private void ReplaceDataGridView()
		{
			if (this.dataGridView1 != null)
			{
				this.Controls.Remove(this.dataGridView1);
				this.dataGridView1.Dispose();
			}

			customGrid = new CustomDataGridView
			{
				Location = new Point(0, 50),
				Size = new Size(this.ClientSize.Width, this.ClientSize.Height - 140),
				Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
				BackColor = Color.White,
				Visible = true
			};
			this.Controls.Add(customGrid);
			customGrid.BringToFront();
		}

		private void CreatePagination()
		{
			pnlPage = new UIPanel
			{
				Dock = DockStyle.Bottom,
				Height = 40,
				FillColor = Color.White,
				RectColor = Color.FromArgb(230, 235, 240)
			};

			btnPrev = new UIButton
			{
				Text = "上一页",
				Location = new Point(pnlPage.Width - 260, 6),
				Size = new Size(80, 28),
				FillColor = Color.FromArgb(66, 133, 244),
				RectColor = Color.FromArgb(66, 133, 244),
				ForeColor = Color.White,
				Font = new Font("微软雅黑", 9, FontStyle.Bold),
				Radius = 4
			};
			btnPrev.Click += (s, ev) => { if (_currentPage > 1) { _currentPage--; RefreshGridData(); } };

			lblPageInfo = new UILabel
			{
				Text = "第 1 / 1 页",
				Location = new Point(pnlPage.Width - 170, 10),
				AutoSize = true,
				ForeColor = Color.FromArgb(80, 80, 80),
				Font = new Font("微软雅黑", 10)
			};

			btnNext = new UIButton
			{
				Text = "下一页",
				Location = new Point(pnlPage.Width - 85, 6),
				Size = new Size(80, 28),
				FillColor = Color.FromArgb(66, 133, 244),
				RectColor = Color.FromArgb(66, 133, 244),
				ForeColor = Color.White,
				Font = new Font("微软雅黑", 9, FontStyle.Bold),
				Radius = 4
			};
			btnNext.Click += (s, ev) => { if (_currentPage < _totalPages) { _currentPage++; RefreshGridData(); } };

			pnlPage.Controls.Add(btnPrev);
			pnlPage.Controls.Add(lblPageInfo);
			pnlPage.Controls.Add(btnNext);
			this.Controls.Add(pnlPage);
			pnlPage.BringToFront();
		}

		private void RefreshGridData()
		{
			if (_currentData == null || customGrid == null) return;

			int start = (_currentPage - 1) * _pageSize;
			int end = Math.Min(start + _pageSize, _currentData.Rows.Count);
			var pageRows = new List<DataRow>();
			for (int i = start; i < end; i++)
			{
				pageRows.Add(_currentData.Rows[i]);
			}

			// 获取中文列名和列宽
			var columnHeaders = GetColumnHeaders();
			customGrid.SetData(pageRows, _currentData.Columns, columnHeaders);
			lblPageInfo.Text = $"第 {_currentPage} / {_totalPages} 页（共 {_currentData.Rows.Count} 条）";
		}

		private List<ColumnHeaderInfo> GetColumnHeaders()
		{
			var headers = new List<ColumnHeaderInfo>();

			if (currentMode == "summary")
			{
				headers.Add(new ColumnHeaderInfo("p_date", "日期", 100));
				headers.Add(new ColumnHeaderInfo("p_shift", "班次", 60));
				headers.Add(new ColumnHeaderInfo("sku", "SKU", 80));
				headers.Add(new ColumnHeaderInfo("total_count", "总检数", 70));
				headers.Add(new ColumnHeaderInfo("ok_count", "OK数", 70));
				headers.Add(new ColumnHeaderInfo("ng_count", "NG数", 70));
				headers.Add(new ColumnHeaderInfo("ng_异物", "管内异物", 80));
				headers.Add(new ColumnHeaderInfo("ng_管盖有无", "管盖有无", 80));
				headers.Add(new ColumnHeaderInfo("ng_管口圆度", "管口圆度", 80));
				headers.Add(new ColumnHeaderInfo("ng_正面工号不齐", "正面工号不齐", 80));
				headers.Add(new ColumnHeaderInfo("ng_背面工号不齐", "背面工号不齐", 80));
				headers.Add(new ColumnHeaderInfo("ng_PCode", "P-Code", 75));
				headers.Add(new ColumnHeaderInfo("ng_色标对中", "色标对中", 80));
				headers.Add(new ColumnHeaderInfo("ng_爆管", "爆管", 70));
				headers.Add(new ColumnHeaderInfo("ng_斜口", "斜口", 70));
				headers.Add(new ColumnHeaderInfo("ng_未剪断", "未剪断", 70));
				headers.Add(new ColumnHeaderInfo("ng_混合多种缺陷", "混合多种缺陷", 90));
				headers.Add(new ColumnHeaderInfo("continuous_exclude_count", "连续爆管剔除", 90));
				headers.Add(new ColumnHeaderInfo("yield_rate", "良率", 75));
			}
			else
			{
				headers.Add(new ColumnHeaderInfo("p_time", "检测时间", 150));
				headers.Add(new ColumnHeaderInfo("p_shift", "班次", 60));
				headers.Add(new ColumnHeaderInfo("sku", "SKU", 80));
				headers.Add(new ColumnHeaderInfo("sequence_id", "流水号", 100));
				headers.Add(new ColumnHeaderInfo("final_result", "检测结果", 80));
				headers.Add(new ColumnHeaderInfo("defect_detail", "缺陷详情", 250));
				headers.Add(new ColumnHeaderInfo("defect_count", "缺陷数", 70));
			}

			return headers;
		}

		private void RecordsFrm_Load(object sender, EventArgs e)
		{
			InitializeUI();
			InitializeData();
		}

		private void InitializeUI()
		{
			if (this.startTime != null) this.startTime.Value = DateTime.Now.AddDays(-7);
			if (this.endTime != null) this.endTime.Value = DateTime.Now;

			if (this.uiComboBox2 != null)
			{
				this.uiComboBox2.Items.Clear();
				this.uiComboBox2.Items.Add("全部");
				this.uiComboBox2.Items.Add("OK");
				this.uiComboBox2.Items.Add("NG");
				this.uiComboBox2.SelectedIndex = 0;
			}

			if (this.uiComboBox3 != null)
			{
				this.uiComboBox3.Items.Clear();
				this.uiComboBox3.Items.Add("汇总记录（主表）");
				this.uiComboBox3.Items.Add("明细记录（副表）");
				this.uiComboBox3.SelectedIndex = 0;
				this.uiComboBox3.SelectedIndexChanged += UiComboBox3_SelectedIndexChanged;
			}

			if (this.uiTextBox1 != null) this.uiTextBox1.Watermark = "请输入流水号或SKU...";

			CreateDefectFilterPanel();
		}

		private void CreateDefectFilterPanel()
		{
			this.pnlDefectFilter = new UIPanel
			{
				Location = new Point(12, 165),
				Size = new Size(this.ClientSize.Width - 30, 50),
				FillColor = Color.FromArgb(248, 249, 250),
				RectColor = Color.FromArgb(230, 235, 240),
				Radius = 5,
				Visible = false
			};

			var lblTitle = new UILabel
			{
				Text = "缺陷筛选：",
				Location = new Point(10, 14),
				AutoSize = true,
				Font = new Font("微软雅黑", 9, FontStyle.Bold),
				ForeColor = Color.FromArgb(66, 133, 244)
			};
			this.pnlDefectFilter.Controls.Add(lblTitle);

			this.flpDefect = new FlowLayoutPanel
			{
				Location = new Point(85, 8),
				Size = new Size(this.pnlDefectFilter.Width - 170, 35),
				AutoScroll = true
			};

			string[] defects = {
				"管内异物", "管盖有无", "管口圆度", "正面工号不齐",
				"背面工号不齐", "P-Code", "色标对中", "爆管",
				"斜口", "未剪断", "混合多种缺陷"
			};

			for (int i = 0; i < defects.Length; i++)
			{
				var chk = new UICheckBox
				{
					Text = defects[i],
					Size = new Size(85, 24),
					Checked = true,
					Font = new Font("微软雅黑", 8),
					ForeColor = Color.FromArgb(60, 60, 60)
				};
				this.flpDefect.Controls.Add(chk);
				this._defectChecks.Add(chk);
			}
			this.pnlDefectFilter.Controls.Add(this.flpDefect);

			this.btnDefectAll = new UIButton
			{
				Text = "全选",
				Location = new Point(this.pnlDefectFilter.Width - 105, 12),
				Size = new Size(48, 26),
				FillColor = Color.FromArgb(66, 133, 244),
				RectColor = Color.FromArgb(66, 133, 244),
				ForeColor = Color.White,
				Font = new Font("微软雅黑", 8, FontStyle.Bold),
				Radius = 4
			};
			this.btnDefectAll.Click += (s, ev) => { this._defectChecks.ForEach(c => c.Checked = true); if (this.currentMode == "detail") this.LoadData(); };
			this.pnlDefectFilter.Controls.Add(this.btnDefectAll);

			this.btnDefectClear = new UIButton
			{
				Text = "清空",
				Location = new Point(this.pnlDefectFilter.Width - 52, 12),
				Size = new Size(48, 26),
				FillColor = Color.FromArgb(108, 117, 125),
				RectColor = Color.FromArgb(108, 117, 125),
				ForeColor = Color.White,
				Font = new Font("微软雅黑", 8, FontStyle.Bold),
				Radius = 4
			};
			this.btnDefectClear.Click += (s, ev) => { this._defectChecks.ForEach(c => c.Checked = false); if (this.currentMode == "detail") this.LoadData(); };
			this.pnlDefectFilter.Controls.Add(this.btnDefectClear);

			this.Controls.Add(this.pnlDefectFilter);
			this.pnlDefectFilter.BringToFront();
		}

		private async void InitializeData()
		{
			this.Cursor = Cursors.WaitCursor;
			await Task.Run(() => LoadDatabaseDataAsync());
			this.Cursor = Cursors.Default;
		}

		private void LoadDatabaseDataAsync()
		{
			try
			{
				var check = this.ExecuteProdQuery("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='production_records_detail'");
				bool hasTable = check.Rows.Count > 0 && Convert.ToInt32(check.Rows[0][0]) > 0;

				if (!hasTable)
				{
					CreateTables();
					GenerateMockData();
				}

				this.BeginInvoke(new Action(() => LoadData()));
			}
			catch (Exception ex)
			{
				this._log.SaveLog($"加载数据异常: {ex.Message}");
			}
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
						ng_正面工号不齐 INTEGER DEFAULT 0,
						ng_背面工号不齐 INTEGER DEFAULT 0,
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
						ng_正面工号不齐 INTEGER DEFAULT 0,
						ng_背面工号不齐 INTEGER DEFAULT 0,
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

				this.ExecuteProdNonQuery(createDetail);
				this.ExecuteProdNonQuery(createSummary);
			}
			catch (Exception ex)
			{
				this._log.SaveLog($"创建表异常: {ex.Message}");
			}
		}

		private void GenerateMockData()
		{
			try
			{
				Random rand = new Random();
				string[] skus = { "SKU-A", "SKU-B", "SKU-C", "SKU-D" };
				string[] shifts = { "早班", "中班", "夜班" };
				string[] defectTypes = { "管内异物", "管盖有无", "管口圆度", "正面工号不齐",
										  "背面工号不齐", "P-Code", "色标对中", "爆管",
										  "斜口", "未剪断" };

				int consecutiveBurst = 0;
				long lastSeq = 0;

				for (int i = 0; i < 150; i++)
				{
					DateTime dt = DateTime.Now.AddDays(-rand.Next(15)).AddHours(rand.Next(24));
					string shift = GetShiftByHour(dt.Hour);
					string sku = skus[rand.Next(skus.Length)];

					bool isBurst = rand.Next(100) < 8;
					bool isExcluded = false;

					if (isBurst)
					{
						if (lastSeq == i - 1) consecutiveBurst++;
						else consecutiveBurst = 1;
						if (consecutiveBurst >= 3) isExcluded = true;
						lastSeq = i;
					}
					else
					{
						consecutiveBurst = 0;
					}

					bool isOk = rand.Next(100) < 85;
					if (isBurst) isOk = false;
					string result = isOk ? "OK" : "NG";

					string defectDetail = "";
					int defectCount = 0;
					int ng1 = 0, ng2 = 0, ng3 = 0, ng4 = 0, ng5 = 0, ng6 = 0, ng7 = 0, ng8 = 0, ng9 = 0, ng10 = 0;

					if (!isOk && !isBurst)
					{
						defectCount = rand.Next(1, 3);
						var selected = new List<string>();
						for (int j = 0; j < defectCount; j++)
							selected.Add(defectTypes[rand.Next(defectTypes.Length)]);
						selected = selected.Distinct().ToList();
						defectCount = selected.Count;
						defectDetail = string.Join("、", selected);

						foreach (var d in selected)
						{
							if (d == "管内异物") ng1 = 1;
							else if (d == "管盖有无") ng2 = 1;
							else if (d == "管口圆度") ng3 = 1;
							else if (d == "正面工号不齐") ng4 = 1;
							else if (d == "背面工号不齐") ng5 = 1;
							else if (d == "P-Code") ng6 = 1;
							else if (d == "色标对中") ng7 = 1;
							else if (d == "爆管") ng8 = 1;
							else if (d == "斜口") ng9 = 1;
							else if (d == "未剪断") ng10 = 1;
						}
					}
					else if (isBurst)
					{
						defectDetail = "爆管";
						defectCount = 1;
						ng8 = 1;
					}

					string shiftDate = shift == "夜班" ? dt.AddDays(-1).ToString("yyyy-MM-dd") : dt.ToString("yyyy-MM-dd");

					string insertDetail = @"
						INSERT INTO production_records_detail 
						(p_time, p_date, p_shift, p_shift_date, sku, sequence_id, final_result,
						 ng_异物, ng_管盖有无, ng_管口圆度, ng_正面工号不齐, ng_背面工号不齐,
						 ng_PCode, ng_色标对中, ng_爆管, ng_斜口, ng_未剪断,
						 defect_detail, defect_count, is_excluded)
						VALUES 
						(@time, @date, @shift, @shiftDate, @sku, @seq, @result,
						 @ng1, @ng2, @ng3, @ng4, @ng5, @ng6, @ng7, @ng8, @ng9, @ng10,
						 @detail, @count, @excluded)";

					this.ExecuteProdNonQuery(insertDetail,
						new SQLiteParameter("@time", dt),
						new SQLiteParameter("@date", dt.ToString("yyyy-MM-dd")),
						new SQLiteParameter("@shift", shift),
						new SQLiteParameter("@shiftDate", shiftDate),
						new SQLiteParameter("@sku", sku),
						new SQLiteParameter("@seq", i + 1),
						new SQLiteParameter("@result", result),
						new SQLiteParameter("@ng1", ng1), new SQLiteParameter("@ng2", ng2),
						new SQLiteParameter("@ng3", ng3), new SQLiteParameter("@ng4", ng4),
						new SQLiteParameter("@ng5", ng5), new SQLiteParameter("@ng6", ng6),
						new SQLiteParameter("@ng7", ng7), new SQLiteParameter("@ng8", ng8),
						new SQLiteParameter("@ng9", ng9), new SQLiteParameter("@ng10", ng10),
						new SQLiteParameter("@detail", defectDetail),
						new SQLiteParameter("@count", defectCount),
						new SQLiteParameter("@excluded", isExcluded ? 1 : 0));
				}

				string summarySql = @"
					INSERT OR REPLACE INTO production_records_summary
					SELECT 
						p_shift_date as p_date, p_shift, sku,
						COUNT(*) as total_count,
						SUM(CASE WHEN final_result='OK' AND is_excluded=0 THEN 1 ELSE 0 END) as ok_count,
						SUM(CASE WHEN final_result='NG' AND is_excluded=0 THEN 1 ELSE 0 END) as ng_count,
						SUM(CASE WHEN defect_count=1 AND ng_异物=1 AND is_excluded=0 THEN 1 ELSE 0 END) as ng_异物,
						SUM(CASE WHEN defect_count=1 AND ng_管盖有无=1 AND is_excluded=0 THEN 1 ELSE 0 END) as ng_管盖有无,
						SUM(CASE WHEN defect_count=1 AND ng_管口圆度=1 AND is_excluded=0 THEN 1 ELSE 0 END) as ng_管口圆度,
						SUM(CASE WHEN defect_count=1 AND ng_正面工号不齐=1 AND is_excluded=0 THEN 1 ELSE 0 END) as ng_正面工号不齐,
						SUM(CASE WHEN defect_count=1 AND ng_背面工号不齐=1 AND is_excluded=0 THEN 1 ELSE 0 END) as ng_背面工号不齐,
						SUM(CASE WHEN defect_count=1 AND ng_PCode=1 AND is_excluded=0 THEN 1 ELSE 0 END) as ng_PCode,
						SUM(CASE WHEN defect_count=1 AND ng_色标对中=1 AND is_excluded=0 THEN 1 ELSE 0 END) as ng_色标对中,
						SUM(CASE WHEN defect_count=1 AND ng_爆管=1 AND is_excluded=0 THEN 1 ELSE 0 END) as ng_爆管,
						SUM(CASE WHEN defect_count=1 AND ng_斜口=1 AND is_excluded=0 THEN 1 ELSE 0 END) as ng_斜口,
						SUM(CASE WHEN defect_count=1 AND ng_未剪断=1 AND is_excluded=0 THEN 1 ELSE 0 END) as ng_未剪断,
						SUM(CASE WHEN defect_count>=2 AND is_excluded=0 THEN 1 ELSE 0 END) as ng_混合多种缺陷,
						SUM(CASE WHEN is_excluded=1 THEN 1 ELSE 0 END) as continuous_exclude_count,
						ROUND(CAST(SUM(CASE WHEN final_result='OK' AND is_excluded=0 THEN 1 ELSE 0 END) AS REAL) / 
							NULLIF(SUM(CASE WHEN is_excluded=0 THEN 1 ELSE 0 END), 0) * 100, 2) as yield_rate
					FROM production_records_detail
					GROUP BY p_shift_date, p_shift, sku";

				this.ExecuteProdNonQuery(summarySql);
				this._log.SaveLog("模拟数据生成成功");
			}
			catch (Exception ex)
			{
				this._log.SaveLog($"模拟数据异常: {ex.Message}");
			}
		}

		private string GetShiftByHour(int hour)
		{
			if (hour >= 8 && hour <= 15) return "早班";
			if (hour >= 16 && hour <= 23) return "中班";
			return "夜班";
		}

		private void UiComboBox3_SelectedIndexChanged(object sender, EventArgs e)
		{
			bool isDetail = this.uiComboBox3 != null && this.uiComboBox3.SelectedIndex == 1;
			this.currentMode = isDetail ? "detail" : "summary";

			if (this.pnlDefectFilter != null) this.pnlDefectFilter.Visible = isDetail;

			if (customGrid != null)
			{
				if (isDetail)
				{
					customGrid.Location = new Point(0, 215);
					customGrid.Height = this.ClientSize.Height - 260;
				}
				else
				{
					customGrid.Location = new Point(0, 50);
					customGrid.Height = this.ClientSize.Height - 95;
				}
				// 刷新表格
				RefreshGridData();
			}

			LoadData();
		}

		private void LoadData()
		{
			try
			{
				string sql = (this.currentMode == "summary")
					? "SELECT * FROM production_records_summary ORDER BY p_date DESC, p_shift, sku"
					: "SELECT * FROM production_records_detail ORDER BY p_time DESC";

				_currentData = this.ExecuteProdQuery(sql);

				if (_currentData != null && _currentData.Rows.Count > 0)
				{
					_totalPages = (int)Math.Ceiling((double)_currentData.Rows.Count / _pageSize);
					_currentPage = 1;
					RefreshGridData();
					UpdateStatistics();
				}
				else
				{
					if (customGrid != null) customGrid.SetData(new List<DataRow>(), null, null);
					UpdateStatistics();
				}
			}
			catch (Exception ex)
			{
				this._log.SaveLog($"加载数据异常: {ex.Message}");
				if (customGrid != null) customGrid.SetData(new List<DataRow>(), null, null);
			}
		}

		private void UpdateStatistics()
		{
			if (_currentData == null || _currentData.Rows.Count == 0)
			{
				if (this.totalTxt != null) this.totalTxt.Text = "0";
				if (this.okTxt != null) this.okTxt.Text = "0";
				if (this.ngTxt != null) this.ngTxt.Text = "0";
				if (this.yieldTxt != null) this.yieldTxt.Text = "0.00%";
				return;
			}

			int total = _currentData.Rows.Count;
			int ok = 0, ng = 0;

			if (this.currentMode == "summary")
			{
				foreach (DataRow row in _currentData.Rows)
				{
					ok += Convert.ToInt32(row["ok_count"]);
					ng += Convert.ToInt32(row["ng_count"]);
				}
				total = ok + ng;
			}
			else
			{
				foreach (DataRow row in _currentData.Rows)
				{
					if (row["final_result"].ToString() == "OK") ok++;
					else ng++;
				}
			}

			if (this.totalTxt != null) this.totalTxt.Text = total.ToString();
			if (this.okTxt != null) this.okTxt.Text = ok.ToString();
			if (this.ngTxt != null) this.ngTxt.Text = ng.ToString();
			double yield = total > 0 ? (double)ok / total * 100 : 0;
			if (this.yieldTxt != null) this.yieldTxt.Text = yield.ToString("F2") + "%";
		}

		private void findBtn_Click(object sender, EventArgs e)
		{
			try
			{
				string startDate = this.startTime?.Value.ToString("yyyy-MM-dd") ?? DateTime.Now.AddDays(-7).ToString("yyyy-MM-dd");
				string endDate = this.endTime?.Value.ToString("yyyy-MM-dd") ?? DateTime.Now.ToString("yyyy-MM-dd");
				string resultFilter = this.uiComboBox2?.SelectedItem?.ToString();
				string searchText = this.uiTextBox1?.Text.Trim() ?? "";

				string sql;
				var parameters = new List<SQLiteParameter>();

				if (this.currentMode == "summary")
				{
					sql = "SELECT * FROM production_records_summary WHERE 1=1";
					sql += " AND p_date >= @start AND p_date <= @end";
					parameters.Add(new SQLiteParameter("@start", startDate));
					parameters.Add(new SQLiteParameter("@end", endDate));
					if (!string.IsNullOrEmpty(searchText))
					{
						sql += " AND (sku LIKE @search)";
						parameters.Add(new SQLiteParameter("@search", $"%{searchText}%"));
					}
					sql += " ORDER BY p_date DESC, p_shift, sku";
				}
				else
				{
					sql = "SELECT * FROM production_records_detail WHERE 1=1";
					sql += " AND p_date >= @start AND p_date <= @end";
					parameters.Add(new SQLiteParameter("@start", startDate));
					parameters.Add(new SQLiteParameter("@end", endDate));
					if (!string.IsNullOrEmpty(searchText))
					{
						sql += " AND (sku LIKE @search OR sequence_id LIKE @search)";
						parameters.Add(new SQLiteParameter("@search", $"%{searchText}%"));
					}
					if (resultFilter != null && resultFilter != "全部")
					{
						sql += " AND final_result = @result";
						parameters.Add(new SQLiteParameter("@result", resultFilter));
					}
					string defectCondition = GetDefectCondition();
					if (!string.IsNullOrEmpty(defectCondition))
					{
						sql += $" AND ({defectCondition})";
					}
					sql += " ORDER BY p_time DESC";
				}

				_currentData = this.ExecuteProdQuery(sql, parameters.ToArray());

				if (_currentData != null)
				{
					_totalPages = (int)Math.Ceiling((double)_currentData.Rows.Count / _pageSize);
					_currentPage = 1;
					RefreshGridData();
					UpdateStatistics();
				}
			}
			catch (Exception ex)
			{
				this._log.SaveLog("查询数据异常: " + ex.Message);
				MessageBox.Show($"查询失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private string GetDefectCondition()
		{
			var map = new Dictionary<string, string>
			{
				{"管内异物", "ng_异物 = 1"}, {"管盖有无", "ng_管盖有无 = 1"},
				{"管口圆度", "ng_管口圆度 = 1"}, {"正面工号不齐", "ng_正面工号不齐 = 1"},
				{"背面工号不齐", "ng_背面工号不齐 = 1"}, {"P-Code", "ng_PCode = 1"},
				{"色标对中", "ng_色标对中 = 1"}, {"爆管", "ng_爆管 = 1"},
				{"斜口", "ng_斜口 = 1"}, {"未剪断", "ng_未剪断 = 1"},
				{"混合多种缺陷", "defect_count >= 2"}
			};
			var selected = this._defectChecks.Where(c => c.Checked && map.ContainsKey(c.Text)).Select(c => map[c.Text]);
			return string.Join(" OR ", selected);
		}

		private void resetBtn_Click(object sender, EventArgs e)
		{
			if (this.startTime != null) this.startTime.Value = DateTime.Now.AddDays(-7);
			if (this.endTime != null) this.endTime.Value = DateTime.Now;
			if (this.uiTextBox1 != null) this.uiTextBox1.Text = "";
			if (this.uiComboBox2 != null) this.uiComboBox2.SelectedIndex = 0;
			this._defectChecks.ForEach(c => c.Checked = true);
			LoadData();
		}

		private void saveBtn_Click(object sender, EventArgs e)
		{
			if (_currentData == null || _currentData.Rows.Count == 0)
			{
				MessageBox.Show("没有数据可导出！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			SaveFileDialog sfd = new SaveFileDialog
			{
				Filter = "CSV文件|*.csv",
				FileName = $"生产记录_{this.currentMode}_{DateTime.Now:yyyyMMddHHmmss}"
			};
			if (sfd.ShowDialog() != DialogResult.OK) return;

			try
			{
				var headers = GetColumnHeaders();
				using (StreamWriter sw = new StreamWriter(sfd.FileName, false, Encoding.UTF8))
				{
					// 写入中文表头
					for (int i = 0; i < headers.Count; i++)
					{
						if (i > 0) sw.Write(",");
						sw.Write(headers[i].DisplayName);
					}
					sw.WriteLine();

					// 写入数据
					foreach (DataRow row in _currentData.Rows)
					{
						for (int i = 0; i < headers.Count; i++)
						{
							if (i > 0) sw.Write(",");
							string val = row[headers[i].ColumnName]?.ToString() ?? "";
							if (val.Contains(",")) val = "\"" + val + "\"";
							sw.Write(val);
						}
						sw.WriteLine();
					}
				}
				MessageBox.Show("导出成功！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);

			if (this.pnlDefectFilter != null)
			{
				this.pnlDefectFilter.Width = this.ClientSize.Width - 30;
				if (this.flpDefect != null) this.flpDefect.Width = this.pnlDefectFilter.Width - 170;
				if (this.btnDefectAll != null) this.btnDefectAll.Location = new Point(this.pnlDefectFilter.Width - 105, 12);
				if (this.btnDefectClear != null) this.btnDefectClear.Location = new Point(this.pnlDefectFilter.Width - 52, 12);
			}

			if (customGrid != null)
			{
				customGrid.Width = this.ClientSize.Width;
				if (this.currentMode == "detail")
				{
					customGrid.Location = new Point(0, 215);
					customGrid.Height = this.ClientSize.Height - 260;
				}
				else
				{
					customGrid.Location = new Point(0, 50);
					customGrid.Height = this.ClientSize.Height - 95;
				}
				// 刷新表格以适应新尺寸
				customGrid.RefreshLayout();
			}

			if (pnlPage != null)
			{
				btnPrev.Location = new Point(pnlPage.Width - 260, 6);
				lblPageInfo.Location = new Point(pnlPage.Width - 170, 10);
				btnNext.Location = new Point(pnlPage.Width - 85, 6);
			}
		}
	}

	// 列头信息类
	public class ColumnHeaderInfo
	{
		public string ColumnName { get; set; }
		public string DisplayName { get; set; }
		public int Width { get; set; }

		public ColumnHeaderInfo(string colName, string displayName, int width)
		{
			ColumnName = colName;
			DisplayName = displayName;
			Width = width;
		}
	}

	/// <summary>
	/// GDI+ 高性能自定义表格控件 - 支持水平滚动和中文列名
	/// </summary>
	public class CustomDataGridView : Control
	{
		private List<DataRow> _dataRows = new List<DataRow>();
		private DataColumnCollection _columns;
		private List<ColumnHeaderInfo> _columnHeaders;
		private int _rowHeight = 32;
		private int _headerHeight = 36;
		private int _scrollY = 0;
		private int _scrollX = 0;
		private int _visibleRows = 10;
		private int _visibleCols = 8;
		private int _hoverRow = -1;

		private Font _headerFont = new Font("微软雅黑", 10, FontStyle.Bold);
		private Font _cellFont = new Font("微软雅黑", 9);
		private Brush _headerBg = new SolidBrush(Color.FromArgb(47, 60, 76));
		private Brush _headerText = new SolidBrush(Color.White);
		private Brush _rowEven = new SolidBrush(Color.White);
		private Brush _rowOdd = new SolidBrush(Color.FromArgb(248, 249, 250));
		private Brush _rowHover = new SolidBrush(Color.FromArgb(230, 240, 255));
		private Brush _ngBg = new SolidBrush(Color.FromArgb(255, 230, 230));
		private Pen _gridPen = new Pen(Color.FromArgb(230, 235, 240));

		private int[] _colWidths;
		private int _totalWidth;
		private int _totalRows;
		private VScrollBar _vScroll;
		private HScrollBar _hScroll;

		public CustomDataGridView()
		{
			this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);
			this.BackColor = Color.White;

			_vScroll = new VScrollBar { Dock = DockStyle.Right, Width = 18, SmallChange = 1, LargeChange = 10 };
			_vScroll.Scroll += (s, e) => { _scrollY = _vScroll.Value; this.Invalidate(); };

			_hScroll = new HScrollBar { Dock = DockStyle.Bottom, Height = 18, SmallChange = 20, LargeChange = 50 };
			_hScroll.Scroll += (s, e) => { _scrollX = _hScroll.Value; this.Invalidate(); };

			this.Controls.Add(_vScroll);
			this.Controls.Add(_hScroll);
		}

		public void SetData(List<DataRow> rows, DataColumnCollection cols, List<ColumnHeaderInfo> headers)
		{
			_dataRows = rows ?? new List<DataRow>();
			_columns = cols;
			_columnHeaders = headers;
			_totalRows = _dataRows.Count;

			if (_columnHeaders != null && _columnHeaders.Count > 0)
			{
				_colWidths = _columnHeaders.Select(h => h.Width).ToArray();
				_totalWidth = _colWidths.Sum();
			}
			else if (_columns != null && _columns.Count > 0)
			{
				_colWidths = new int[_columns.Count];
				_totalWidth = 0;
				for (int i = 0; i < _columns.Count; i++)
				{
					_colWidths[i] = 100;
					_totalWidth += 100;
				}
			}

			UpdateScrollBars();
			this.Invalidate();
		}

		public void RefreshLayout()
		{
			UpdateScrollBars();
			this.Invalidate();
		}

		private void UpdateScrollBars()
		{
			int clientHeight = this.Height - _headerHeight - (_hScroll.Visible ? _hScroll.Height : 0);
			_visibleRows = Math.Max(1, clientHeight / _rowHeight);

			if (_totalRows > _visibleRows)
			{
				_vScroll.Visible = true;
				_vScroll.Maximum = _totalRows - 1;
				_vScroll.LargeChange = _visibleRows;
			}
			else
			{
				_vScroll.Visible = false;
				_scrollY = 0;
			}

			int clientWidth = this.Width - (_vScroll.Visible ? _vScroll.Width : 0);
			if (_totalWidth > clientWidth)
			{
				_hScroll.Visible = true;
				_hScroll.Maximum = _totalWidth - clientWidth;
				_hScroll.LargeChange = clientWidth / 2;
				// 调整垂直滚动条的位置
				if (_vScroll.Visible) _vScroll.Height = this.Height - _hScroll.Height;
			}
			else
			{
				_hScroll.Visible = false;
				_scrollX = 0;
				if (_vScroll.Visible) _vScroll.Height = this.Height;
			}
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			UpdateScrollBars();
			this.Invalidate();
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			int row = (e.Y - _headerHeight) / _rowHeight + _scrollY;
			int old = _hoverRow;
			_hoverRow = (row >= 0 && row < _totalRows && e.Y > _headerHeight) ? row : -1;
			if (old != _hoverRow) this.Invalidate();
		}

		protected override void OnMouseClick(MouseEventArgs e)
		{
			base.OnMouseClick(e);
			int row = (e.Y - _headerHeight) / _rowHeight + _scrollY;
			if (row >= 0 && row < _totalRows) this.OnDoubleClick(EventArgs.Empty);
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			base.OnMouseWheel(e);
			int delta = e.Delta > 0 ? -3 : 3;
			int val = _vScroll.Value + delta;
			val = Math.Max(0, Math.Min(val, _vScroll.Maximum - _visibleRows + 1));
			if (val != _vScroll.Value)
			{
				_vScroll.Value = val;
				_scrollY = val;
				this.Invalidate();
			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

			if (_dataRows.Count == 0)
			{
				using (var brush = new SolidBrush(Color.FromArgb(150, 150, 150)))
				using (var font = new Font("微软雅黑", 12))
				{
					string msg = "暂无数据";
					SizeF sz = e.Graphics.MeasureString(msg, font);
					e.Graphics.DrawString(msg, font, brush, (this.Width - sz.Width) / 2, (this.Height - sz.Height) / 2);
				}
				return;
			}

			if (_colWidths == null || _columnHeaders == null) return;

			DrawHeader(e.Graphics);
			DrawRows(e.Graphics);
		}

		private void DrawHeader(Graphics g)
		{
			g.FillRectangle(_headerBg, 0, 0, this.Width, _headerHeight);

			int startCol = GetStartCol();
			int endCol = Math.Min(startCol + _visibleCols + 2, _colWidths.Length);
			int x = -_scrollX;

			for (int i = 0; i < startCol; i++) x += _colWidths[i];

			for (int i = startCol; i < endCol; i++)
			{
				int w = _colWidths[i];
				if (x + w < 0) { x += w; continue; }
				if (x > this.Width) break;

				string text = i < _columnHeaders.Count ? _columnHeaders[i].DisplayName : _columns[i].ColumnName;

				using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
				{
					g.DrawString(text, _headerFont, _headerText, new Rectangle(x, 0, w, _headerHeight), fmt);
				}
				g.DrawLine(_gridPen, x + w, 2, x + w, _headerHeight - 2);
				x += w;
			}
			g.DrawLine(_gridPen, 0, _headerHeight - 1, this.Width, _headerHeight - 1);
		}

		private int GetStartCol()
		{
			int sum = 0;
			for (int i = 0; i < _colWidths.Length; i++)
			{
				if (sum + _colWidths[i] > _scrollX) return i;
				sum += _colWidths[i];
			}
			return 0;
		}

		private void DrawRows(Graphics g)
		{
			int startRow = _scrollY;
			int endRow = Math.Min(startRow + _visibleRows + 1, _totalRows);
			int y = _headerHeight;
			int startCol = GetStartCol();
			int endCol = Math.Min(startCol + _visibleCols + 2, _colWidths.Length);

			for (int r = startRow; r < endRow; r++)
			{
				DataRow row = _dataRows[r];
				Brush bg = (r == _hoverRow) ? _rowHover : ((r % 2 == 0) ? _rowEven : _rowOdd);
				g.FillRectangle(bg, 0, y, this.Width, _rowHeight);

				bool isNg = _columns != null && _columns.Contains("final_result") && row["final_result"]?.ToString() == "NG";
				if (isNg) g.FillRectangle(_ngBg, 0, y, this.Width, _rowHeight);

				int x = -_scrollX;
				for (int i = 0; i < startCol; i++) x += _colWidths[i];

				for (int c = startCol; c < endCol; c++)
				{
					int w = _colWidths[c];
					if (x + w < 0) { x += w; continue; }
					if (x > this.Width) break;

					string text = row[c]?.ToString() ?? "";
					Color textColor = isNg ? Color.FromArgb(200, 60, 60) : Color.FromArgb(60, 60, 60);

					g.DrawLine(_gridPen, x, y, x + w, y);
					g.DrawLine(_gridPen, x + w, y, x + w, y + _rowHeight);

					using (var fmt = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
					using (var brush = new SolidBrush(textColor))
					{
						g.DrawString(text, _cellFont, brush, new Rectangle(x + 5, y, w - 10, _rowHeight), fmt);
					}
					x += w;
				}
				g.DrawLine(_gridPen, 0, y + _rowHeight, this.Width, y + _rowHeight);
				y += _rowHeight;
			}
		}
	}
}