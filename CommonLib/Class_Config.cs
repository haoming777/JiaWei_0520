using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Ini.IniAPI;
using static System.Net.Mime.MediaTypeNames;

namespace CommonLib
{

	public struct PLC
	{
		private string spec;
		private string name;
		private string ipath;
		public void set(string vspec, string controlname, string vpath)
		{
			spec = vspec;
			name = controlname;
			ipath = vpath;
		}
		/// <summary>
		/// button数据读写
		/// </summary>
		public string Button
		{
			get
			{
				return GetPrivateProfileString(spec, name, "0", ipath);//型号、button名字、默认值、路径
			}
			set
			{
				INIWriteValue(ipath, spec, name, value.ToString());
			}
		}
		/// <summary>
		/// text数据读写
		/// </summary>
		public string Text
		{
			get
			{
				return GetPrivateProfileString(spec, name, "0", ipath);//型号、text名字、默认值、路径
			}
			set
			{
				INIWriteValue(ipath, spec, name, value.ToString());
			}
		}
	}
	public class Class_Config
	{
		private static Class_Config _instance;
		private Class_Config() { }
		// 定义标识符确保多线程安全性
		private static readonly object locker = new object();

		// INI配置缓存：避免每次getter都P/Invoke读磁盘（每周期~35次）
		private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _iniCache
			= new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

		private string GetCachedValue(string section, string key, string defaultValue)
		{
			string cacheKey = section + "|" + key;
			if (_iniCache.TryGetValue(cacheKey, out string cached))
				return cached;
			string value = GetPrivateProfileString(section, key, defaultValue, _iniPath);
			_iniCache[cacheKey] = value;
			return value;
		}

		private void SetCachedValue(string section, string key, string value)
		{
			string cacheKey = section + "|" + key;
			_iniCache[cacheKey] = value;
			INIWriteValue(_iniPath, section, key, value);
		}

		public static PLC plc;
		public static Class_Config GetInstance()
		{
			if (_instance == null)
			{
				lock (locker)
				{
					if (_instance == null)
					{
						_instance = new Class_Config();
					}
				}
			}
			return _instance;
		}

		public static Class_Config _Config
		{
			get
			{
				if (_instance == null)
				{
					lock (locker)
					{
						if (_instance == null)
						{
							_instance = new Class_Config();
						}
					}
				}

				return _instance;
			}
		}
		public bool test = false;

		public static string _path = Directory.GetCurrentDirectory();
		public string _iniPath = _path + "\\setup.ini";//这里改成了static类型的，不知道会不会影响
		public string _vppPath = _path + "\\vpp\\";
		public string _dataPath = _path + "\\data\\data.dt";
		#region 需要存储的数据


		/// <summary>
		/// 当前检测型号
		/// </summary>
		public string CurCheckSpec
		{
			get
			{
				return GetCachedValue("system", "curSpec", "A50");
			}
			set
			{
				SetCachedValue("system", "curSpec", value.ToString());
			}
		}

		/// <summary>
		/// 上次使用的SKU
		/// </summary>
		public string LastSku
		{
			get
			{
				return GetCachedValue("system", "LastSku", "");
			}
			set
			{
				SetCachedValue("system", "LastSku", value.ToString());
			}
		}

		/// <summary>
		/// 当前检测把型
		/// </summary>
		public string CurCheckBa
		{
			get
			{
				return GetCachedValue("system", "curBa", "塑料把");
			}
			set
			{
				SetCachedValue("system", "curBa", value.ToString());
			}
		}
		/// <summary>
		/// 第一行检测字符
		/// </summary>
		public string FirstString
		{
			get
			{
				return GetCachedValue("system", "First", "MFG");
			}
			set
			{
				SetCachedValue("system", "First", value.ToString());
			}
		}
		/// <summary>
		/// 第二行检测字符
		/// </summary>
		public string SecondString
		{
			get
			{
				return GetCachedValue("system", "Second", "MFG");
			}
			set
			{
				SetCachedValue("system", "Second", value.ToString());
			}
		}

		public int cameraDebug
		{
			get
			{
				return GetPrivateProfileInt("system", "cameraDebug", 0, _iniPath);
			}
			set
			{
				SetCachedValue("system", "cameraDebug", value.ToString());
			}
		}


		/// <summary>
		/// 创建新把型
		/// </summary>
		public string CreateBa
		{
			get
			{
				return GetCachedValue("system", "CreateBa", "塑料把");
			}
			set
			{
				SetCachedValue("system", "CreateBa", value.ToString());
			}
		}
		/// <summary>
		/// 当前产品ID
		/// </summary>
		public string CurProductId
		{
			get
			{
				return GetCachedValue("system", "curId", "1");
			}
			set
			{
				SetCachedValue("system", "curId", value.ToString());
			}
		}

		/// <summary>
		/// 图片存储地址
		/// </summary>
		public string ImagePath
		{
			get
			{
				return GetCachedValue("system", "imagePath", _path + "\\image");
			}
			set
			{
				SetCachedValue("system", "imagePath", value.ToString());
			}
		}

		//public string DataPath
		//{
		//	get
		//	{
		//		return GetCachedValue("路径", "dataPath", _path + "\\data");
		//	}
		//	set
		//	{
		//		SetCachedValue("路径", "dataPath", value.ToString());
		//	}
		//}

		/// <summary>
		/// 相机序列号
		/// </summary>
		public string Camera1SN
		{
			get
			{
				return GetCachedValue("camera", "camera1sn", "DA1665132");
			}
			set
			{
				SetCachedValue("camera", "camera1sn", value.ToString());
			}
		}



		public bool IsSaveOkImage
		{
			get
			{
				return bool.Parse(GetCachedValue("system", "saveokimage", "true"));
			}
			set
			{
				SetCachedValue("system", "saveokimage", value.ToString());
			}

		}
		public bool IsSaveNgImage
		{
			get
			{
				return bool.Parse(GetCachedValue("system", "savengimage", "true"));
			}
			set
			{
				SetCachedValue("system", "savengimage", value.ToString());
			}

		}
		public bool IsSaveOkRawImage
		{
			get
			{
				return bool.Parse(GetCachedValue("system", "saveokrawimage", "true"));
			}
			set
			{
				SetCachedValue("system", "saveokrawimage", value.ToString());
			}

		}
		public bool IsSaveNgRawImage
		{
			get
			{
				return bool.Parse(GetCachedValue("system", "savengrawimage", "true"));
			}
			set
			{
				SetCachedValue("system", "savengrawimage", value.ToString());
			}

		}
		/// <summary>
		/// 保留天数
		/// </summary>
		public int ImageDays
		{
			get
			{
				return GetPrivateProfileInt("system", "imagedays", (int)7, _iniPath);
			}
			set
			{
				SetCachedValue("system", "imagedays", value.ToString());
			}
		}
		public string Camera2SN
		{
			get
			{
				return GetCachedValue("camera", "camera2sn", "DA1665132");
			}
			set
			{
				SetCachedValue("camera", "camera2sn", value.ToString());
			}
		}


