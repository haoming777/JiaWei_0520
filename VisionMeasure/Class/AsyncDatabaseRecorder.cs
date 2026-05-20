using CommonLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XL.Tool;

namespace VisionMeasure
{
	/// <summary>
	/// 异步数据库记录器 - 不阻塞主检测流程
	/// </summary>
	public class AsyncDatabaseRecorder : IDisposable
	{
		private readonly BlockingCollection<ProductionRecord> _recordQueue;
		private readonly Thread _workerThread;
		private readonly string _databasePath;
		private readonly SQLiteHelper _dbHelper;
		private volatile bool _isRunning = true;
		private bool _disposed = false;
		private XLToolClass _toolClass = new XLToolClass();

		// 连续爆管追踪
		private long _lastSequenceId = 0;
		private int _consecutiveBurstCount = 0;
		private long _consecutiveStartId = 0;
		private readonly object _burstLock = new object();

		// 班次缓存
		private string _cachedShift = "";
		private string _cachedShiftDate = "";
		private DateTime _lastShiftCheck = DateTime.MinValue;

		public AsyncDatabaseRecorder(string databasePath = null)
		{
			if (string.IsNullOrEmpty(databasePath))
			{
				databasePath = Path.Combine(Directory.GetCurrentDirectory(), "data", "production.db");
			}
			_databasePath = databasePath;

			// 确保目录存在
			string directory = Path.GetDirectoryName(_databasePath);
			if (!Directory.Exists(directory))
				Directory.CreateDirectory(directory);

			_dbHelper = new SQLiteHelper();
			_recordQueue = new BlockingCollection<ProductionRecord>(new ConcurrentQueue<ProductionRecord>(), 10000);

			// 初始化数据库表
			InitializeDatabase();

			// 启动工作线程
			_workerThread = new Thread(ProcessQueue)
			{
				Name = "AsyncDatabaseRecorder",
				IsBackground = true,
				Priority = ThreadPriority.BelowNormal
			};
			_workerThread.Start();

			_toolClass.SaveLog("异步数据库记录器初始化完成");
		}

		private void InitializeDatabase()
		{
			try
			{
				// 创建详细记录表
				string createDetailTable = @"
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

				// 创建汇总表
				string createSummaryTable = @"
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
                        ng_爆管 INTEGER DEFAULT 0,
                        ng_斜口 INTEGER DEFAULT 0,
                        ng_未剪断 INTEGER DEFAULT 0,
                        ng_混合多种缺陷 INTEGER DEFAULT 0,
                        ng_PCode INTEGER DEFAULT 0,
                        ng_色标对中 INTEGER DEFAULT 0,
                        continuous_exclude_count INTEGER DEFAULT 0,
                        yield_rate REAL DEFAULT 0,
                        summary_date TEXT DEFAULT CURRENT_DATE,
                        UNIQUE(p_date, p_shift, sku)
                    )";

				_dbHelper.ExecuteNonQuery(createDetailTable);
				_dbHelper.ExecuteNonQuery(createSummaryTable);

				_toolClass.SaveLog("数据库表初始化完成");
			}
			catch (Exception ex)
			{
				_toolClass.SaveLog($"数据库初始化失败: {ex.Message}");
			}
		}

		/// <summary>
		/// 添加生产记录（异步，不阻塞）
		/// </summary>
		public void AddRecord(ProductionRecord record)
		{
			if (_disposed || record == null) return;

			// 计算班次和归属日期
			ComputeShiftAndDate(record);

			// 计算缺陷分类
			ComputeDefectCategories(record);

			// 添加到队列
			if (!_recordQueue.TryAdd(record))
			{
				_toolClass.SaveLog($"记录队列已满，丢弃记录: SequenceId={record.SequenceId}");
			}
		}

		/// <summary>
		/// 获取当前SKU（从主窗体获取）
		/// </summary>
		public Func<string> GetCurrentSku { get; set; }

		/// <summary>
		/// 计算班次和归属日期
		/// </summary>
		private void ComputeShiftAndDate(ProductionRecord record)
		{
			DateTime dt = record.DetectionTime;
			int hour = dt.Hour;

			if (hour >= 8 && hour <= 15)
			{
				record.Shift = "早班";
				record.ShiftDate = dt.Date;
			}
			else if (hour >= 16 && hour <= 23)
			{
				record.Shift = "中班";
				record.ShiftDate = dt.Date;
			}
			else // 00:00 - 07:59
			{
				record.Shift = "夜班";
				record.ShiftDate = dt.Date.AddDays(-1); // 归属前一天
			}

			record.ShiftDateStr = record.ShiftDate.ToString("yyyy-MM-dd");
			record.DateStr = dt.ToString("yyyy-MM-dd");
		}

