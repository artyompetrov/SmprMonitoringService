using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Documents;

namespace SMPRmonitoring
{
    public class Settings
    {
#if DEBUG
        public string DebugPcapFile { get; set; }
        public string DebugLocalIP { get; set; }
#endif

        public string DeviceName { get; set; } = "";
        public uint RequestDepth { get; set; } = 10;
        public uint IgnoreChannelLostSeconds { get; set; } = 0;
        public uint AveragingPeriod { get; set; } = 60;
        public int RTUID { get; set; }
        public uint Iec104Port { get; set; } = 2404;
        public BindingList<Destination> Destinations { get; set; } = new BindingList<Destination>();
        public BindingList<Ip> AllowedIPAddresses { get; set; } = new BindingList<Ip>();

        public string CheckSettings()
        {
            bool[] b = new bool[4];


            var ipAddresses = new List<Ip>();
            
            foreach (var dest in Destinations)
            {
                if (Destinations.Count(d => d.IOAPrefix == dest.IOAPrefix) > 1) b[1] = true;
                if (Destinations.Count(d => d.Name == dest.Name) > 1) b[2] = true;

                ipAddresses.AddRange(dest.IpAddresses);
            }

            foreach (var ipAddress in AllowedIPAddresses)
            {
                if (AllowedIPAddresses.Count(ip => Equals(ip, ipAddress)) > 1) b[3] = true;
            }

            foreach (var ipAddress in ipAddresses)
            {
                if (ipAddresses.Count(ip => Equals(ip, ipAddress)) > 1)
                    b[3] = true;
            }

            string result = "";
            if (b[1]) result += $"Несколько направлений с одним префиксом.{Environment.NewLine}";
            if (b[2]) result += $"Несколько направлений с одним названием.{Environment.NewLine}";
            if (b[3]) result += $"IP-адреса не должны дублироваться.{Environment.NewLine}";

            if (result == "") return null;

            return result;
        }

    }    

}