		/// <summary>
		/// 相机1是否屏蔽最后一位
		/// </summary>
		public string Camera1Ignore
		{
			get
			{
				return GetCachedValue("StandChar", "Camera1Ignore", "False");
			}
			set
			{
				SetCachedValue("StandChar", "Camera1Ignore", value.ToString());
			}
		}
		/// <summary>
		/// 相机2是否屏蔽最后一位
		/// </summary>
		public string Camera2Ignore
		{
			get
			{
				return GetCachedValue("StandChar", "Camera2Ignore", "False");
			}
			set
			{
				SetCachedValue("StandChar", "Camera2Ignore", value.ToString());
			}
		}



		public string Camera3SN
		{
			get
			{
				return GetCachedValue("camera", "camera3sn", "DA1665132");
			}
			set
			{
				SetCachedValue("camera", "camera3sn", value.ToString());
			}
		}

		public string Camera4SN
		{
			get
			{
				return GetCachedValue("camera", "camera4sn", "DA1665132");
			}
			set
			{
				SetCachedValue("camera", "camera4sn", value.ToString());
			}
		}

		public string Camera5SN
		{
			get
			{
				return GetCachedValue("camera", "camera5sn", "DA1665132");
			}
			set
			{
				SetCachedValue("camera", "camera5sn", value.ToString());
			}
		}

		public string ControlIP
		{
			get
			{
				return GetCachedValue("control", "ipaddr", "192.168000.0.11");
			}
			set
			{
				SetCachedValue("control", "ipaddr", value.ToString());
			}
		}


		public string ModbusIP
		{
			get
			{
				return GetCachedValue("modbus", "modbusIP", "127.0.0.1");
			}
			set
			{
				SetCachedValue("modbus", "modbusIP", value.ToString());
			}
		}

		public int ModbusPort
		{
			get
			{
				return GetPrivateProfileInt("modbus", "modbusPort", 502, _iniPath);
			}
			set
			{
				SetCachedValue("modbus", "modbusPort", value.ToString());
			}
		}
		/// <summary>
		/// 倍率，系统属性
		/// </summary>
		public double K
		{
			get
			{
				return GetPrivateProfileDouble("system", "K", 0.05, _iniPath);
			}
			set
			{
				SetCachedValue("system", "K", value.ToString());
			}
		}

		/// <summary>
		/// 倍率，系统属性
		/// </summary>
		public double K_Cam3
		{
			get
			{
				return GetPrivateProfileDouble("params", "K_Cam3", 0.05, _iniPath);
			}
			set
			{
				SetCachedValue("params", "K_Cam3", value.ToString());
			}
		}

		/// <summary>
		/// 补偿值
		/// </summary>
		public double Offset
		{
			get
			{
				return GetPrivateProfileDouble("system", "Offset", 0.05, _iniPath);
			}
			set
			{
				SetCachedValue("system", "Offset", value.ToString());
			}
		}


		/// <summary>
		/// 补偿值
		/// </summary>
		public double Astrict
		{
			get
			{
				return GetPrivateProfileDouble("system", "Astrict", 2, _iniPath);
			}
			set
			{
				SetCachedValue("system", "Astrict", value.ToString());
			}
		}

		/// <summary>
		/// 段差阈值parameters
		/// </summary>
		public double DuanChaThreshold
		{
			get
			{
				return GetPrivateProfileDouble("Parameter", _Config.CurCheckSpec + "_Duancha", 5, _iniPath);
			}
			set
			{
				SetCachedValue("Parameter", _Config.CurCheckSpec + "_Duancha", value.ToString());
			}
		}

		#region 计数器（内存缓存，5秒批量写盘）
		private static double _totalVal = -1;
		private static double _okVal, _ng1Val, _ng2Val, _ng3Val, _ng4Val, _ng5Val, _burstVal;
		private static DateTime _cntLastFlush = DateTime.MinValue;
		private static readonly object _cntLock = new object();

		private static void LazyLoadCounters()
		{
			if (_totalVal >= 0) return;
			lock (_cntLock)
			{
				if (_totalVal >= 0) return;
				_totalVal = GetPrivateProfileDouble("count", "total", 0, _instance._iniPath);
				_okVal = GetPrivateProfileDouble("count", "ok", 0, _instance._iniPath);
				_ng1Val = GetPrivateProfileDouble("count", "ng_cam1", 0, _instance._iniPath);
				_ng2Val = GetPrivateProfileDouble("count", "ng_cam2", 0, _instance._iniPath);
				_ng3Val = GetPrivateProfileDouble("count", "ng_cam3", 0, _instance._iniPath);
				_ng4Val = GetPrivateProfileDouble("count", "ng_cam4", 0, _instance._iniPath);
				_ng5Val = GetPrivateProfileDouble("count", "ng_cam5", 0, _instance._iniPath);
				_burstVal = GetPrivateProfileDouble("count", "burstExcludeCount", 0, _instance._iniPath);
			}
		}

		private static void FlushCounters()
		{
			if ((DateTime.Now - _cntLastFlush).TotalMilliseconds < 5000) return;
			lock (_cntLock)
			{
				if ((DateTime.Now - _cntLastFlush).TotalMilliseconds < 5000) return;
				string ip = _instance._iniPath;
				INIWriteValue(ip, "count", "total", _totalVal.ToString());
				INIWriteValue(ip, "count", "ok", _okVal.ToString());
				INIWriteValue(ip, "count", "ng_cam1", _ng1Val.ToString());
				INIWriteValue(ip, "count", "ng_cam2", _ng2Val.ToString());
				INIWriteValue(ip, "count", "ng_cam3", _ng3Val.ToString());
				INIWriteValue(ip, "count", "ng_cam4", _ng4Val.ToString());
				INIWriteValue(ip, "count", "ng_cam5", _ng5Val.ToString());
				INIWriteValue(ip, "count", "burstExcludeCount", _burstVal.ToString());
				_cntLastFlush = DateTime.Now;
			}
		}

