using System.ServiceProcess;
using System.Threading;

namespace SmprMonitoringService
{
    static class Program
    {
        /// <summary>
        /// Главная точка входа для приложения.
        /// </summary>
        static void Main()
        {
#if DEBUG
            SmprMonitoringService s = new SmprMonitoringService();
            s.OnDebug();
            Thread.Sleep(10000);
#else
            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                new SmprMonitoringService()
            };
            ServiceBase.Run(ServicesToRun);
#endif
        }
    }
}
