using System;
using XL.UsbDog;

namespace CameraSwitchTool.Services
{
    /// <summary>
    /// 加密狗检测（XL.UsbDog，与主程序同一个狗 SDK）。
    /// 工具启动必须插狗；运行中拔狗锁定保存与重启。
    /// FindUsbDog 枚举 HID 设备，任何异常一律按"未检测到"处理（fail-closed）。
    /// </summary>
    public static class DogGuardService
    {
        private static XLUsbDogClass _dog;
        private static readonly object LockObj = new object();
        private static bool _ctorErrorLogged;
        private static bool _findErrorLogged;

        private static XLUsbDogClass Dog
        {
            get
            {
                if (_dog == null)
                {
                    lock (LockObj)
                    {
                        if (_dog == null)
                        {
                            try
                            {
                                _dog = new XLUsbDogClass();
                                _ctorErrorLogged = false;
                            }
                            catch (Exception ex)
                            {
                                // 构造失败仅记一次日志，避免定时轮询刷屏
                                if (!_ctorErrorLogged)
                                {
                                    _ctorErrorLogged = true;
                                    OperationLog.Error("加密狗组件初始化异常: " + ex.Message);
                                }
                                throw;
                            }
                        }
                    }
                }
                return _dog;
            }
        }

        public static bool IsDogPresent()
        {
            try
            {
                bool present = Dog.FindUsbDog();
                _findErrorLogged = false;
                return present;
            }
            catch (Exception ex)
            {
                if (!_findErrorLogged)
                {
                    _findErrorLogged = true;
                    OperationLog.Error("加密狗检测异常: " + ex.Message);
                }
                return false;
            }
        }
    }
}