		/// <summary>
		/// 批量清零所有计数器（原子操作，只写一次盘）
		/// </summary>
		public static void ResetAllCounters()
		{
			lock (_cntLock)
			{
				_totalVal = 0; _okVal = 0;
				_ng1Val = 0; _ng2Val = 0; _ng3Val = 0; _ng4Val = 0; _ng5Val = 0;
				_burstVal = 0;
				string ip = _instance._iniPath;
				INIWriteValue(ip, "count", "total", "0");
				INIWriteValue(ip, "count", "ok", "0");
				INIWriteValue(ip, "count", "ng_cam1", "0");
				INIWriteValue(ip, "count", "ng_cam2", "0");
				INIWriteValue(ip, "count", "ng_cam3", "0");
				INIWriteValue(ip, "count", "ng_cam4", "0");
				INIWriteValue(ip, "count", "ng_cam5", "0");
				INIWriteValue(ip, "count", "burstExcludeCount", "0");
				_cntLastFlush = DateTime.Now;
			}
		}

		/// <summary>【P2】强制落盘计数器（无5秒节流），在程序退出时调用防止最后几秒的计数丢失</summary>
		public static void FlushCountersNow()
		{
			lock (_cntLock)
			{
				string ip = _instance._iniPath;
				INIWriteValue(ip, "count", "total", _totalVal.ToString());
				INIWriteValue(ip, "count", "ok", _okVal.ToString());
				INIWriteValue(ip, "count", "ng_cam1", _ng1Val.ToString());
				INIWriteValue(ip, "count", "ng_cam2", _ng2Val.ToString());
				INIWriteValue(ip, "count", "ng_cam3", _ng3Val.ToString());
				INIWriteValue(ip, "count", "ng_cam4", _ng4Val.ToString());
				INIWriteValue(ip, "count", "ng_cam5", _ng5Val.ToString());
				INIWriteValue(ip, "count", "burstExcludeCount", _burstVal.ToString());
				_cntLastFlush = DateTime.Now;
				try { FastLogger.Instance.Info("[Counters] Force-flushed to INI on shutdown"); } catch { }
			}
		}

		public double total
		{
			get { LazyLoadCounters(); return _totalVal; }
			set { _totalVal = value; FlushCounters(); }
		}
		public double ok
		{
			get { LazyLoadCounters(); return _okVal; }
			set { _okVal = value; FlushCounters(); }
		}
		public double ng_cam1
		{
			get { LazyLoadCounters(); return _ng1Val; }
			set { _ng1Val = value; FlushCounters(); }
		}
		public double burstExcludeCount
		{
			get { LazyLoadCounters(); return _burstVal; }
			set { _burstVal = value; FlushCounters(); }
		}
		public double ng_cam2
		{
			get { LazyLoadCounters(); return _ng2Val; }
			set { _ng2Val = value; FlushCounters(); }
		}
		public double ng_cam3
		{
			get { LazyLoadCounters(); return _ng3Val; }
			set { _ng3Val = value; FlushCounters(); }
		}
		public double ng_cam4
		{
			get { LazyLoadCounters(); return _ng4Val; }
			set { _ng4Val = value; FlushCounters(); }
		}
		public double ng_cam5
		{
			get { LazyLoadCounters(); return _ng5Val; }
			set { _ng5Val = value; FlushCounters(); }
		}
		#endregion

		/// <summary>
		/// 相机一运行总耗时
		/// </summary>
		public int TotalTimeCam1
		{
			get
			{
				return GetPrivateProfileInt("system", "totalTimeCam1", 0, _iniPath);
			}
			set
			{
				SetCachedValue("system", "totalTimeCam1", value.ToString());
			}
		}

		/// <summary>
		/// 相机一运行总耗时
		/// </summary>
		public int TotalTimeCam2
		{
			get
			{
				return GetPrivateProfileInt("system", "totalTimeCam2", 0, _iniPath);
			}
			set
			{
				SetCachedValue("system", "totalTimeCam2", value.ToString());
			}
		}


		#endregion

		public double BaoGuan_minArea
		{
			get
			{
				return GetPrivateProfileDouble("system", "BaoGuan_minArea", 0, _iniPath);
			}
			set
			{
				SetCachedValue("system", "BaoGuan_minArea", value.ToString());
			}
		}

		#region 系统设置
		/// <summary>
		/// 是否初始化相机
		/// </summary>
		public string IFInitCamera
		{
			get
			{
				return GetCachedValue("system", "IFInitCamera", "True");
			}
			set
			{
				SetCachedValue("system", "IFInitCamera", value.ToString());
			}
		}

		public bool IFSaveLog
		{
			get
			{
				return bool.Parse(GetCachedValue("system", "IFRunSaveLog", "False"));
			}
			set
			{
				SetCachedValue("system", "IFRunSaveLog", value.ToString());
			}
		}

		/// <summary>
		/// 是否强制NG相机四
		/// </summary>
		public bool IFCamera4NG
		{
			get
			{
				return bool.Parse(GetCachedValue("system", "IFCamera4NG", "False"));
			}
			set
			{
				SetCachedValue("system", "IFCamera4NG", value.ToString());
			}
		}

		public string ImagePath1
		{
			get
			{
				return GetCachedValue("temp", "ImagePath1", "True");
			}
			set
			{
				SetCachedValue("temp", "ImagePath1", value.ToString());
			}
		}

		public string ImagePath2
		{
			get
			{
				return GetCachedValue("temp", "ImagePath2", "True");
			}
			set
			{
				SetCachedValue("temp", "ImagePath2", value.ToString());
			}
		}
		public string ImagePath2_1
		{
			get
			{
				return GetCachedValue("temp", "ImagePath2_1", "True");
			}
			set
			{
				SetCachedValue("temp", "ImagePath2_1", value.ToString());
			}
		}

		public string ImagePath3
		{
			get
			{
				return GetCachedValue("temp", "ImagePath3", "True");
			}
			set
			{
				SetCachedValue("temp", "ImagePath3", value.ToString());
			}
		}