		/// <summary>
		/// 计算缺陷分类
		/// </summary>
		private void ComputeDefectCategories(ProductionRecord record)
		{
			if (record.FinalResult == "OK")
			{
				record.DefectCount = 0;
				record.DefectDetail = "";
				return;
			}

			var defects = new List<string>();

			// 相机1 - 管内异物NG
			if (record.Cam1Result == 0)
			{
				record.Ng_异物 = 1;
				defects.Add("管内异物NG");
			}

			// 相机2 - 管盖有无NG
			if (record.Cam2Result == 0)
			{
				record.Ng_管盖有无 = 1;
				defects.Add("管盖有无NG");
			}

			// 相机3 - 管口圆度NG
			if (record.Cam3Result == 0)
			{
				record.Ng_管口圆度 = 1;
				defects.Add("管口圆度NG");
			}

			// 相机4 - 正面工号不齐NG
			if (record.Cam4Result == 0)
			{
				record.Ng_正面工号不齐 = 1;
				defects.Add("正面工号不齐NG");
			}

			// 相机5 - 细分缺陷
			if (record.Cam5Result == 0)
			{
				// 背面工号不齐
				if (record.Cam5_CharResult == 0)
				{
					record.Ng_背面工号不齐 = 1;
					defects.Add("背面工号不齐NG");
				}

				// P-Code NG
				if (record.Cam5_PCodeResult == 0)
				{
					record.Ng_PCode = 1;
					defects.Add("P-CodeNG");
				}

				// 色标对中NG
				if (record.Cam5_SebiaoResult == 0)
				{
					record.Ng_色标对中 = 1;
					defects.Add("色标对中NG");
				}

				// 爆管NG
				if (record.Cam5_BaoguanResult == 0)
				{
					record.Ng_爆管 = 1;
					defects.Add("爆管NG");
				}

				// 斜口NG
				if (record.Cam5_XiekouResult == 0)
				{
					record.Ng_斜口 = 1;
					defects.Add("斜口NG");
				}

				// 未剪断NG
				if (record.Cam5_WeijianduanResult == 0)
				{
					record.Ng_未剪断 = 1;
					defects.Add("未剪断NG");
				}
			}

			record.DefectCount = defects.Count;
			record.DefectDetail = string.Join("、", defects);

			// 注意：混合多种缺陷的标记在汇总时处理，这里不设置
		}

		/// <summary>
		/// 更新连续爆管状态（需要在主线程中调用，传入相机5的结果）
		/// </summary>
		public void UpdateConsecutiveBurst(long sequenceId, bool isBurstNg, bool isPureBurst)
		{
			lock (_burstLock)
			{
				if (isBurstNg && isPureBurst)
				{
					if (_consecutiveBurstCount == 0)
					{
						_consecutiveStartId = sequenceId;
						_consecutiveBurstCount = 1;
					}
					else if (sequenceId == _lastSequenceId + 1)
					{
						_consecutiveBurstCount++;
					}
					else
					{
						_consecutiveBurstCount = 1;
						_consecutiveStartId = sequenceId;
					}
				}
				else
				{
					// 中断连续爆管
					_consecutiveBurstCount = 0;
					_consecutiveStartId = 0;
				}

				_lastSequenceId = sequenceId;
			}
		}

		/// <summary>
		/// 检查序列是否被标记为连续异常剔除
		/// </summary>
		public bool IsConsecutiveBurstExcluded(long sequenceId)
		{
			lock (_burstLock)
			{
				if (_consecutiveBurstCount >= 3 &&
					sequenceId >= _consecutiveStartId &&
					sequenceId <= _consecutiveStartId + _consecutiveBurstCount - 1)
				{
					return true;
				}
				return false;
			}
		}

		/// <summary>
		/// 获取当前连续爆管数量
		/// </summary>
		public int GetCurrentConsecutiveBurstCount()
		{
			lock (_burstLock)
			{
				return _consecutiveBurstCount >= 3 ? _consecutiveBurstCount : 0;
			}
		}

		/// <summary>
		/// 处理队列中的记录
		/// </summary>
		private void ProcessQueue()
		{
			while (_isRunning)
			{
				try
				{
					if (_recordQueue.TryTake(out var record, 100))
					{
						SaveRecordToDatabase(record);
					}
				}
				catch (Exception ex)
				{
					_toolClass.SaveLog($"处理数据库记录异常: {ex.Message}");
				}
			}
		}

