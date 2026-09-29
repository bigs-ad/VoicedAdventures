using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
class Launcher {
    [STAThread] static void Main() {
        string dll=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"QuestVoiceStreaming.dll");
        try {
            Process.Start(new ProcessStartInfo("dotnet","\""+dll+"\"") { UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory });
        } catch { MessageBox.Show("无法启动测试程序，请确认 .NET 9 桌面运行时已安装。","冒险有声"); }
    }
}