		public string ImagePath4
		{
			get
			{
				return GetCachedValue("temp", "ImagePath4", "True");
			}
			set
			{
				SetCachedValue("temp", "ImagePath4", value.ToString());
			}
		}

		public string ImagePath5
		{
			get
			{
				return GetCachedValue("temp", "ImagePath5", "True");
			}
			set
			{
				SetCachedValue("temp", "ImagePath5", value.ToString());
			}
		}
		#endregion

		#region 工位输入输出口

		#region 工位输入信号
		/// <summary>
		/// 相机一触发口
		/// </summary>
		public int Input_Camera1
		{
			get
			{
				return GetPrivateProfileInt("input_port", "cam1", -1, _iniPath);
			}
			set
			{
				SetCachedValue("input_port", "cam1", value.ToString());
			}
		}

		/// <summary>
		/// 相机二触发口
		/// </summary>
		public int Input_Camera2
		{
			get
			{
				return GetPrivateProfileInt("input_port", "cam2", -1, _iniPath);
			}
			set
			{
				SetCachedValue("input_port", "cam2", value.ToString());
			}
		}

		/// <summary>
		/// 相机三触发口
		/// </summary>
		public int Input_Camera3
		{
			get
			{
				return GetPrivateProfileInt("input_port", "cam3", -1, _iniPath);
			}
			set
			{
				SetCachedValue("input_port", "cam3", value.ToString());
			}
		}

		/// <summary>
		/// 相机四触发口
		/// </summary>
		public int Input_Camera4
		{
			get
			{
				return GetPrivateProfileInt("input_port", "cam4", -1, _iniPath);
			}
			set
			{
				SetCachedValue("input_port", "cam4", value.ToString());
			}
		}

		/// <summary>
		/// 相机五触发口
		/// </summary>
		public int Input_Camera5
		{
			get
			{
				return GetPrivateProfileInt("input_port", "cam5", -1, _iniPath);
			}
			set
			{
				SetCachedValue("input_port", "cam5", value.ToString());
			}
		}
		#endregion

		#region 工位输出信号
		/// <summary>
		/// 相机一触发口
		/// </summary>
		public string Output_Camera1
		{
			get
			{
				return GetCachedValue("output_port", "cam1", "MX7080.4");
			}
			set
			{
				SetCachedValue("output_port", "cam1", value.ToString());
			}
		}

		/// <summary>
		/// 相机二触发口
		/// </summary>
		public string Output_Camera2
		{
			get
			{
				return GetCachedValue("output_port", "cam2", "MX7080.3");
			}
			set
			{
				SetCachedValue("output_port", "cam2", value.ToString());
			}
		}

		/// <summary>
		/// 相机三触发口
		/// </summary>
		public string Output_Camera3
		{
			get
			{
				return GetCachedValue("output_port", "cam3", "MX7080.3");
			}
			set
			{
				SetCachedValue("output_port", "cam3", value.ToString());
			}
		}

		/// <summary>
		/// 相机四触发口
		/// </summary>
		public string Output_Camera4
		{
			get
			{
				return GetCachedValue("output_port", "cam4", "MX7080.1");
			}
			set
			{
				SetCachedValue("output_port", "cam4", value.ToString());
			}
		}

		/// <summary>
		/// 相机五触发口
		/// </summary>
		public string Output_Camera5
		{
			get
			{
				return GetCachedValue("output_port", "cam5", "MX7080.3");
			}
			set
			{
				SetCachedValue("output_port", "cam5", value.ToString());
			}
		}
		#endregion

		#region 相机触发延时

		/// <summary>
		/// 相机一触发延时
		/// </summary>
		public int DelayOutput_Camera1
		{
			get
			{
				return GetPrivateProfileInt("output_delay", "cam1", 0, _iniPath);
			}
			set
			{
				SetCachedValue("output_delay", "cam1", value.ToString());
			}
		}

		/// <summary>
		/// 相机二触发延时
		/// </summary>
		public int DelayOutput_Camera2
		{
			get
			{
				return GetPrivateProfileInt("output_delay", "cam2", 0, _iniPath);
			}
			set
			{
				SetCachedValue("output_delay", "cam2", value.ToString());
			}
		}

		/// <summary>
		/// 相机三触发延时
		/// </summary>
		public int DelayOutput_Camera3
		{
			get
			{
				return GetPrivateProfileInt("output_delay", "cam3", 0, _iniPath);
			}
			set
			{
				SetCachedValue("output_delay", "cam3", value.ToString());
			}
		}

		/// <summary>
		/// 相机四触发延时
		/// </summary>
		public int DelayOutput_Camera4
		{
			get
			{
				return GetPrivateProfileInt("output_delay", "cam4", 0, _iniPath);
			}
			set
			{
				SetCachedValue("output_delay", "cam4", value.ToString());
			}
		}

		/// <summary>
		/// 相机五触发延时
		/// </summary>
		public int DelayOutput_Camera5
		{
			get
			{
				return GetPrivateProfileInt("output_delay", "cam5", 0, _iniPath);
			}
			set
			{
				SetCachedValue("output_delay", "cam5", value.ToString());
			}
		}



		#endregion

		#endregion

		#region 工位偏移量

		/// <summary>
		/// 相机一偏移量
		/// </summary>
		public int Offset_Camera1
		{
			get
			{
				return GetPrivateProfileInt("offset", "cam1", -1, _iniPath);
			}
			set
			{
				SetCachedValue("offset", "cam1", value.ToString());
			}
		}

		/// <summary>
		/// 相机二偏移量
		/// </summary>
		public int Offset_Camera2
		{
			get
			{
				return GetPrivateProfileInt("offset", "cam2", -1, _iniPath);
			}
			set
			{
				SetCachedValue("offset", "cam2", value.ToString());
			}
		}

		/// <summary>
		/// 相机三偏移量
		/// </summary>
		public int Offset_Camera3
		{
			get
			{
				return GetPrivateProfileInt("offset", "cam3", -1, _iniPath);
			}
			set
			{
				SetCachedValue("offset", "cam3", value.ToString());
			}
		}

		/// <summary>
		/// 相机四偏移量
		/// </summary>
		public int Offset_Camera4
		{
			get
			{
				return GetPrivateProfileInt("offset", "cam4", -1, _iniPath);
			}
			set
			{
				SetCachedValue("offset", "cam4", value.ToString());
			}
		}

