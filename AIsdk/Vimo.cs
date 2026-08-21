using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;
using OpenCvSharp;
using SmartMore.ViMo;

namespace AIsdk
{
	public class Vimo
	{
		private const int ERROR_OK = 0;
		private const int ERROR_FAILED = -1;
		public string ErrorInfo = "";
		private string modelsPath = "";
		private string modelID = "";
		private bool useGpu = false;
		private int deviceId = 0;
		Stopwatch stopwatch = new Stopwatch();
		public ModuleType moduleType { get; set; }

		/// <summary>原生级致命异常：进程内存已不可信，记录后必须重新抛出（由全局处理器转储退出），绝不能吞掉继续跑</summary>
		private static bool IsFatal(Exception ex)
		{
			if (ex == null) return false;
			for (Exception e = ex; e != null; e = e.InnerException)
			{
				if (e is AccessViolationException ||
					e is System.Runtime.InteropServices.SEHException ||
					e is OutOfMemoryException ||
					e is StackOverflowException)
					return true;
			}
			return false;
		}

		/// <summary>崩溃兜底日志：直接同步写文件（AIsdk 不引用 CommonLib，无法使用 FastLogger）</summary>
		private static void WriteFatalLog(Exception ex)
		{
			try
			{
				string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
				if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
				string file = Path.Combine(dir, "crash_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
				string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] [CRASH] [ViMo原生崩溃] "
					+ ex.GetType().Name + " | " + ex.Message + "\r\n堆栈: " + (ex.StackTrace ?? "");
				File.AppendAllText(file, line + Environment.NewLine);
			}
			catch { }
		}

		IPipelines pipelines1;
		Solution solution;
		IOcrModule module;
		ISegmentationModule module_segmentation;
		IClassificationModule module_class;
		public Vimo()
		{
		}

		public Vimo(string modelPath, bool useGpu, int deviceId, string moduleId)
		{
			this.modelsPath = modelPath;
			this.useGpu = useGpu;
			this.deviceId = deviceId;
			this.modelID = moduleId;
		}



		[HandleProcessCorruptedStateExceptions] // 允许捕获 ViMo 原生层的访问违例等致命异常并留痕
		public int Init(string modelPath, bool usegup, int deviceid, string modelid)
		{
			try
			{
				modelsPath = modelPath;
				useGpu = usegup;
				deviceId = deviceid;
				modelID = modelid;
				solution = new Solution();
				solution.LoadFromFile(modelsPath);
				pipelines1 = solution.CreatePipelines(modelID, useGpu, deviceId);
				moduleType = solution.GetModuleInfo(modelID).Type;
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				if (IsFatal(ex)) { WriteFatalLog(ex); throw; } // 原生致命异常：留痕后重新抛出，走全局处理器转储退出
				ErrorInfo = ex.ToString();
				return ERROR_FAILED;
			}
		}

		[HandleProcessCorruptedStateExceptions]
		public int Init_Segmentation(string modelPath, bool usegup, int deviceid, string modelid)
		{
			try
			{
				modelsPath = modelPath;
				useGpu = usegup;
				deviceId = deviceid;
				modelID = modelid;
				solution = new Solution();
				solution.LoadFromFile(modelsPath);
				module_segmentation = solution.CreateModule<ISegmentationModule>(modelID, useGpu, deviceId);
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				if (IsFatal(ex)) { WriteFatalLog(ex); throw; } // 原生致命异常：留痕后重新抛出，走全局处理器转储退出
				ErrorInfo = ex.ToString();
				return ERROR_FAILED;
			}
		}

		[HandleProcessCorruptedStateExceptions]
		public int Init_OrderOcr(string modelPath, bool usegup, int deviceid, string modelid)
		{
			try
			{
				modelsPath = modelPath;
				useGpu = usegup;
				deviceId = deviceid;
				modelID = modelid;
				solution = new Solution();
				solution.LoadFromFile(modelsPath);
				module = solution.CreateModule<IOcrModule>(modelID, useGpu, deviceId);
				var ocrParams = module.Params;
				ocrParams.Order = OutputOrder.LeftToRight;
				module.Params = ocrParams;
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				if (IsFatal(ex)) { WriteFatalLog(ex); throw; } // 原生致命异常：留痕后重新抛出，走全局处理器转储退出
				ErrorInfo = ex.ToString();
				return ERROR_FAILED;
			}
		}

		[HandleProcessCorruptedStateExceptions]
		public int Init_Class(string modelPath, bool usegup, int deviceid, string modelid)
		{
			try
			{
				modelsPath = modelPath;
				useGpu = usegup;
				deviceId = deviceid;
				modelID = modelid;
				solution = new Solution();
				solution.LoadFromFile(modelsPath);
				module_class = solution.CreateModule<IClassificationModule>(modelID, useGpu, deviceId);
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				if (IsFatal(ex)) { WriteFatalLog(ex); throw; } // 原生致命异常：留痕后重新抛出，走全局处理器转储退出
				ErrorInfo = ex.ToString();
				return ERROR_FAILED;
			}
		}

		/// <summary>
		/// SDK Run接口
		/// </summary>
		[HandleProcessCorruptedStateExceptions]
		public int Run(Mat image, out ResponseList<DetectionResponse> results)
		{
			try
			{
				var req = new Request(image);
				stopwatch.Restart();
				pipelines1.Run(req, out results);
				stopwatch.Stop();
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				if (IsFatal(ex)) { WriteFatalLog(ex); throw; } // 原生致命异常：留痕后重新抛出，走全局处理器转储退出
				results = null;
				ErrorInfo = ex.ToString();
				return ERROR_FAILED;
			}
		}

