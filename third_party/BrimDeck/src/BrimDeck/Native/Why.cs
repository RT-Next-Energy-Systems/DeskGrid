using BrimDeck.Core;

namespace BrimDeck.Native;

// Why a reading is missing, worded in the interface language when the reading is taken.
internal static class Why
{
    public static string Waiting => Loc.T("等待第二次讀數", "waiting for a second reading");
    public static string NoCounters => Loc.T("系統效能計數器不可用", "the system's performance counters are unavailable");
    public static string NoFrequency => Loc.T("處理器未報告頻率", "the processor reports no frequency");
    public static string NoMemory => Loc.T("無法讀取記憶體狀態", "the memory status could not be read");
    public static string NoNetwork => Loc.T("無法讀取網絡卡", "the network adapters could not be read");
    public static string NoAdapter => Loc.T("未找到物理網絡卡", "no physical network adapter was found");
    public static string NoGpu => Loc.T("顯示卡驅動未提供 GPU 計數器", "the graphics driver provides no GPU counters");
    public static string NoGpuMemory => Loc.T("顯示卡驅動未報告視訊記憶體佔用", "the graphics driver reports no memory in use");
    public static string NoProcesses => Loc.T("無法讀取程序列表", "the process list could not be read");
}