		/// <summary>
		/// 保存单条记录到数据库
		/// </summary>
		private void SaveRecordToDatabase(ProductionRecord record)
		{
			try
			{
				string insertSql = @"
                    INSERT INTO production_records_detail (
                        p_time, p_date, p_shift, p_shift_date, sku, sequence_id,
                        final_result, cam1_result, cam2_result, cam3_result, cam4_result, cam5_result,
                        ng_异物, ng_管盖有无, ng_管口圆度, ng_正面工号不齐, ng_背面工号不齐,
                        ng_PCode, ng_色标对中, ng_爆管, ng_斜口, ng_未剪断,
                        defect_detail, defect_count, is_excluded
                    ) VALUES (
                        @p_time, @p_date, @p_shift, @p_shift_date, @sku, @sequence_id,
                        @final_result, @cam1, @cam2, @cam3, @cam4, @cam5,
                        @ng1, @ng2, @ng3, @ng4, @ng5, @ng6, @ng7, @ng8, @ng9, @ng10,
                        @defect_detail, @defect_count, @is_excluded
                    )";

				var parameters = new SQLiteParameter[]
				{
					new SQLiteParameter("@p_time", record.DetectionTime),
					new SQLiteParameter("@p_date", record.DateStr),
					new SQLiteParameter("@p_shift", record.Shift),
					new SQLiteParameter("@p_shift_date", record.ShiftDateStr),
					new SQLiteParameter("@sku", record.Sku),
					new SQLiteParameter("@sequence_id", record.SequenceId),
					new SQLiteParameter("@final_result", record.FinalResult),
					new SQLiteParameter("@cam1", record.Cam1Result),
					new SQLiteParameter("@cam2", record.Cam2Result),
					new SQLiteParameter("@cam3", record.Cam3Result),
					new SQLiteParameter("@cam4", record.Cam4Result),
					new SQLiteParameter("@cam5", record.Cam5Result),
					new SQLiteParameter("@ng1", record.Ng_异物),
					new SQLiteParameter("@ng2", record.Ng_管盖有无),
					new SQLiteParameter("@ng3", record.Ng_管口圆度),
					new SQLiteParameter("@ng4", record.Ng_正面工号不齐),
					new SQLiteParameter("@ng5", record.Ng_背面工号不齐),
					new SQLiteParameter("@ng6", record.Ng_PCode),
					new SQLiteParameter("@ng7", record.Ng_色标对中),
					new SQLiteParameter("@ng8", record.Ng_爆管),
					new SQLiteParameter("@ng9", record.Ng_斜口),
					new SQLiteParameter("@ng10", record.Ng_未剪断),
					new SQLiteParameter("@defect_detail", record.DefectDetail),
					new SQLiteParameter("@defect_count", record.DefectCount),
					new SQLiteParameter("@is_excluded", record.IsExcluded ? 1 : 0)
				};

				_dbHelper.ExecuteNonQuery(insertSql, parameters);
			}
			catch (Exception ex)
			{
				_toolClass.SaveLog($"保存记录失败: {ex.Message}");
			}
		}

		/// <summary>
		/// 班次结束时生成汇总报表
		/// </summary>
		public void GenerateShiftSummary(string date, string shift)
		{
			Task.Run(() =>
			{
				try
				{
					GenerateShiftSummaryInternal(date, shift);
				}
				catch (Exception ex)
				{
					_toolClass.SaveLog($"生成班次汇总失败: {ex.Message}");
				}
			});
		}

		private void GenerateShiftSummaryInternal(string date, string shift)
		{
			// 获取该班次所有SKU
			string getSkusSql = @"
                SELECT DISTINCT sku FROM production_records_detail 
                WHERE p_shift_date = @date AND p_shift = @shift";

			var skus = _dbHelper.ExecuteQuery(getSkusSql,
				new SQLiteParameter("@date", date),
				new SQLiteParameter("@shift", shift));

			foreach (DataRow row in skus.Rows)
			{
				string sku = row["sku"].ToString();
				UpdateOrCreateSummary(date, shift, sku);
			}

			_toolClass.SaveLog($"班次汇总生成完成: {date} {shift}");
		}

