using System.ComponentModel;
using System.Linq;
using System.Net;

namespace SmprMonitoring
{
    public class Settings
    {
        public uint RequestDepth { get; set; } = 10;
        public uint IgnoreChannelLostSeconds { get; set; } = 0;
        public uint AveragingPeriod { get; set; } = 60;
        
        public string CheckSettings()
        {
            bool[] b = new bool[4];

            foreach (var dest in Destinations)
            {
                if (Destinations.Count(d => d.Port == dest.Port) > 1) b[0] = true;
                if (Destinations.Count(d => d.IOAPrefix == dest.IOAPrefix) > 1) b[1] = true;
                if (Destinations.Count(d => d.Name == dest.Name) > 1) b[2] = true;
            }

            IPAddress ip;
            foreach (var ipString in AllowedIPAddresses)
            {
                if (IPAddress.TryParse(ipString, out ip) == false)
                {
                    b[3] = true;
                    break;
                }
            }

            string result = "";
            if (b[0]) result += "Несколько направлений с одним портом.\n";
            if (b[1]) result += "Несколько направлений с одним префиксом.\n";
            if (b[2]) result += "Несколько направлений с одним названием.\n";
            if (b[3]) result += "Неверный формат IP-адреса.\n";

            if (result == "") return null;
            else return result;
        }

        public string DeviceName = "";

        public BindingList<Destination> Destinations { get; set; } = new BindingList<Destination>();

        public BindingList<string> AllowedIPAddresses { get; set; } = new BindingList<string>();
    }    
}
