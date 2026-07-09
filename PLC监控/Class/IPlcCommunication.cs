using System;

namespace PLC调试.Class
{
    /// <summary>PLC连接状态变化委托</summary>
    public delegate void PlcConnectStateHandler(bool state, string error);

    /// <summary>PLC计数更新委托</summary>
    public delegate void PlcCountHandler(uint count1, uint count2, uint count3, uint count4, uint count5);

    /// <summary>
    /// PLC通讯统一接口 —— 支持 S7-1200 / HCModbus 等多种PLC类型
    /// </summary>
    public interface IPlcCommunication : IDisposable
    {
        /// <summary>PLC连接状态（true=已连接）</summary>
        bool modbusState { get; }

        /// <summary>连接PLC</summary>
        bool ConnectModbus();

        /// <summary>写入3个产品的检测结果</summary>
        bool WriteResult(bool result1, bool result2, bool result3);

        /// <summary>启动运行模式</summary>
        void RuningMethod();

        /// <summary>清零PLC计数</summary>
        void ClearCount();

        /// <summary>断线重连</summary>
        void Reconnect();

        /// <summary>连接状态变化事件</summary>
        event PlcConnectStateHandler EventConnectState;

        /// <summary>PLC计数更新事件</summary>
        event PlcCountHandler EventCount;
    }
}