		private void UpdateOrCreateSummary(string date, string shift, string sku)
		{
			// 获取该班次该SKU的详细统计数据
			string detailSql = @"
                SELECT 
                    COUNT(*) as total_count,
                    SUM(CASE WHEN final_result = 'OK' THEN 1 ELSE 0 END) as ok_count,
                    SUM(CASE WHEN final_result = 'NG' AND is_excluded = 0 THEN 1 ELSE 0 END) as ng_count,
                    SUM(CASE WHEN is_excluded = 1 THEN 1 ELSE 0 END) as exclude_count,
                    
                    SUM(ng_异物) as ng_异物,
                    SUM(ng_管盖有无) as ng_管盖有无,
                    SUM(ng_管口圆度) as ng_管口圆度,
                    SUM(ng_正面工号不齐) as ng_正面工号不齐,
                    SUM(ng_背面工号不齐) as ng_背面工号不齐,
                    SUM(ng_爆管) as ng_爆管,
                    SUM(ng_斜口) as ng_斜口,
                    SUM(ng_未剪断) as ng_未剪断,
                    SUM(ng_PCode) as ng_PCode,
                    SUM(ng_色标对中) as ng_色标对中,
                    
                    SUM(CASE WHEN defect_count >= 2 AND final_result = 'NG' AND is_excluded = 0 THEN 1 ELSE 0 END) as ng_混合多种缺陷
                    
                FROM production_records_detail 
                WHERE p_shift_date = @date AND p_shift = @shift AND sku = @sku";

			var parameters = new SQLiteParameter[]
			{
				new SQLiteParameter("@date", date),
				new SQLiteParameter("@shift", shift),
				new SQLiteParameter("@sku", sku)
			};

			var result = _dbHelper.ExecuteQuery(detailSql, parameters);
			if (result.Rows.Count > 0)
			{
				DataRow row = result.Rows[0];

				int totalCount = Convert.ToInt32(row["total_count"]);
				int okCount = Convert.ToInt32(row["ok_count"]);
				int ngCount = Convert.ToInt32(row["ng_count"]);
				int excludeCount = Convert.ToInt32(row["exclude_count"]);

				double yieldRate = totalCount - excludeCount > 0
					? (double)okCount / (totalCount - excludeCount) * 100
					: 0;

				// 更新汇总表
				string upsertSql = @"
                    INSERT OR REPLACE INTO production_records_summary (
                        p_date, p_shift, sku, total_count, ok_count, ng_count,
                        ng_异物, ng_管盖有无, ng_管口圆度, ng_正面工号不齐, ng_背面工号不齐,
                        ng_爆管, ng_斜口, ng_未剪断, ng_混合多种缺陷, ng_PCode, ng_色标对中,
                        continuous_exclude_count, yield_rate, summary_date
                    ) VALUES (
                        @date, @shift, @sku, @total, @ok, @ng,
                        @ng1, @ng2, @ng3, @ng4, @ng5, @ng8, @ng9, @ng10, @ng_mix, @ng6, @ng7,
                        @exclude, @yield, @summary_date
                    )";

				var upsertParams = new SQLiteParameter[]
				{
					new SQLiteParameter("@date", date),
					new SQLiteParameter("@shift", shift),
					new SQLiteParameter("@sku", sku),
					new SQLiteParameter("@total", totalCount),
					new SQLiteParameter("@ok", okCount),
					new SQLiteParameter("@ng", ngCount),
					new SQLiteParameter("@ng1", row["ng_异物"]),
					new SQLiteParameter("@ng2", row["ng_管盖有无"]),
					new SQLiteParameter("@ng3", row["ng_管口圆度"]),
					new SQLiteParameter("@ng4", row["ng_正面工号不齐"]),
					new SQLiteParameter("@ng5", row["ng_背面工号不齐"]),
					new SQLiteParameter("@ng6", row["ng_PCode"]),
					new SQLiteParameter("@ng7", row["ng_色标对中"]),
					new SQLiteParameter("@ng8", row["ng_爆管"]),
					new SQLiteParameter("@ng9", row["ng_斜口"]),
					new SQLiteParameter("@ng10", row["ng_未剪断"]),
					new SQLiteParameter("@ng_mix", row["ng_混合多种缺陷"]),
					new SQLiteParameter("@exclude", excludeCount),
					new SQLiteParameter("@yield", Math.Round(yieldRate, 2)),
					new SQLiteParameter("@summary_date", DateTime.Now.ToString("yyyy-MM-dd"))
				};

				_dbHelper.ExecuteNonQuery(upsertSql, upsertParams);

				// 导出Excel文件
				ExportSummaryToExcel(date, shift, sku);
			}
		}