		/// <summary>
		/// 相机五偏移量
		/// </summary>
		public int Offset_Camera5
		{
			get
			{
				return GetPrivateProfileInt("offset", "cam5", -1, _iniPath);
			}
			set
			{
				SetCachedValue("offset", "cam5", value.ToString());
			}
		}

		/// <summary>
		/// 
		/// </summary>
		public int Offset_Send
		{
			get
			{
				return GetPrivateProfileInt("offset", "send", -1, _iniPath);
			}
			set
			{
				SetCachedValue("offset", "send", value.ToString());
			}
		}

		//public int Offset_Send
		//{
		//	get
		//	{
		//		return GetPrivateProfileInt("offset", "send", -1, _iniPath);
		//	}
		//	set
		//	{
		//		SetCachedValue("offset", "send", value.ToString());
		//	}
		//}

		#endregion

		#region 拍照位记录
		/// <summary>
		/// 正面拍照位
		/// </summary>
		public double zhengPosition
		{
			get
			{
				return GetPrivateProfileDouble(_Config.CurCheckSpec + "_Position", "ZhengPosition", 100, _iniPath);
			}
			set
			{
				SetCachedValue(_Config.CurCheckSpec + "_Position", "ZhengPosition", value.ToString());
			}
		}
		/// <summary>
		/// 反面拍照位
		/// </summary>
		public double fanPosition
		{
			get
			{
				return GetPrivateProfileDouble(_Config.CurCheckSpec + "_Position", "FanPosition", 100, _iniPath);
			}
			set
			{
				SetCachedValue(_Config.CurCheckSpec + "_Position", "FanPosition", value.ToString());
			}
		}

		/// <summary>
		/// 反面拍照位
		/// </summary>
		public double roundPosition
		{
			get
			{
				return GetPrivateProfileDouble(_Config.CurCheckSpec + "_Position", "RoundPosition", 100, _iniPath);
			}
			set
			{
				SetCachedValue(_Config.CurCheckSpec + "_Position", "RoundPosition", value.ToString());
			}
		}
		#endregion

		#region 运动控制
		/// <summary>
		/// 红灯输入口
		/// </summary>
		public int Red_Light_Num
		{
			get
			{
				return GetPrivateProfileInt("control", "Red_Light_Num", 0, _iniPath);
			}
			set
			{
				SetCachedValue("control", "Red_Light_Num", value.ToString());
			}
		}

		/// <summary>
		/// 绿灯输入口
		/// </summary>
		public int Green_Light_Num
		{
			get
			{
				return GetPrivateProfileInt("control", "Green_Light_Num", 0, _iniPath);
			}
			set
			{
				SetCachedValue("control", "Green_Light_Num", value.ToString());
			}
		}

		/// <summary>
		/// 黄灯输入口
		/// </summary>
		public int Yellow_Light_Num
		{
			get
			{
				return GetPrivateProfileInt("control", "Yellow_Light_Num", 0, _iniPath);
			}
			set
			{
				SetCachedValue("control", "Yellow_Light_Num", value.ToString());
			}
		}

		/// <summary>
		/// 黄灯输入口
		/// </summary>
		public int Buzzer_Num
		{
			get
			{
				return GetPrivateProfileInt("control", "Buzzer_Num", 0, _iniPath);
			}
			set
			{
				SetCachedValue("control", "Buzzer_Num", value.ToString());
			}
		}
		#endregion

		#region 运行时轴初始化参数