		/// <summary>
		/// SDK Run接口
		/// </summary>
		[HandleProcessCorruptedStateExceptions]
		public int Run(Mat image, out ResponseList<SegmentationResponse> results)
		{
			try
			{
				var req = new Request(image);
				stopwatch.Restart();
				pipelines1.Run(req, out results);
				stopwatch.Stop();
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				if (IsFatal(ex)) { WriteFatalLog(ex); throw; } // 原生致命异常：留痕后重新抛出，走全局处理器转储退出
				results = null;
				ErrorInfo = ex.ToString();
				return ERROR_FAILED;
			}
		}

		/// <summary>
		/// SDK Run接口
		/// </summary>
		[HandleProcessCorruptedStateExceptions]
		public int Run(Mat image, out ResponseList<ClassificationResponse> results)
		{
			try
			{
				var req = new Request(image);
				stopwatch.Restart();
				pipelines1.Run(req, out results);
				stopwatch.Stop();
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				if (IsFatal(ex)) { WriteFatalLog(ex); throw; } // 原生致命异常：留痕后重新抛出，走全局处理器转储退出
				results = null;
				ErrorInfo = ex.ToString();
				return ERROR_FAILED;
			}
		}

		[HandleProcessCorruptedStateExceptions]
		public int Run(Mat image, out ResponseList<OcrResponse> results)
		{
			try
			{
				var req = new Request(image);
				stopwatch.Restart();
				pipelines1.Run(req, out results);
				stopwatch.Stop();
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				if (IsFatal(ex)) { WriteFatalLog(ex); throw; } // 原生致命异常：留痕后重新抛出，走全局处理器转储退出
				results = null;
				ErrorInfo = ex.ToString();
				return ERROR_FAILED;
			}
		}

		[HandleProcessCorruptedStateExceptions]
		public int Run_OrderOcr(Mat image, out OcrResponse results)
		{
			try
			{
				var req = new Request(image);
				stopwatch.Restart();
				module.Run(req, out results);
				stopwatch.Stop();
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				if (IsFatal(ex)) { WriteFatalLog(ex); throw; } // 原生致命异常：留痕后重新抛出，走全局处理器转储退出
				results = null;
				ErrorInfo = ex.ToString();
				return ERROR_FAILED;
			}
		}

		[HandleProcessCorruptedStateExceptions]
		public int Run_OrderOcr(Mat image, Rect roi, out OcrResponse results)
		{
			try
			{
				var req = new Request(image, roi);
				stopwatch.Restart();
				module.Run(req, out results);
				stopwatch.Stop();
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				if (IsFatal(ex)) { WriteFatalLog(ex); throw; } // 原生致命异常：留痕后重新抛出，走全局处理器转储退出
				results = null;
				ErrorInfo = ex.ToString();
				return ERROR_FAILED;
			}
		}

		[HandleProcessCorruptedStateExceptions]
		public int Run_Segmentation(Mat image, out SegmentationResponse results)
		{
			try
			{
				var req = new Request(image);
				stopwatch.Restart();
				module_segmentation.Run(req, out results);
				stopwatch.Stop();
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				if (IsFatal(ex)) { WriteFatalLog(ex); throw; } // 原生致命异常：留痕后重新抛出，走全局处理器转储退出
				results = null;
				ErrorInfo = ex.ToString();
				return ERROR_FAILED;
			}
		}
		[HandleProcessCorruptedStateExceptions]
		public int Run_Segmentation(Mat image,Rect roi, out SegmentationResponse results)
		{
			try
			{
				var req = new Request(image, roi);
				stopwatch.Restart();
				module_segmentation.Run(req, out results);
				stopwatch.Stop();
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				if (IsFatal(ex)) { WriteFatalLog(ex); throw; } // 原生致命异常：留痕后重新抛出，走全局处理器转储退出
				results = null;
				ErrorInfo = ex.ToString();
				return ERROR_FAILED;
			}
		}

		[HandleProcessCorruptedStateExceptions]
		public int Run_Class(Mat image, Rect roi, out ClassificationResponse results)
		{
			try
			{
				var req = new Request(image, roi);
				stopwatch.Restart();
				module_class.Run(req, out results);
				stopwatch.Stop();
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				if (IsFatal(ex)) { WriteFatalLog(ex); throw; } // 原生致命异常：留痕后重新抛出，走全局处理器转储退出
				results = null;
				ErrorInfo = ex.ToString();
				return ERROR_FAILED;
			}
		}

		/// <summary>
		/// 释放AI模型资源（GPU显存等），防止程序关闭时资源未释放导致卡死
		/// </summary>
		public void Dispose()
		{
			try
			{
				if (pipelines1 is IDisposable d1) d1.Dispose();
				if (module is IDisposable d2) d2.Dispose();
				if (module_segmentation is IDisposable d3) d3.Dispose();
				if (module_class is IDisposable d4) d4.Dispose();
				if (solution is IDisposable d5) d5.Dispose();
				pipelines1 = null;
				module = null;
				module_segmentation = null;
				module_class = null;
				solution = null;
			}
			catch { }
		}

		/// <summary>
		/// Mat格式转为Bitmap
		/// </summary>
		/// <param name="mat"></param>
		/// <returns></returns>
		private Bitmap Visualize(Mat mat)
		{
			Bitmap bitmap = new Bitmap(mat.Cols, mat.Rows, (int)mat.Step(), PixelFormat.Format24bppRgb, mat.Data);
			return bitmap;
		}
	}
}
