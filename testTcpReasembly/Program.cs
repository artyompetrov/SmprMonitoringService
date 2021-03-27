/*using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PcapDotNet.Core;
using PcapDotNet.Packets;
using PcapDotNet.Packets.IpV4;
using PcapDotNet.Packets.Transport;
using SMPRmonitoring;
using TcpReconstructor;

namespace testTcpReasembly
{
    class Program
    {
        private static readonly Dictionary<long, Destination> _destinationDictionary = new Dictionary<long, Destination>();

        static Dictionary<string, TcpRecon> _tcpConnections = new Dictionary<string, TcpRecon>();

        private static readonly DateTime _unixOrigin = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static int conn = 0;

        static void Main(string[] args)
        {


            var ipString = "172.24.219.245";
            ushort port = 4712;


            var destination = new Destination("Тестовое", port, 1, 1);

            var ip = new Ip("ipString");

            long ipPort = ip.AsUint * 65536 + 4712;
            _destinationDictionary.Add(ipPort, destination);


            PacketDevice selectedDevice;

            if (true)
            {
                selectedDevice = new OfflinePacketDevice(@"D:\Sources\SmprMonitoring\SmprMonitoringService\test.pcap");
            }
            else
            {
                var allDevices = LivePacketDevice.AllLocalMachine;

                var deviceNumber = 0;
                foreach (var device in allDevices)
                {
                    Console.WriteLine($"{deviceNumber} {device.Description}");
                    deviceNumber++;
                }

                selectedDevice = allDevices[int.Parse(Console.ReadLine())];
            }


            var filterString = $"ip src host {ipString} tcp src port {port}";

            using (var communicator = selectedDevice.Open(65536, PacketDeviceOpenAttributes.Promiscuous, 1000))
            {

                using (var filter = communicator.CreateFilter(filterString))
                {
                    communicator.SetFilter(filter);
                }
                communicator.ReceivePackets(0, PacketHandler);
            }

            Console.WriteLine("DONE");
            Console.ReadLine();

        }

        private static void PacketHandler(Packet packet)
        {

            var ip = packet.Ethernet.IpV4;

            Datagram datagram;
            ushort port;

            switch (ip.Protocol)
            {
                case IpV4Protocol.Udp:
                {
                    var udpDatagram = ip.Udp;
                    datagram = udpDatagram.Payload;
                    port = udpDatagram.DestinationPort;
                    break;
                }

                case IpV4Protocol.Tcp:
                {
                    var tcpDatagram = ip.Tcp;
                    datagram = tcpDatagram.Payload;
                    port = tcpDatagram.SourcePort;
                    break;
                }

                default:
                    throw new Exception($"received packet with unknown protocol: {ip.Protocol}");
            }

            long ipPort = ip.Source.ToValue() * 65536 + port;
            _destinationDictionary[ipPort].ProcessDatagram(datagram, (packet.Timestamp.ToUniversalTime() - _unixOrigin).TotalMilliseconds);

            
            /*
            IpV4Datagram ip = packet.Ethernet.IpV4;

            if (ip.Protocol == IpV4Protocol.Tcp)
            {
                TcpDatagram tcp = ip.Tcp;

                var connection = $"data/{ip.Source}p{tcp.SourcePort}t{ip.Destination}p{tcp.DestinationPort}.data";


                if (!_tcpConnections.ContainsKey(connection))
                {
                    conn++;
                    var reconstructor = new TcpRecon(conn);
                    _tcpConnections.Add(connection, reconstructor);
                    
                    reconstructor.ReassemblePacket(tcp);
                    


                }
                else
                {
                    _tcpConnections[connection].ReassemblePacket(tcp);
                }

            

            }
            //

        }
    }
}
*/