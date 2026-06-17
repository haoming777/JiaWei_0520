using CommonLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
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

			// 【关键修复1】必须在这里强制带上 WAL 模式和连接池，防止 SQLiteHelper 内部被覆盖
			// 增加 Synchronous=Normal 可以极大减少 SQLite 频繁刷写硬盘的动作，提升10倍写入性能
			string connString = _databasePath + ";Journal Mode=WAL;Pooling=True;Max Pool Size=100;Synchronous=Normal;wal_autocheckpoint=10000;";
			_dbHelper = new SQLiteHelper(connString);

			_recordQueue = new BlockingCollection<ProductionRecord>(new ConcurrentQueue<ProductionRecord>(), 50000);

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
                        ng_正面工号缺失 INTEGER DEFAULT 0,
                        ng_背面工号缺失 INTEGER DEFAULT 0,
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

				// 补建检测标准列（兼容旧数据库）
				EnsureSummaryConfigColumns();

				_toolClass.SaveLog("数据库表初始化完成");
			}
			catch (Exception ex)
			{
				_toolClass.SaveLog($"[DB] 初始化数据库异常: {ex.Message}\n{ex.StackTrace}");
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
			// 连续爆管剔除已移至消费者线程 INSERT 后执行，确保查 DB 时前序记录已入库

			// 【关键修复2：把 RetroactiveUpdateExcludedRecords 从这里删掉，绝不能在这里阻塞主线程】

			// 添加到队列（阻塞最多2秒等待消费者，防止记录丢失）
			if (!_recordQueue.TryAdd(record, 2000))
			{
				_toolClass.SaveLog($"[严重] 记录队列阻塞超2秒仍未空，丢弃记录: SequenceId={record.SequenceId}");
			}
		}

		/// <summary>
		/// 回溯更新前两条记录为连续爆管剔除
		/// 当发现连续3个爆管时，前两条也需要标记为剔除
		/// 连续爆管剔除优先级最高，需要清空详细缺陷记录
		/// </summary>
		private void RetroactiveUpdateExcludedRecords(long currentSequenceId, string sku)
		{
			try
			{
				// 获取当前SKU
				//string currentSku = GetCurrentSku?.Invoke() ?? "";

				// 直接使用传入的 sku
				string currentSku = sku;

				// 更新连续爆管组的三条记录（当前记录和前两条）
				for (int i = 0; i <= 2; i++)
				{
					long targetId = currentSequenceId - i;
					// 标记为剔除，清空详细缺陷记录，只保留"连续爆管剔除"标识
					// 【防误伤】必须同时满足ng_爆管=1，防止程序重启后ID复用误伤OK品
					string updateSql = @"
						UPDATE production_records_detail 
						SET is_excluded = 1, 
							excluded_reason = '连续爆管剔除',
							defect_detail = '连续爆管剔除',
							defect_count = 0,
							ng_异物 = 0,
							ng_管盖有无 = 0,
							ng_管口圆度 = 0,
							ng_正面工号缺失 = 0,
							ng_背面工号缺失 = 0,
							ng_PCode = 0,
							ng_色标对中 = 0,
							ng_爆管 = 0,
							ng_斜口 = 0,
							ng_未剪断 = 0
						WHERE sequence_id = @sequence_id AND sku = @sku AND ng_爆管 = 1";
					
					_dbHelper.ExecuteNonQuery(updateSql,
						new SQLiteParameter("@sequence_id", targetId),
						new SQLiteParameter("@sku", currentSku));
				}
				
				_toolClass.SaveLog($"回溯更新连续爆管剔除记录: {currentSequenceId-2}, {currentSequenceId-1}, {currentSequenceId}");
			}
			catch (Exception ex)
			{
				_toolClass.SaveLog($"回溯更新连续爆管剔除记录失败: {ex.Message}");
			}
		}

		/// <summary>
		/// 检查连续爆管剔除逻辑
		/// 连续爆管剔除优先级最高：
		/// - 连续三支爆管（不管是单独爆管还是混合缺陷包含爆管）标记为剔除
		/// - 被剔除的记录清空详细缺陷，只保留"连续爆管剔除"标识
		/// </summary>
		private void CheckConsecutiveBurstExclusion(ProductionRecord record)
		{
			// 只要有爆管NG（不管是否有其他缺陷，不管是否归为混合缺陷），且是连续爆管，则标记为剔除
			// 直接检查原始的Cam5_BaoguanResult而不是Ng_爆管字段
			if (record.Cam5_BaoguanResult == 0)
			{
				if (IsConsecutiveBurstExcluded(record.SequenceId))
				{
					record.IsExcluded = true;
					record.ExcludedReason = "连续爆管剔除";
					
					// 连续爆管剔除优先级最高，清空详细缺陷记录
					record.DefectDetail = "连续爆管剔除";
					record.DefectCount = 0;
					record.Ng_异物 = 0;
					record.Ng_管盖有无 = 0;
					record.Ng_管口圆度 = 0;
					record.Ng_正面工号缺失 = 0;
					record.Ng_背面工号缺失 = 0;
					record.Ng_PCode = 0;
					record.Ng_色标对中 = 0;
					record.Ng_爆管 = 0;
					record.Ng_斜口 = 0;
					record.Ng_未剪断 = 0;
					
					// 移除已被标记为剔除的3个连续爆管记录，避免第四个爆管误判
					RemoveExcludedBurstRecords(record.SequenceId);
				}
			}
		}

		/// <summary>
		/// 从爆管历史队列中移除已被标记为剔除的3个连续爆管记录
		/// 这样可以避免第四个爆管与它们形成新的连续爆管组
		/// </summary>
		private void RemoveExcludedBurstRecords(long currentSequenceId)
		{
			lock (_burstLock)
			{
				// 精确移除 currentId, currentId-1, currentId-2（避免清错记录）
				var arr = _burstHistory.ToArray();
				_burstHistory = new Queue<long>(arr.Where(id => id != currentSequenceId && id != currentSequenceId - 1 && id != currentSequenceId - 2));
			}
		}

		/// <summary>
		/// 获取当前SKU（从主窗体获取）
		/// </summary>
		public Func<string> GetCurrentSku { get; set; }

			// 记录提交回调：DB INSERT成功后触发，用于存图
			public Action<long> OnRecordCommitted { get; set; }
			// 连续爆管剔除回调：通知主界面更新计数
			public Action OnBurstExcluded { get; set; }
			// 汇总刷新回调：通知主界面同步良率数据
			public Action<int, int, int> OnSummaryRefreshed { get; set; }

			// 记录提交回调：DB INSERT成功后触发，用于存图

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
		/// 记录所有NG项详情到defect_detail字段
		/// 规则：
		/// 1. 缺陷数>=2：归为"混合缺陷"，同时记录所有缺陷详情
		/// 2. 缺陷数==1：记录具体缺陷名称
		/// 3. 连续爆管剔除优先级最高（后续由CheckConsecutiveBurstExclusion处理）
		/// </summary>
		private void ComputeDefectCategories(ProductionRecord record)
		{
			if (record.FinalResult == "OK")
			{
				record.DefectCount = 0;
				record.DefectDetail = "";
				// 清空所有细分NG
				record.Ng_异物 = 0;
				record.Ng_管盖有无 = 0;
				record.Ng_管口圆度 = 0;
				record.Ng_正面工号缺失 = 0;
				record.Ng_背面工号缺失 = 0;
				record.Ng_PCode = 0;
				record.Ng_色标对中 = 0;
				record.Ng_爆管 = 0;
				record.Ng_斜口 = 0;
				record.Ng_未剪断 = 0;
				return;
			}

			var defects = new List<string>();

			// 相机1 - 管内异物NG
			if (record.Cam1Result == 0)
			{
				record.Ng_异物 = 1;
				defects.Add("管内异物NG");
			}
			else
			{
				record.Ng_异物 = 0;
			}

			// 相机2 - 管盖有无NG
			if (record.Cam2Result == 0)
			{
				record.Ng_管盖有无 = 1;
				defects.Add("管盖有无NG");
			}
			else
			{
				record.Ng_管盖有无 = 0;
			}

			// 相机3 - 管口圆度NG
			if (record.Cam3Result == 0)
			{
				record.Ng_管口圆度 = 1;
				defects.Add("管口圆度NG");
			}
			else
			{
				record.Ng_管口圆度 = 0;
			}

			// 相机4 - 正面工号缺失NG
			if (record.Cam4Result == 0)
			{
				record.Ng_正面工号缺失 = 1;
				defects.Add("正面工号缺失NG");
			}
			else
			{
				record.Ng_正面工号缺失 = 0;
			}

			// 相机5 - 细分缺陷
			// 背面工号缺失
			if (record.Cam5_CharResult == 0)
			{
				record.Ng_背面工号缺失 = 1;
				defects.Add("背面工号缺失NG");
			}
			else
			{
				record.Ng_背面工号缺失 = 0;
			}

			// P-Code NG
			if (record.Cam5_PCodeResult == 0)
			{
				record.Ng_PCode = 1;
				defects.Add("P-CodeNG");
			}
			else
			{
				record.Ng_PCode = 0;
			}

			// 色标对中NG
			if (record.Cam5_SebiaoResult == 0)
			{
				record.Ng_色标对中 = 1;
				defects.Add("色标对中NG");
			}
			else
			{
				record.Ng_色标对中 = 0;
			}

			// 爆管NG
			if (record.Cam5_BaoguanResult == 0)
			{
				record.Ng_爆管 = 1;
				defects.Add("爆管NG");
			}
			else
			{
				record.Ng_爆管 = 0;
			}

			// 斜口NG
			if (record.Cam5_XiekouResult == 0)
			{
				record.Ng_斜口 = 1;
				defects.Add("斜口NG");
			}
			else
			{
				record.Ng_斜口 = 0;
			}

			// 未剪断NG
			if (record.Cam5_WeijianduanResult == 0)
			{
				record.Ng_未剪断 = 1;
				defects.Add("未剪断NG");
			}
			else
			{
				record.Ng_未剪断 = 0;
			}

			record.DefectCount = defects.Count;

			// 判断缺陷类型并记录详情
			if (defects.Count >= 2)
			{
				// 2种或以上缺陷：归为"混合缺陷"，同时记录所有缺陷详情
				record.DefectDetail = "混合缺陷[" + string.Join(",", defects) + "]";
			}
			else if (defects.Count == 1)
			{
				// 只有1种缺陷类型：记录具体缺陷名称
				record.DefectDetail = defects[0];
			}
			else
			{
				record.DefectDetail = "";
			}
		}

		// 连续爆管追踪 - 使用队列存储最近的爆管记录（只记录包含爆管的检测）
		private Queue<long> _burstHistory = new Queue<long>();
		private const int BURST_HISTORY_SIZE = 100;

		/// <summary>
		/// 更新连续爆管状态（需要在主线程中调用，传入相机 5 的结果）
		/// 规则：只有包含爆管缺陷的记录才加入历史队列
		/// </summary>
		public void UpdateConsecutiveBurst(long sequenceId, bool isBurstNg)
		{
			lock (_burstLock)
			{
				// 只有包含爆管缺陷才记录到历史队列
				if (isBurstNg)
				{
					_burstHistory.Enqueue(sequenceId);
					try { FastLogger.Instance.Debug($"爆管入队: ID={sequenceId} 队列长度={_burstHistory.Count}"); } catch {}

					if (_burstHistory.Count > BURST_HISTORY_SIZE)
					{
						long removed = _burstHistory.Dequeue();
						try { FastLogger.Instance.Debug($"爆管出队(满): ID={removed} 队列长度={_burstHistory.Count}"); } catch {}
					}
				}
			}
		}

		/// <summary>
		/// 检查序列是否被标记为连续爆管剔除
		/// 规则：连续三支爆管（不管是单独爆管还是混合缺陷包含爆管），在第三支时标记为剔除
		/// 必须满足：序列号连续（中间没有非爆管记录插入）且三条都包含爆管
		/// 正确示例：
		///   [爆管，混合缺陷 (爆管 + 未剪断)，爆管] -> 连续爆管剔除
		///   [爆管，爆管，爆管] -> 连续爆管剔除
		///   [混合缺陷 (爆管 + 未剪断)，混合缺陷 (爆管 + 未剪断)，混合缺陷 (爆管 + 未剪断)] -> 连续爆管剔除
		/// 错误示例：
		///   [爆管，斜口，爆管] -> 不算（中间有非爆管）
		/// </summary>
						public bool IsConsecutiveBurstExcluded(long sequenceId)
		{
			// 查 DB：sequence_id-1 和 sequence_id-2 是否都 cam5_result=0（连续爆管）
			// 当前记录已在 DB 中，所以查前两条即可凑齐3条连续
			try
			{
				string sql = "SELECT COUNT(*) FROM production_records_detail WHERE sequence_id = @id1 AND cam5_result = 0 AND is_excluded = 0";
				var r1 = _dbHelper.ExecuteQuery(sql, new SQLiteParameter("@id1", sequenceId - 1));
				int c1 = (r1 != null && r1.Rows.Count > 0) ? Convert.ToInt32(r1.Rows[0][0]) : 0;
				if (c1 == 0) return false;

				var r2 = _dbHelper.ExecuteQuery(sql, new SQLiteParameter("@id1", sequenceId - 2));
				int c2 = (r2 != null && r2.Rows.Count > 0) ? Convert.ToInt32(r2.Rows[0][0]) : 0;
				bool result = c2 >= 1;
				if (result) try { FastLogger.Instance.Debug("连续爆管: ID=" + sequenceId); } catch {}
				return result;
			}
			catch { return false; }
		}

	public int GetCurrentConsecutiveBurstCount()
		{
			lock (_burstLock)
			{
				// 队列中只包含爆管记录，直接检查序列号连续性
				var arr = _burstHistory.ToArray();
				if (arr.Length < 3) return 0;
				
				// 从队列末尾向前检查序列号是否连续
				int count = 1;
				for (int i = arr.Length - 1; i > 0; i--)
				{
					if (arr[i] == arr[i - 1] + 1)
					{
						count++;
					}
					else
					{
						break; // 序列号不连续，中断
					}
				}
				
				return count >= 3 ? count : 0;
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
				_toolClass.SaveLog($"[DB] 开始保存记录 - SequenceId:{record.SequenceId}, FinalResult:{record.FinalResult}, SKU:{record.Sku}");

				string insertSql = @"
                    INSERT INTO production_records_detail (
                        p_time, p_date, p_shift, p_shift_date, sku, sequence_id,
                        final_result, cam1_result, cam2_result, cam3_result, cam4_result, cam5_result,
                        ng_异物, ng_管盖有无, ng_管口圆度, ng_正面工号缺失, ng_背面工号缺失,
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
					new SQLiteParameter("@ng4", record.Ng_正面工号缺失),
					new SQLiteParameter("@ng5", record.Ng_背面工号缺失),
					new SQLiteParameter("@ng6", record.Ng_PCode),
					new SQLiteParameter("@ng7", record.Ng_色标对中),
					new SQLiteParameter("@ng8", record.Ng_爆管),
					new SQLiteParameter("@ng9", record.Ng_斜口),
					new SQLiteParameter("@ng10", record.Ng_未剪断),
					new SQLiteParameter("@defect_detail", record.DefectDetail),
					new SQLiteParameter("@defect_count", record.DefectCount),
					new SQLiteParameter("@is_excluded", record.IsExcluded ? 1 : 0)
				};

				bool success = _dbHelper.ExecuteNonQuery(insertSql, parameters);
				if (!success)
				{
					_toolClass.SaveLog($"[DB] 保存记录失败 - SequenceId:{record.SequenceId}");
				}
				else
				{
					_toolClass.SaveLog($"[DB] 保存记录成功 - SequenceId:{record.SequenceId}");

					// 【INSERT 后检查连续爆管】记录已入库，查 DB 前 2 条是否也是爆管
					if (record.Cam5_BaoguanResult == 0)
					{
						bool burstExcluded = IsConsecutiveBurstExcluded(record.SequenceId);
						try { FastLogger.Instance.Debug($"连续爆管检查: ID={record.SequenceId} Cam5Burst={record.Cam5_BaoguanResult} 结果={burstExcluded}"); } catch {}
						if (burstExcluded)
						{
							// 更新当前记录
							string updateSql = "UPDATE production_records_detail SET is_excluded=1, excluded_reason='连续爆管剔除', defect_detail='连续爆管剔除', defect_count=0, ng_爆管=0, ng_斜口=0, ng_未剪断=0, ng_异物=0, ng_管盖有无=0, ng_管口圆度=0, ng_正面工号缺失=0, ng_背面工号缺失=0, ng_PCode=0, ng_色标对中=0 WHERE sequence_id=@id AND sku=@sku";
							_dbHelper.ExecuteNonQuery(updateSql, new SQLiteParameter("@id", record.SequenceId), new SQLiteParameter("@sku", record.Sku));
							// 回溯前 2 条
							RetroactiveUpdateExcludedRecords(record.SequenceId, record.Sku);
							// 通知主界面更新计数
							try { OnBurstExcluded?.Invoke(); } catch { }
						}
					}

					// 记录已入库，触发存图回调
					if (record.UnifiedId > 0)
					{
						OnRecordCommitted?.Invoke(record.UnifiedId);
					}

					// 新SKU首条记录：立刻创建汇总行，避免30秒窗口期内报表查不到
					EnsureSummaryExists(record);

					// 汇总表定时全量更新（_batchFlushTimer 30秒）
				}
			}
			catch (Exception ex)
			{
				_toolClass.SaveLog($"[DB] 保存记录异常: {ex.Message}\n{ex.StackTrace}");
			}
		}

		/// <summary>
		/// 定时刷新当前班次的汇总表（30秒触发，替代之前的每产品更新）
		/// </summary>
		public void RefreshCurrentSummary()
		{
			try
			{
				string sku = GetCurrentSku?.Invoke() ?? "";
				if (string.IsNullOrEmpty(sku)) return;
				var now = DateTime.Now;
				string shift = GetCurrentShift(now.Hour);
				string date = shift == "夜班" && now.Hour < 8 ? now.AddDays(-1).ToString("yyMMdd") : now.ToString("yyMMdd");
				UpdateOrCreateSummary(date, shift, sku);
			}
			catch { }
		}

		private static string GetCurrentShift(int hour)
		{
			if (hour >= 8 && hour <= 15) return "早班";
			if (hour >= 16 && hour <= 23) return "中班";
			return "夜班";
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

		/// <summary>
		/// 完整导出班次报表（包含所有SKU的汇总）
		/// </summary>
		/// <param name="date">班次归属日期</param>
		/// <param name="shift">班次名称</param>
		/// <param name="openFolderAfterExport">导出完成后是否打开文件夹</param>
		public void ExportFullShiftReport(string date, string shift, bool openFolderAfterExport = false, bool skipDetailExport = false)
		{
			Task.Run(() =>
			{
				try
				{
					// 等待队列处理完成
					while (_recordQueue.Count > 0)
					{
						Thread.Sleep(100);
					}

					// 首先生成所有SKU的汇总
					GenerateShiftSummaryInternal(date, shift);

					// 导出报表
					string reportPath = ExportFullShiftReportToCSV(date, shift, skipDetailExport);

					string desc = skipDetailExport ? "汇总" : "完整";
					_toolClass.SaveLog($"{desc}班次报表已导出: {date} {shift}");

					// 如果需要，打开保存报表的文件夹
					if (openFolderAfterExport && !string.IsNullOrEmpty(reportPath) && Directory.Exists(reportPath))
					{
						System.Diagnostics.Process.Start("explorer.exe", reportPath);
					}
				}
				catch (Exception ex)
				{
					_toolClass.SaveLog($"导出完整班次报表失败: {ex.Message}");
				}
			});
		}

		/// <summary>
		/// 完整导出班次报表到CSV
		/// </summary>
		/// <returns>报表保存的文件夹路径</returns>
		private string ExportFullShiftReportToCSV(string date, string shift, bool skipDetailExport = false)
		{
			try
			{
				string imagePath = GetImagePath();
				if (string.IsNullOrEmpty(imagePath)) return null;

				string exportDir = Path.Combine(imagePath, "Reports", date);
				if (!Directory.Exists(exportDir))
					Directory.CreateDirectory(exportDir);

				string fileName = $"{date}_{shift}_完整生产报表.csv";
				string filePath = Path.Combine(exportDir, fileName);

				// 读取当前系统设置（兜底：当 DB 列为空时使用 INI 当前值）
				var cfg = CommonLib.Class_Config._Config;
				string fallbackCam1Stand = cfg.Camera1StandChar.ToString();
				string fallbackCam2Stand = cfg.Camera2StandChar.ToString();
				string fallbackTotalArea = cfg.totalArea_Camera1.ToString();
				string fallbackBaoGuan = cfg.Camera5IFBaoGuan ? "开启" : "关闭";
				string fallbackXieKou = cfg.Camera5IFXieKou ? "开启" : "关闭";
				string fallbackWeiJianDuan = cfg.Camera5IFWeiJianDuan ? "开启" : "关闭";
				string fallbackSeBiao = cfg.Camera5IFSeBiao ? "开启" : "关闭";
				string fallbackOcr = cfg.Camera5IFOcr ? "开启" : "关闭";

				using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
				{
					// ══════════════════════════════════
					// 汇总表：包含所有历史班次（倒序，最新在前）
					// ══════════════════════════════════
					string summarySql = @"
						SELECT * FROM production_records_summary
						ORDER BY p_date DESC, p_shift DESC, sku";

					var summaryData = _dbHelper.ExecuteQuery(summarySql);

					writer.WriteLine("# ==== 汇总表（全部历史班次，倒序） ====");
					writer.WriteLine("日期,班次,SKU,总检数,OK总数,NG总数,管内异物NG数量,管盖有无NG数量,管口圆度NG数量,正面工号不齐数量,背面工号不齐数量,P-CodeNG数量,色标对中NG数量,爆管数量,斜口数量,未剪断数量,混合多种缺陷,连续爆管剔除,良率(%),正面字符标准数量,反面字符标准数量,异物面积上限标准,爆管检测状态,斜口检测状态,未剪断检测状态,色标对中检测状态,反面字符检测状态");

					if (summaryData.Rows.Count > 0)
					{
						foreach (DataRow row in summaryData.Rows)
						{
							// 优先读 DB 列（生产时的真实快照），为空则用当前 INI 兜底
							string c1 = (row["cfg_正面字符标准"]?.ToString()) ?? fallbackCam1Stand;
							string c2 = (row["cfg_反面字符标准"]?.ToString()) ?? fallbackCam2Stand;
							string c3 = (row["cfg_异物面积上限"]?.ToString()) ?? fallbackTotalArea;
							string c4 = (row["cfg_爆管检测"]?.ToString()) ?? fallbackBaoGuan;
							string c5 = (row["cfg_斜口检测"]?.ToString()) ?? fallbackXieKou;
							string c6 = (row["cfg_未剪断检测"]?.ToString()) ?? fallbackWeiJianDuan;
							string c7 = (row["cfg_色标检测"]?.ToString()) ?? fallbackSeBiao;
							string c8 = (row["cfg_反面字符检测"]?.ToString()) ?? fallbackOcr;

							writer.WriteLine(
								$"{row["p_date"]},{row["p_shift"]},{row["sku"]}," +
								$"{row["total_count"]},{row["ok_count"]},{row["ng_count"]}," +
								$"{row["ng_异物"]},{row["ng_管盖有无"]},{row["ng_管口圆度"]},{row["ng_正面工号缺失"]},{row["ng_背面工号缺失"]}," +
								$"{row["ng_PCode"]},{row["ng_色标对中"]},{row["ng_爆管"]},{row["ng_斜口"]},{row["ng_未剪断"]}," +
								$"{row["ng_混合多种缺陷"]},{row["continuous_exclude_count"]},{row["yield_rate"]}," +
								$"{c1},{c2},{c3},{c4},{c5},{c6},{c7},{c8}");
						}
					}
					else
					{
						writer.WriteLine("(无汇总记录)");
					}

					// ══════════════════════════════════
					// 明细记录（仅当前班次，倒序）—— 仅完整导出时包含
					// ══════════════════════════════════
					if (!skipDetailExport)
					{
						writer.WriteLine("");
						writer.WriteLine($"# ==== 检测明细记录（{date} {shift}，按时间倒序） ====");
						writer.WriteLine("检测时间,日期,班次,SKU,流水号,结果,缺陷详情,缺陷数,连续爆管剔除,剔除原因");

						string detailSql = @"
							SELECT p_time, p_date, p_shift, sku, sequence_id,
							       final_result, defect_detail, defect_count,
							       is_excluded, excluded_reason
							FROM production_records_detail
							WHERE p_shift_date = @date AND p_shift = @shift
							ORDER BY p_time DESC";

						var detailData = _dbHelper.ExecuteQuery(detailSql,
							new SQLiteParameter("@date", date),
							new SQLiteParameter("@shift", shift));

						if (detailData.Rows.Count > 0)
						{
							foreach (DataRow row in detailData.Rows)
							{
								string pTime = row["p_time"]?.ToString() ?? "";
								string finalResult = row["final_result"]?.ToString() ?? "";
								string defectDetail = (row["defect_detail"]?.ToString() ?? "").Replace(",", "；");
								string excludedReason = (row["excluded_reason"]?.ToString() ?? "").Replace(",", "；");

								writer.WriteLine($"{pTime},{row["p_date"]},{row["p_shift"]},{row["sku"]},{row["sequence_id"]}," +
									$"{finalResult},{defectDetail},{row["defect_count"]},{row["is_excluded"]},{excludedReason}");
							}
						}
						else
						{
							writer.WriteLine("(无明细记录)");
						}
					}
				}

				_toolClass.SaveLog($"完整报表已导出: {filePath}");

				// 返回保存报表的文件夹路径
				return exportDir;
			}
			catch (Exception ex)
			{
				_toolClass.SaveLog($"导出完整班次报表异常: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// 检查并自动补建汇总表中的检测标准列（向前兼容旧数据库）
		/// </summary>
				private void EnsureSummaryConfigColumns()
		{
			// 需要新增的列定义（列名, 类型, 默认值）
			var columns = new (string Name, string Type, string Default)[]
			{
				("cfg_正面字符标准", "INTEGER", "0"),
				("cfg_反面字符标准", "INTEGER", "0"),
				("cfg_异物面积上限", "INTEGER", "0"),
				("cfg_爆管检测", "TEXT", "''"),
				("cfg_斜口检测", "TEXT", "''"),
				("cfg_未剪断检测", "TEXT", "''"),
				("cfg_色标检测", "TEXT", "''"),
				("cfg_反面字符检测", "TEXT", "''"),
			};

			foreach (var col in columns)
			{
				try
				{
					string alterSql = "ALTER TABLE production_records_summary ADD COLUMN " + col.Name + " " + col.Type + " DEFAULT " + col.Default;
					_dbHelper.ExecuteNonQuery(alterSql);
					_toolClass.SaveLog("[DB迁移] 已添加列: " + col.Name);
					try { FastLogger.Instance.Info("DB迁移: 已添加列 " + col.Name); } catch { }
				}
				catch
				{
					// 列已存在，正常跳过
					try { FastLogger.Instance.Debug("DB迁移: 列已存在 " + col.Name); } catch { }
				}
			}
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
			// 连续爆管剔除优先级最高：被剔除的记录不计入其他缺陷统计
			// 优先级：连续爆管剔除 > 混合缺陷 > 单一缺陷
			string detailSql = @"
                SELECT 
                    COUNT(*) as total_count,
                    SUM(CASE WHEN final_result = 'OK' THEN 1 ELSE 0 END) as ok_count,
                    SUM(CASE WHEN final_result = 'NG' THEN 1 ELSE 0 END) as ng_count,
                    -- 连续爆管剔除：直接统计被剔除记录数（与主界面burstExcludeCount一致）
                    (SELECT COUNT(*) FROM production_records_detail d2
                     WHERE d2.p_shift_date = production_records_detail.p_shift_date
                     AND d2.p_shift = production_records_detail.p_shift
                     AND d2.sku = production_records_detail.sku
                         AND d2.excluded_reason = '连续爆管剔除'
                                        ) as exclude_count,
                    
                    -- 单一缺陷统计（仅统计未被剔除且缺陷数=1的记录）
                    SUM(CASE WHEN is_excluded = 0 AND defect_count = 1 AND ng_异物 = 1 THEN 1 ELSE 0 END) as ng_异物,
                    SUM(CASE WHEN is_excluded = 0 AND defect_count = 1 AND ng_管盖有无 = 1 THEN 1 ELSE 0 END) as ng_管盖有无,
                    SUM(CASE WHEN is_excluded = 0 AND defect_count = 1 AND ng_管口圆度 = 1 THEN 1 ELSE 0 END) as ng_管口圆度,
                    SUM(CASE WHEN is_excluded = 0 AND defect_count = 1 AND ng_正面工号缺失 = 1 THEN 1 ELSE 0 END) as ng_正面工号缺失,
                    SUM(CASE WHEN is_excluded = 0 AND defect_count = 1 AND ng_背面工号缺失 = 1 THEN 1 ELSE 0 END) as ng_背面工号缺失,
                    SUM(CASE WHEN is_excluded = 0 AND defect_count = 1 AND ng_爆管 = 1 THEN 1 ELSE 0 END) as ng_爆管,
                    SUM(CASE WHEN is_excluded = 0 AND defect_count = 1 AND ng_斜口 = 1 THEN 1 ELSE 0 END) as ng_斜口,
                    SUM(CASE WHEN is_excluded = 0 AND defect_count = 1 AND ng_未剪断 = 1 THEN 1 ELSE 0 END) as ng_未剪断,
                    SUM(CASE WHEN is_excluded = 0 AND defect_count = 1 AND ng_PCode = 1 THEN 1 ELSE 0 END) as ng_PCode,
                    SUM(CASE WHEN is_excluded = 0 AND defect_count = 1 AND ng_色标对中 = 1 THEN 1 ELSE 0 END) as ng_色标对中,
                    
                    -- 混合多种缺陷：缺陷数>=2的NG记录，但被连续爆管剔除的不计入
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

				// 良率计算：剔除的记录不计入分母
				double yieldRate = (totalCount - excludeCount) > 0
					? Math.Min(100.0, Math.Max(0.0, (double)okCount / (totalCount - excludeCount) * 100))
					: 0;

				// 更新汇总表
				string upsertSql = @"
                    INSERT OR REPLACE INTO production_records_summary (
                        p_date, p_shift, sku, total_count, ok_count, ng_count,
                        ng_异物, ng_管盖有无, ng_管口圆度, ng_正面工号缺失, ng_背面工号缺失,
                        ng_爆管, ng_斜口, ng_未剪断, ng_混合多种缺陷, ng_PCode, ng_色标对中,
                        continuous_exclude_count, yield_rate, summary_date,
                        cfg_正面字符标准, cfg_反面字符标准, cfg_异物面积上限,
                        cfg_爆管检测, cfg_斜口检测, cfg_未剪断检测, cfg_色标检测, cfg_反面字符检测
                    ) VALUES (
                        @date, @shift, @sku, @total, @ok, @ng,
                        @ng1, @ng2, @ng3, @ng4, @ng5, @ng8, @ng9, @ng10, @ng_mix, @ng6, @ng7,
                        @exclude, @yield, @summary_date,
                        @cfg1, @cfg2, @cfg3, @cfg4, @cfg5, @cfg6, @cfg7, @cfg8
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
					new SQLiteParameter("@ng4", row["ng_正面工号缺失"]),
					new SQLiteParameter("@ng5", row["ng_背面工号缺失"]),
					new SQLiteParameter("@ng6", row["ng_PCode"]),
					new SQLiteParameter("@ng7", row["ng_色标对中"]),
					new SQLiteParameter("@ng8", row["ng_爆管"]),
					new SQLiteParameter("@ng9", row["ng_斜口"]),
					new SQLiteParameter("@ng10", row["ng_未剪断"]),
					new SQLiteParameter("@ng_mix", row["ng_混合多种缺陷"]),
					new SQLiteParameter("@exclude", excludeCount),
					new SQLiteParameter("@yield", Math.Round(yieldRate, 2)),
					new SQLiteParameter("@summary_date", DateTime.Now.ToString("yyyy-MM-dd")),
					// 检测标准（每次汇总时从本地 INI 读取最新值，记录快照）
					new SQLiteParameter("@cfg1", CommonLib.Class_Config._Config.Camera1StandChar),
					new SQLiteParameter("@cfg2", CommonLib.Class_Config._Config.Camera2StandChar),
					new SQLiteParameter("@cfg3", CommonLib.Class_Config._Config.totalArea_Camera1),
					new SQLiteParameter("@cfg4", CommonLib.Class_Config._Config.Camera5IFBaoGuan ? "开启" : "关闭"),
					new SQLiteParameter("@cfg5", CommonLib.Class_Config._Config.Camera5IFXieKou ? "开启" : "关闭"),
					new SQLiteParameter("@cfg6", CommonLib.Class_Config._Config.Camera5IFWeiJianDuan ? "开启" : "关闭"),
					new SQLiteParameter("@cfg7", CommonLib.Class_Config._Config.Camera5IFSeBiao ? "开启" : "关闭"),
					new SQLiteParameter("@cfg8", CommonLib.Class_Config._Config.Camera5IFOcr ? "开启" : "关闭")
				};

				_dbHelper.ExecuteNonQuery(upsertSql, upsertParams);
					try { FastLogger.Instance.Debug("汇总写入: SKU=" + sku + " Date=" + date + " Total=" + totalCount + " OK=" + okCount + " NG=" + ngCount + " Yield=" + yieldRate.ToString("F2")); } catch {}
				try { OnSummaryRefreshed?.Invoke(totalCount, okCount, excludeCount); } catch {}

				// 移除：不再每一条记录都导出Excel文件，避免耗时
				// ExportSummaryToExcel(date, shift, sku);
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
							$"{row["ng_异物"]},{row["ng_管盖有无"]},{row["ng_管口圆度"]},{row["ng_正面工号缺失"]},{row["ng_背面工号缺失"]}," +
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

		/// <summary>
		/// 手动触发班次汇总（用于班次结束时）
		/// </summary>
		/// <param name="date">统计归属日期</param>
		/// <param name="shift">班次名称</param>
		/// <param name="sku">SKU（可选，为空时汇总所有SKU）</param>
		public void TriggerShiftSummary(string date, string shift, string sku = "")
		{
			Task.Run(() =>
			{
				try
				{
					// 等待队列处理完成
					while (_recordQueue.Count > 0)
					{
						Thread.Sleep(100);
					}

					// 生成汇总
					if (string.IsNullOrEmpty(sku))
					{
						GenerateShiftSummary(date, shift);
					}
					else
					{
						// 针对特定SKU生成汇总
						UpdateOrCreateSummary(date, shift, sku);
					}
				}
				catch (Exception ex)
				{
					_toolClass.SaveLog($"手动触发班次汇总异常: {ex.Message}");
				}
			});
		}

		/// <summary>
		/// 获取所有SKU列表（用于汇总所有SKU）
		/// </summary>
		/// <returns></returns>
		public List<string> GetAllSkus()
		{
			List<string> skus = new List<string>();
			try
			{
				string sql = "SELECT DISTINCT sku FROM production_records_detail WHERE sku IS NOT NULL AND sku != ''";
				var data = _dbHelper.ExecuteQuery(sql);
				foreach (DataRow row in data.Rows)
				{
					skus.Add(row["sku"].ToString());
				}
			}
			catch (Exception ex)
			{
				_toolClass.SaveLog($"获取SKU列表异常: {ex.Message}");
			}
			return skus;
		}

		/// <summary>
		/// 确保汇总行存在并有初始计数：新SKU/新班次首条记录入库时立刻创建并填充
		/// 后续记录由30秒定时器全量更新
		/// </summary>
		private void EnsureSummaryExists(ProductionRecord record)
		{
			try
			{
				// 检查是否已存在汇总行
				string checkSql = "SELECT COUNT(*) FROM production_records_summary WHERE p_date = @date AND p_shift = @shift AND sku = @sku";
				var result = _dbHelper.ExecuteQuery(checkSql,
					new SQLiteParameter("@date", record.ShiftDateStr),
					new SQLiteParameter("@shift", record.Shift),
					new SQLiteParameter("@sku", record.Sku));

				int existingCount = (result != null && result.Rows.Count > 0)
					? Convert.ToInt32(result.Rows[0][0]) : 0;
				if (existingCount == 0)
				{
					// 新SKU/新班次：立刻跑一次完整汇总，确保报表能立刻查到
					UpdateOrCreateSummary(record.ShiftDateStr, record.Shift, record.Sku);
					try { FastLogger.Instance.Debug($"首条汇总: SKU={record.Sku} Date={record.ShiftDateStr} Shift={record.Shift}"); } catch { }
				}
				else
				{
					try { FastLogger.Instance.Debug($"汇总行已存在: SKU={record.Sku} Date={record.ShiftDateStr}"); } catch { }
				}
			}
			catch (Exception ex)
			{
				_toolClass.SaveLog($"首条汇总创建异常: {ex.Message}\n{ex.StackTrace}");
				try { CommonLib.FastLogger.Instance.Error($"汇总行创建失败 SKU={record.Sku} Date={record.ShiftDateStr} Shift={record.Shift}", ex); } catch { }
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
		public long UnifiedId { get; set; }       // 统一产品ID（=Cam1SeqId-Cam1Offset）
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
		public int Ng_正面工号缺失 { get; set; }
		public int Ng_背面工号缺失 { get; set; }
		public int Ng_PCode { get; set; }
		public int Ng_色标对中 { get; set; }
		public int Ng_爆管 { get; set; }
		public int Ng_斜口 { get; set; }
		public int Ng_未剪断 { get; set; }

		public int DefectCount { get; set; }
		public string DefectDetail { get; set; }
		public bool IsExcluded { get; set; }
		public string ExcludedReason { get; set; }
	}
}