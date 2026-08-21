using System;

namespace PLC调试.Class
{
    /// <summary>PLC连接状态变化委托</summary>
    public delegate void PlcConnectStateHandler(bool state, string error);

    /// <summary>PLC计数更新委托</summary>
    public delegate void PlcCountHandler(uint count1, uint count2, uint count3, uint count4, uint count5);

    /// <summary>设备运行模式变化委托（isAuto=true 表示自动，即 PLC 寄存器值=4；rawValue=PLC 原始值）</summary>
    public delegate void PlcDeviceModeHandler(bool isAuto, short rawValue);

    /// <summary>气缸禁用状态变化委托（disabled=true 表示禁用）</summary>
    public delegate void PlcCylinderStateHandler(bool disabled);

    /// <summary>
    /// PLC通讯统一接口 —— 支持 S7-1200 / HCModbus 等多种PLC类型
    /// </summary>
    public interface IPlcCommunication : IDisposable
    {
        /// <summary>PLC连接状态（true=已连接）</summary>
        bool modbusState { get; }

        /// <summary>设备是否处于自动模式（PLC寄存器值==4 为自动）。首次成功读取前返回 false（按手动处理）</summary>
        bool IsAutoMode { get; }

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

        /// <summary>设备运行模式变化事件（首读成功时触发一次 + 值变化时触发；读失败不触发）</summary>
        event PlcDeviceModeHandler EventDeviceMode;

        /// <summary>气缸禁用状态变化事件（首读成功时触发一次 + 值变化时触发；读失败不触发）</summary>
        event PlcCylinderStateHandler EventCylinderState;
    }
}