		private void ExportSummaryToExcel(string date, string shift, string sku)
		{
			try
			{
				string imagePath = GetImagePath();
				if (string.IsNullOrEmpty(imagePath)) return;

				string exportDir = Path.Combine(imagePath, "Reports", date);
				if (!Directory.Exists(exportDir))
					Directory.CreateDirectory(exportDir);

				string fileName = $"{date}_{shift}_{sku}_汇总报表.csv";
				string filePath = Path.Combine(exportDir, fileName);

				// 获取汇总数据
				string summarySql = @"
                    SELECT * FROM production_records_summary 
                    WHERE p_date = @date AND p_shift = @shift AND sku = @sku";

				var data = _dbHelper.ExecuteQuery(summarySql,
					new SQLiteParameter("@date", date),
					new SQLiteParameter("@shift", shift),
					new SQLiteParameter("@sku", sku));

				if (data.Rows.Count > 0)
				{
					using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
					{
						// 写入表头
						writer.WriteLine("日期,班次,SKU,总检数,OK总数,NG总数,管内异物NG数量,管盖有无NG数量,管口圆度NG数量,正面工号不齐数量,背面工号不齐数量,爆管数量,斜口数量,未剪断数量,P-CodeNG数量,色标对中NG数量,混合多种缺陷,连续爆管剔除,良率(%)");

						DataRow row = data.Rows[0];
						writer.WriteLine($"{row["p_date"]},{row["p_shift"]},{row["sku"]},{row["total_count"]},{row["ok_count"]},{row["ng_count"]}," +
							$"{row["ng_异物"]},{row["ng_管盖有无"]},{row["ng_管口圆度"]},{row["ng_正面工号不齐"]},{row["ng_背面工号不齐"]}," +
							$"{row["ng_爆管"]},{row["ng_斜口"]},{row["ng_未剪断"]},{row["ng_PCode"]},{row["ng_色标对中"]}," +
							$"{row["ng_混合多种缺陷"]},{row["continuous_exclude_count"]},{row["yield_rate"]}");
					}

					_toolClass.SaveLog($"报表已导出: {filePath}");
				}
			}
			catch (Exception ex)
			{
				_toolClass.SaveLog($"导出Excel失败: {ex.Message}");
			}
		}

		private string GetImagePath()
		{
			try
			{
				return CommonLib.Class_Config._Config.ImagePath;
			}
			catch
			{
				return Path.Combine(Directory.GetCurrentDirectory(), "image");
			}
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			_isRunning = false;
			_recordQueue.CompleteAdding();

			if (_workerThread != null && _workerThread.IsAlive)
			{
				_workerThread.Join(3000);
			}

			_recordQueue.Dispose();
			_toolClass.SaveLog("异步数据库记录器已释放");
		}
	}

	/// <summary>
	/// 生产记录实体类
	/// </summary>
	public class ProductionRecord
	{
		public DateTime DetectionTime { get; set; } = DateTime.Now;
		public string DateStr { get; set; }
		public string Shift { get; set; }
		public DateTime ShiftDate { get; set; }
		public string ShiftDateStr { get; set; }
		public string Sku { get; set; }
		public long SequenceId { get; set; }
		public string FinalResult { get; set; }  // "OK" 或 "NG"

		// 相机结果 (1=OK, 0=NG)
		public int Cam1Result { get; set; } = 1;
		public int Cam2Result { get; set; } = 1;
		public int Cam3Result { get; set; } = 1;
		public int Cam4Result { get; set; } = 1;
		public int Cam5Result { get; set; } = 1;

		// 相机5细分结果
		public int Cam5_CharResult { get; set; } = 1;      // 背面工号
		public int Cam5_PCodeResult { get; set; } = 1;     // P-Code
		public int Cam5_SebiaoResult { get; set; } = 1;    // 色标对中
		public int Cam5_BaoguanResult { get; set; } = 1;   // 爆管
		public int Cam5_XiekouResult { get; set; } = 1;    // 斜口
		public int Cam5_WeijianduanResult { get; set; } = 1; // 未剪断

		// NG标志位
		public int Ng_异物 { get; set; }
		public int Ng_管盖有无 { get; set; }
		public int Ng_管口圆度 { get; set; }
		public int Ng_正面工号不齐 { get; set; }
		public int Ng_背面工号不齐 { get; set; }
		public int Ng_PCode { get; set; }
		public int Ng_色标对中 { get; set; }
		public int Ng_爆管 { get; set; }
		public int Ng_斜口 { get; set; }
		public int Ng_未剪断 { get; set; }

		public int DefectCount { get; set; }
		public string DefectDetail { get; set; }
		public bool IsExcluded { get; set; }
	}
}