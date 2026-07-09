using System;
using System.Diagnostics;
using XL.Tool;

namespace PLC调试.Class
{
    /// <summary>
    /// HCModbusClass 适配器 —— 将 HCModbusClass 包装为 IPlcCommunication 接口
    /// 保持 HCModbusClass 自身不变，确保相机设置窗体等现有代码不受影响
    /// </summary>
    public class HCModbusAdapter : IPlcCommunication
    {
        private readonly HCModbusClass _inner;
        private volatile bool _disposed;
        private XLToolClass _toolClass = new XLToolClass();

        public HCModbusAdapter()
        {
            _inner = new HCModbusClass();
            // 转发内部事件到接口事件
            _inner.EventConnectState += (state, error) => EventConnectState?.Invoke(state, error);
            _inner.EventCount += (c1, c2, c3, c4, c5) => EventCount?.Invoke(c1, c2, c3, c4, c5);
        }

        /// <summary>获取内部 HCModbusClass 实例（相机设置窗体等需要直接引用）</summary>
        public HCModbusClass Inner => _inner;

        public bool modbusState => _inner.modbusState;

        public event PlcConnectStateHandler EventConnectState;
        public event PlcCountHandler EventCount;

        public bool ConnectModbus()
        {
            try { return _inner.ConnectModbus(); }
            catch (Exception ex)
            {
                _toolClass.SaveLog($"[HCModbusAdapter] ConnectModbus异常: {ex.Message}");
                EventConnectState?.Invoke(false, $"连接失败: {ex.Message}");
                return false;
            }
        }

        public bool WriteResult(bool result1, bool result2, bool result3)
        {
            if (_disposed || !_inner.modbusState) return false;
            try
            {
                // HCModbusClass 使用 WriteResult 方法（如果存在）或底层 modbusTcp.Write
                // 尝试通过反射或已知API调用
                return _inner.WriteResult(result1, result2, result3);
            }
            catch (Exception ex)
            {
                _toolClass.SaveLog($"[HCModbusAdapter] WriteResult异常: {ex.Message}");
                return false;
            }
        }

        public void RuningMethod()
        {
            if (_disposed) return;
            try { _inner.RuningMethod(); }
            catch (Exception ex) { _toolClass.SaveLog($"[HCModbusAdapter] RuningMethod异常: {ex.Message}"); }
        }

        public void ClearCount()
        {
            if (_disposed) return;
            try { _inner.ClearCount(); }
            catch (Exception ex) { _toolClass.SaveLog($"[HCModbusAdapter] ClearCount异常: {ex.Message}"); }
        }

        public void Reconnect()
        {
            if (_disposed) return;
            try { _inner.Reconnect(); }
            catch (Exception ex) { _toolClass.SaveLog($"[HCModbusAdapter] Reconnect异常: {ex.Message}"); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _inner?.CloseModbus(); }
            catch (Exception ex) { _toolClass.SaveLog($"[HCModbusAdapter] Dispose异常: {ex.Message}"); }
        }
    }
}