		#region 轴0
		/// <summary>
		/// 脉冲当量
		/// </summary>
		public double axis0_Units
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis0_Units", 2500, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis0_Units", value.ToString());
			}
		}

		/// <summary>
		/// 轴速度
		/// </summary>
		public double axis0_Speed
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis0_Speed", 20, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis0_Speed", value.ToString());
			}
		}

		/// <summary>
		/// 加速度
		/// </summary>
		public double axis0_Accel
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis0_Accel", 10000, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis0_Accel", value.ToString());
			}
		}

		/// <summary>
		/// 减速度
		/// </summary>
		public double axis0_Decel
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis0_Decel", 10000, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis0_Decel", value.ToString());
			}
		}

		/// <summary>
		/// S曲线
		/// </summary>
		public double axis0_Sramp
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis0_Sramp", 0, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis0_Sramp", value.ToString());
			}
		}

		/// <summary>
		/// 起始速度
		/// </summary>
		public double axis0_Lspeed
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis0_Lspeed", 0, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis0_Lspeed", value.ToString());
			}
		}

		#endregion

		#region 轴1
		/// <summary>
		/// 脉冲当量
		/// </summary>
		public double axis1_Units
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis1_Units", 2500, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis1_Units", value.ToString());
			}
		}

		/// <summary>
		/// 轴速度
		/// </summary>
		public double axis1_Speed
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis1_Speed", 20, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis1_Speed", value.ToString());
			}
		}

		/// <summary>
		/// 加速度
		/// </summary>
		public double axis1_Accel
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis1_Accel", 10000, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis1_Accel", value.ToString());
			}
		}

		/// <summary>
		/// 减速度
		/// </summary>
		public double axis1_Decel
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis1_Decel", 10000, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis1_Decel", value.ToString());
			}
		}

		/// <summary>
		/// S曲线
		/// </summary>
		public double axis1_Sramp
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis1_Sramp", 0, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis1_Sramp", value.ToString());
			}
		}

		/// <summary>
		/// 起始速度
		/// </summary>
		public double axis1_Lspeed
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis1_Lspeed", 0, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis1_Lspeed", value.ToString());
			}
		}
		#endregion

		#region 轴2
		/// <summary>
		/// 脉冲当量
		/// </summary>
		public double axis2_Units
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis2_Units", 2500, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis2_Units", value.ToString());
			}
		}

		/// <summary>
		/// 轴速度
		/// </summary>
		public double axis2_Speed
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis2_Speed", 20, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis2_Speed", value.ToString());
			}
		}

		/// <summary>
		/// 加速度
		/// </summary>
		public double axis2_Accel
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis2_Accel", 10000, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis2_Accel", value.ToString());
			}
		}

		/// <summary>
		/// 减速度
		/// </summary>
		public double axis2_Decel
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis2_Decel", 10000, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis2_Decel", value.ToString());
			}
		}

		/// <summary>
		/// S曲线
		/// </summary>
		public double axis2_Sramp
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis2_Sramp", 0, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis2_Sramp", value.ToString());
			}
		}

		/// <summary>
		/// 起始速度
		/// </summary>
		public double axis2_Lspeed
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis2_Lspeed", 0, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis2_Lspeed", value.ToString());
			}
		}
		#endregion

		#endregion

		#region 初始化时轴初始化参数
		#region 正负极限、原点
		/// <summary>
		/// 轴0 原点
		/// </summary>
		public int axis0_Datum
		{
			get
			{
				return GetPrivateProfileInt("axis", "axis0_Datum", 4, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis0_Datum", value.ToString());
			}
		}

		/// <summary>
		/// 轴0 正限
		/// </summary>
		public int axis0_Fwd
		{
			get
			{
				return GetPrivateProfileInt("axis", "axis0_Fwd", 2, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis0_Fwd", value.ToString());
			}
		}

		/// <summary>
		/// 轴0 负限
		/// </summary>
		public int axis0_Rev
		{
			get
			{
				return GetPrivateProfileInt("axis", "axis0_Rev", 3, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis0_Rev", value.ToString());
			}
		}

		/// <summary>
		/// 轴1 原点
		/// </summary>
		public int axis1_Datum
		{
			get
			{
				return GetPrivateProfileInt("axis", "axis1_Datum", 7, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis1_Datum", value.ToString());
			}
		}

		/// <summary>
		/// 轴1 正限
		/// </summary>
		public int axis1_Fwd
		{
			get
			{
				return GetPrivateProfileInt("axis", "axis1_Fwd", 5, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis1_Fwd", value.ToString());
			}
		}

		/// <summary>
		/// 轴1 负限
		/// </summary>
		public int axis1_Rev
		{
			get
			{
				return GetPrivateProfileInt("axis", "axis1_Rev", 6, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis1_Rev", value.ToString());
			}
		}

		/// <summary>
		/// 轴2 原点
		/// </summary>
		public int axis2_Datum
		{
			get
			{
				return GetPrivateProfileInt("axis", "axis2_Datum", 13, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis2_Datum", value.ToString());
			}
		}

		/// <summary>
		/// 轴2 正限
		/// </summary>
		public int axis2_Fwd
		{
			get
			{
				return GetPrivateProfileInt("axis", "axis2_Fwd", 11, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis2_Fwd", value.ToString());
			}
		}

		/// <summary>
		/// 轴2 负限
		/// </summary>
		public int axis2_Rev
		{
			get
			{
				return GetPrivateProfileInt("axis", "axis2_Rev", 12, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "axis2_Rev", value.ToString());
			}
		}

		#endregion



		/// <summary>
		/// 爬行速度
		/// </summary>
		public double axis_CreepSpeed_Init
		{
			get
			{
				return GetPrivateProfileDouble("axis", "CreepSpeed_Init", 1, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "CreepSpeed_Init", value.ToString());
			}
		}

		/// <summary>
		/// 脉冲当量
		/// </summary>
		public double axis_Units_Init
		{
			get
			{
				return GetPrivateProfileDouble("axis", "Units_Init", 2500, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "Units_Init", value.ToString());
			}
		}

		/// <summary>
		/// 轴速度
		/// </summary>
		public double axis_Speed_Init
		{
			get
			{
				return GetPrivateProfileDouble("axis", "Speed_Init", 20, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "Speed_Init", value.ToString());
			}
		}

		/// <summary>
		/// 加速度
		/// </summary>
		public double axis_Accel_Init
		{
			get
			{
				return GetPrivateProfileDouble("axis", "Accel_Init", 10000, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "Accel_Init", value.ToString());
			}
		}

		/// <summary>
		/// 减速度
		/// </summary>
		public double axis_Decel_Init
		{
			get
			{
				return GetPrivateProfileDouble("axis", "Decel_Init", 10000, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "Decel_Init", value.ToString());
			}
		}

		/// <summary>
		/// 减速度
		/// </summary>
		public double axis_Sramp_Init
		{
			get
			{
				return GetPrivateProfileDouble("axis", "Sramp_Init", 0, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "Sramp_Init", value.ToString());
			}
		}

		/// <summary>
		/// 起始速度
		/// </summary>
		public double axis_Lspeed_Init
		{
			get
			{
				return GetPrivateProfileDouble("axis", "Lspeed_Init", 0, _iniPath);
			}
			set
			{
				SetCachedValue("axis", "Lspeed_Init", value.ToString());
			}
		}

		#endregion

		#region PLC


		public string PlcIP
		{
			get
			{
				return GetCachedValue("plc", "ip", "192.168.1.88");
			}
			set
			{
				SetCachedValue("plc", "ip", value.ToString());
			}
		}

		public int PlcPort
		{
			get
			{
				return GetPrivateProfileInt("plc", "port", 102, _iniPath);
			}
			set
			{
				SetCachedValue("plc", "port", value.ToString());
			}
		}

		/// <summary>PLC通讯类型: S7-1200(默认) / HCModbus</summary>
		public string PlcType
		{
			get
			{
				return GetCachedValue("plc", "type", "S7-1200");
			}
			set
			{
				SetCachedValue("plc", "type", value?.ToString() ?? "S7-1200");
			}
		}

		/// <summary>
		/// 心跳地址
		/// </summary>
		public string keepAlive
		{
			get
			{
				return GetCachedValue("plc_address", "keepAlive", "D10006");
			}
			set
			{
				SetCachedValue("plc_address", "keepAlive", value.ToString());
			}
		}

		#endregion

		#region 检测参数
		/// <summary>
		/// 异物最大面积
		/// </summary>
		public int minArea_Camera1
		{
			get
			{
				return GetPrivateProfileInt("params", "minArea_Camera1", 0, _iniPath);
			}
			set
			{
				SetCachedValue("params", "minArea_Camera1", value.ToString());
			}
		}

		/// <summary>
		/// 异物最大面积
		/// </summary>
		public int totalArea_Camera1
		{
			get
			{
				return GetPrivateProfileInt("params", "totalArea_Camera1", 0, _iniPath);
			}
			set
			{
				SetCachedValue("params", "totalArea_Camera1", value.ToString());
			}
		}


		/// <summary>
		/// 相机1标准字符
		/// </summary>
		public int Camera1StandChar
		{
			get
			{
				return GetPrivateProfileInt("params", "standNum_Camera4", 0, _iniPath);
			}
			set
			{
				SetCachedValue("params", "standNum_Camera4", value.ToString());
			}
		}

		/// <summary>
		/// 相机2标准字符
		/// </summary>
		public int Camera2StandChar
		{
			get
			{
				return GetPrivateProfileInt("params", "standNum_Camera5", 0, _iniPath);
			}
			set
			{
				SetCachedValue("params", "standNum_Camera5", value.ToString());
			}
		}


		public double Camera3Thresh
		{
			get
			{
				return GetPrivateProfileDouble("params", "thresh_Camera3", 200, _iniPath);
			}
			set
			{
				SetCachedValue("params", "thresh_Camera3", value.ToString());
			}
		}

		public double Camera3Maxval
		{
			get
			{
				return GetPrivateProfileDouble("params", "maxval_Camera3", 255, _iniPath);
			}
			set
			{
				SetCachedValue("params", "maxval_Camera3", value.ToString());
			}
		}

		public double Camera3LWRatio
		{
			get
			{
				return GetPrivateProfileDouble("params", "LW_ratio_Camera3", 1.2, _iniPath);
			}
			set
			{
				SetCachedValue("params", "LW_ratio_Camera3", value.ToString());
			}
		}

		public double Camera3RoundnessUp
		{
			get
			{
				return GetPrivateProfileDouble("params", "Roundness_Up_Camera3", 1.2, _iniPath);
			}
			set
			{
				SetCachedValue("params", "Roundness_Up_Camera3", value.ToString());
			}
		}

		public double Camera3RoundnessDown
		{
			get
			{
				return GetPrivateProfileDouble("params", "Roundness_Down_Camera3", 1.2, _iniPath);
			}
			set
			{
				SetCachedValue("params", "Roundness_Down_Camera3", value.ToString());
			}
		}

		public double Camera3PipeDiameter
		{
			get
			{
				return GetPrivateProfileDouble("params", "Pipe_Diameter_Camera3", 1, _iniPath);
			}
			set
			{
				SetCachedValue("params", "Pipe_Diameter_Camera3", value.ToString());
			}
		}

		public bool Camera5IFBaoGuan
		{
			get
			{
				return bool.Parse(GetCachedValue("params", "BaoGuan_Camera5", "True"));
			}
			set
			{
				SetCachedValue("params", "BaoGuan_Camera5", value.ToString());
			}
		}

		public bool Camera5IFSeBiao
		{
			get
			{
				return bool.Parse(GetCachedValue("params", "SeBiao_Camera5", "True"));
			}
			set
			{
				SetCachedValue("params", "SeBiao_Camera5", value.ToString());
			}
		}
		public bool Camera5IFWeiJianDuan
		{
			get
			{
				return bool.Parse(GetCachedValue("params", "WeiJianDuan_Camera5", "True"));
			}
			set
			{
				SetCachedValue("params", "WeiJianDuan_Camera5", value.ToString());
			}
		}
		public bool Camera5IFOcr
		{
			get
			{
				return bool.Parse(GetCachedValue("params", "Ocr_Camera5", "True"));
			}
			set
			{
				SetCachedValue("params", "Ocr_Camera5", value.ToString());
			}
		}

		public bool Camera5IFXieKou
		{
			get
			{
				return bool.Parse(GetCachedValue("params", "XieKou_Camera5", "True"));
			}
			set
			{
				SetCachedValue("params", "XieKou_Camera5", value.ToString());
			}
		}

		public bool Camera5IFPCode
		{
			get
			{
				return bool.Parse(GetCachedValue("params", "PCode_Camera5", "True"));
			}
			set
			{
				SetCachedValue("params", "PCode_Camera5", value.ToString());
			}
		}

		public string Standard_PCode
		{
			get
			{
				return GetCachedValue("params", "Standard_PCode_Camera5", "5");
			}
			set
			{
				SetCachedValue("params", "Standard_PCode_Camera5", value.ToString());
			}
		}
		#endregion

		#region 是否运行推理
		public bool IFRunCamera1
		{
			get
			{
				return bool.Parse(GetCachedValue("system", "IFRunCamera1", "True"));
			}
			set
			{
				SetCachedValue("system", "IFRunCamera1", value.ToString());
			}
		}

		public bool IFRunCamera2
		{
			get
			{
				return bool.Parse(GetCachedValue("system", "IFRunCamera2", "True"));
			}
			set
			{
				SetCachedValue("system", "IFRunCamera2", value.ToString());
			}
		}

		public bool IFRunCamera3
		{
			get
			{
				return bool.Parse(GetCachedValue("system", "IFRunCamera3", "True"));
			}
			set
			{
				SetCachedValue("system", "IFRunCamera3", value.ToString());
			}
		}

		public bool IFRunCamera4
		{
			get
			{
				return bool.Parse(GetCachedValue("system", "IFRunCamera4", "True"));
			}
			set
			{
				SetCachedValue("system", "IFRunCamera4", value.ToString());
			}
		}

		public bool IFRunCamera5
		{
			get
			{
				return bool.Parse(GetCachedValue("system", "IFRunCamera5", "True"));
			}
			set
			{
				SetCachedValue("system", "IFRunCamera5", value.ToString());
			}
		}

		#region 工位启用配置（程序启动时读一次，运行期不生效）
		public bool ActiveCam1
		{
			get
			{
				return bool.Parse(GetCachedValue("system", "ActiveCam1", "True"));
			}
		}
		public bool ActiveCam2
		{
			get
			{
				return bool.Parse(GetCachedValue("system", "ActiveCam2", "True"));
			}
		}
		public bool ActiveCam3
		{
			get
			{
				return bool.Parse(GetCachedValue("system", "ActiveCam3", "True"));
			}
		}
		public bool ActiveCam4
		{
			get
			{
				return bool.Parse(GetCachedValue("system", "ActiveCam4", "True"));
			}
		}
		public bool ActiveCam5
		{
			get
			{
				return bool.Parse(GetCachedValue("system", "ActiveCam5", "True"));
			}
		}
		#endregion

		public bool IFGroup
		{
			get
			{
				return bool.Parse(GetCachedValue("system", "IFGroup", "True"));
			}
			set
			{
				SetCachedValue("system", "IFGroup", value.ToString());
			}
		}


		#endregion

		#region AI模型相关参数

		#region 相机一
		public string ModelPath_Cam1
		{
			get
			{
				return GetCachedValue("AI_Params", "ModelPath_Cam1", @"D:\bin\AI\Cam1\model_trt_fp16.vimosln");
			}
			set
			{
				SetCachedValue("AI_Params", "ModelPath_Cam1", value.ToString());
			}
		}

		public bool UseGpu_Cam1
		{
			get
			{
				return bool.Parse(GetCachedValue("AI_Params", "UseGpu_Cam1", "True"));
			}
			set
			{
				SetCachedValue("AI_Params", "UseGpu_Cam1", value.ToString());
			}
		}

		public int DeviceId_Cam1
		{
			get
			{
				return GetPrivateProfileInt("AI_Params", "DeviceId_Cam1", 0, _iniPath);
			}
			set
			{
				SetCachedValue("AI_Params", "DeviceId_Cam1", value.ToString());
			}
		}

		public string ModelId_Segmentation_Cam1
		{
			get
			{
				return GetCachedValue("AI_Params", "ModelId_Segmentation_Cam1", "3");
			}
			set
			{
				SetCachedValue("AI_Params", "ModelId_Segmentation_Cam1", value.ToString());
			}
		}

		#endregion

		#region 相机二
		public string ModelPath_Cam2
		{
			get
			{
				return GetCachedValue("AI_Params", "ModelPath_Cam2", @"D:\bin\AI\Cam2\model_trt_fp16.vimosln");
			}
			set
			{
				SetCachedValue("AI_Params", "ModelPath_Cam2", value.ToString());
			}
		}

		public bool UseGpu_Cam2
		{
			get
			{
				return bool.Parse(GetCachedValue("AI_Params", "UseGpu_Cam2", "True"));
			}
			set
			{
				SetCachedValue("AI_Params", "UseGpu_Cam2", value.ToString());
			}
		}

		public int DeviceId_Cam2
		{
			get
			{
				return GetPrivateProfileInt("AI_Params", "DeviceId_Cam2", 0, _iniPath);
			}
			set
			{
				SetCachedValue("AI_Params", "DeviceId_Cam1", value.ToString());
			}
		}

		public string ModelId_Class_Cam2
		{
			get
			{
				return GetCachedValue("AI_Params", "ModelId_Class_Cam2", "5");
			}
			set
			{
				SetCachedValue("AI_Params", "ModelId_Class_Cam2", value.ToString());
			}
		}

		#endregion

		#region 相机四
		public string ModelPath_Cam4
		{
			get
			{
				return GetCachedValue("AI_Params", "ModelPath_Cam4", @"D:\bin\AI\Cam4\model_trt_fp16.vimosln");
			}
			set
			{
				SetCachedValue("AI_Params", "ModelPath_Cam4", value.ToString());
			}
		}

		public bool UseGpu_Cam4
		{
			get
			{
				return bool.Parse(GetCachedValue("AI_Params", "UseGpu_Cam4", "True"));
			}
			set
			{
				SetCachedValue("AI_Params", "UseGpu_Cam4", value.ToString());
			}
		}

		public int DeviceId_Cam4
		{
			get
			{
				return GetPrivateProfileInt("AI_Params", "DeviceId_Cam4", 0, _iniPath);
			}
			set
			{
				SetCachedValue("AI_Params", "DeviceId_Cam4", value.ToString());
			}
		}

		public string ModelId_Char_Cam4
		{
			get
			{
				return GetCachedValue("AI_Params", "ModelId_Char_Cam4", "3");
			}
			set
			{
				SetCachedValue("AI_Params", "ModelId_Char_Cam4", value.ToString());
			}
		}
		
		public string ModelId_Segmentation_Cam4
		{
			get
			{
				return GetCachedValue("AI_Params", "ModelId_Segmentation_Cam4", "2");
			}
			set
			{
				SetCachedValue("AI_Params", "ModelId_Segmentation_Cam4", value.ToString());
			}
		}

		#endregion

		#region 相机五
		public string ModelPath_Cam5
		{
			get
			{
				return GetCachedValue("AI_Params", "ModelPath_Cam5", @"D:\bin\AI\Cam5\model_trt_fp16.vimosln");
			}
			set
			{
				SetCachedValue("AI_Params", "ModelPath_Cam5", value.ToString());
			}
		}

		public bool UseGpu_Cam5
		{
			get
			{
				return bool.Parse(GetCachedValue("AI_Params", "UseGpu_Cam5", "True"));
			}
			set
			{
				SetCachedValue("AI_Params", "UseGpu_Cam5", value.ToString());
			}
		}

		public int DeviceId_Cam5
		{
			get
			{
				return GetPrivateProfileInt("AI_Params", "DeviceId_Cam5", 0, _iniPath);
			}
			set
			{
				SetCachedValue("AI_Params", "DeviceId_Cam5", value.ToString());
			}
		}





		public string ModelId_Char_Cam5
		{
			get
			{
				return GetCachedValue("AI_Params", "ModelId_Char_Cam5", "5");
			}
			set
			{
				SetCachedValue("AI_Params", "ModelId_Char_Cam5", value.ToString());
			}
		}
		public string ModelId_Char_PCode_Cam5
		{
			get
			{
				return GetCachedValue("AI_Params", "ModelId_Char_PCode_Cam5", "6");
			}
			set
			{
				SetCachedValue("AI_Params", "ModelId_Char_PCode_Cam5", value.ToString());
			}
		}

		public string ModelId_Class_Cam5
		{
			get
			{
				return GetCachedValue("AI_Params", "ModelId_Class_Cam5", "4");
			}
			set
			{
				SetCachedValue("AI_Params", "ModelId_Class_Cam5", value.ToString());
			}
		}

		public string ModelId_Segmentation_Cam5
		{
			get
			{
				return GetCachedValue("AI_Params", "ModelId_Segmentation_Cam5", "2");
			}
			set
			{
				SetCachedValue("AI_Params", "ModelId_Segmentation_Cam5", value.ToString());
			}
		}

		public string ModelId_ColorSegmentation_Cam5
		{
			get
			{
				return GetCachedValue("AI_Params", "ModelId_ColorSegmentation_Cam5", "3");
			}
			set
			{
				SetCachedValue("AI_Params", "ModelId_ColorSegmentation_Cam5", value.ToString());
			}
		}

		#endregion


		#endregion
	}
}
