using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SmprMonitoringService
{

    public partial class SmprMonitoringService : ServiceBase
    {

        static bool logFileBusy = false;


        public SmprMonitoringService()
        {

            InitializeComponent();
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(UnhandledExceptionHandler);
        }

        private void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject.GetType() != typeof(LogException))
            {
                Log(e.ExceptionObject.ToString() + Environment.NewLine + "closing with error");
#if !DEBUG
                Environment.Exit(-1);
#endif
            }
        }

#if DEBUG
        public void OnDebug()
        {
            OnStart(null);
        }
#endif

        internal static void Log(string message, bool newLine = false)
        {

            while (logFileBusy)
            {
                Thread.Sleep(1);
            }
            logFileBusy = true;

            try
            {
                using (var sw = new StreamWriter(AppDomain.CurrentDomain.BaseDirectory + "log.txt", true, System.Text.Encoding.UTF8))
                {
                    sw.WriteLine((newLine ? Environment.NewLine : "") + DateTime.Now + " " + message);
                }
            }
            catch (Exception ex)
            {
                throw new LogException("Original exception: " + ex.ToString());
            }

            logFileBusy = false;

        }

        protected override void OnStart(string[] args)
        {
            Log("starting service", true);
            Thread t = new Thread(SmprMonitoringWorker.Start);
            t.Priority = ThreadPriority.Normal;
            t.Start();
        }

        protected override void OnStop()
        {
            
            SmprMonitoringWorker.Stop();
            Thread.Sleep(1000);
            Log("service stopped");
        }
    }
}
