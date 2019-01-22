using lib60870;
using lib60870.CS101;
using lib60870.CS104;
using Newtonsoft.Json;
using PcapDotNet.Core;
using PcapDotNet.Packets;
using PcapDotNet.Packets.IpV4;
using PcapDotNet.Packets.Transport;
using SmprMonitoring;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Timer = System.Timers.Timer;

namespace SmprMonitoringService
{
    static class SmprMonitoringWorker
    {
        static DateTime _unixOrigin = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        static Timer _timer1 = new Timer(100);
        static uint _elapsedTime = 0;
        static uint _nextPeriodTime = 0;
        static Server _server = new Server();
        static ApplicationLayerParameters _alp = new ApplicationLayerParameters();
        static QualityDescriptor _qd = new QualityDescriptor();
        static Settings _settings;
        static PacketCommunicator _communicator;
        static bool _started = false;
        static List<IPAddress> _allowedIpAddresses = new List<IPAddress>();
        static bool _firstPeriodPassed = false;
        static ASDU _asdu = new ASDU(_alp, TypeID.M_ME_TF_1, CauseOfTransmission.SPONTANEOUS, false, false, 0, 0, false);


        internal static void Start()
        {
            if (_started) return;
            _started = true;

            using (StreamReader file = File.OpenText(AppDomain.CurrentDomain.BaseDirectory + "settings.json"))
            {
                JsonSerializer serializer = new JsonSerializer();
                _settings = (Settings)serializer.Deserialize(file, typeof(Settings));
            }

            string checkSettings = _settings.CheckSettings();
            if (checkSettings != null) throw new Exception("Wrong settings: " + checkSettings);

            foreach (var ipString in _settings.AllowedIPAddresses)
            {
                IPAddress ip = IPAddress.Parse(ipString);

                _allowedIpAddresses.Add(ip);
            }

            _timer1.Elapsed += Timer1Elapsed;

            IList<LivePacketDevice> allDevices = LivePacketDevice.AllLocalMachine;

            if (allDevices.Count == 0)
            {
                throw new Exception("No interfaces found! Make sure WinPcap is installed");

            }


            PacketDevice selectedDevice = null;
            foreach (var device in allDevices)
            {
                if (device.Name == _settings.DeviceName)
                {
                    selectedDevice = device;
                    break;
                }
            }

            if (selectedDevice == null) throw new Exception("Device specified in settings file not found");


            SmprMonitoringService.Log("Selected device: " + selectedDevice.Description);

            string localIpAdress = "";
            bool ipFound = false;
            for (int i = 0; i < selectedDevice.Addresses.Count; i++)
            {
                if (selectedDevice.Addresses[i].Address.Family == SocketAddressFamily.Internet)
                {
                    if (ipFound) throw new Exception("Devices with two or more IPv4 adresses are not supported");
                    ipFound = true;
                    localIpAdress = selectedDevice.Addresses[i].Address.ToString().Remove(0, 9);

                }
            }


            _server.ServerMode = ServerMode.SINGLE_REDUNDANCY_GROUP;
            _server.SetConnectionRequestHandler(OnConnectionRequest, null);
            _server.SetConnectionEventHandler(OnConnectionEvent, null);
            _server.Start();


            if (_settings.Destinations.Count > 0)
            {
                uint startTime = (uint)(DateTime.UtcNow - _unixOrigin).TotalSeconds - _settings.RequestDepth + 1;

                foreach (var dest in _settings.Destinations)
                {
                    dest.LastRequestedRecievedTime = startTime;
                }

                _nextPeriodTime = (uint)(DateTime.UtcNow - _unixOrigin).TotalSeconds + _settings.AveragingPeriod;
                _elapsedTime = (uint)(DateTime.UtcNow - _unixOrigin).TotalSeconds + _settings.RequestDepth;

                _timer1.Start();

                using (_communicator = selectedDevice.Open(65536, PacketDeviceOpenAttributes.Promiscuous, 1000))
                {
                    if (_communicator.DataLink.Kind != DataLinkKind.Ethernet)
                    {
                        SmprMonitoringService.Log("This program works only on Ethernet networks.");
                        return;
                    }


                    StringBuilder sb = new StringBuilder();
                    sb.Append("ip dst host ");
                    sb.Append(localIpAdress);
                    sb.Append(" and (");


                    List<int> tcpPorts = new List<int>();
                    List<int> udpPorts = new List<int>();


                    foreach (var dest in _settings.Destinations)
                    {
                        if (dest.Protocol == DestinationProtocol.UDP)
                        {
                            udpPorts.Add(dest.Port);
                        }
                        else if (dest.Protocol == DestinationProtocol.TCP)
                        {
                            tcpPorts.Add(dest.Port);
                        }
                        else throw new Exception("Unknown destination protocol: " + dest.Name);
                    }

                    if (udpPorts.Count > 0)
                    {
                        sb.Append("udp dst port ");
                        for (int i = 0; i < udpPorts.Count - 1; i++)
                        {
                            sb.Append(udpPorts[i]);
                            sb.Append(" or ");
                        }
                        sb.Append(udpPorts[udpPorts.Count - 1]);
                    }

                    if (udpPorts.Count > 0 && tcpPorts.Count > 0) sb.Append(" or ");

                    if (tcpPorts.Count > 0)
                    {
                        sb.Append("tcp src port ");
                        for (int i = 0; i < tcpPorts.Count - 1; i++)
                        {
                            sb.Append(tcpPorts[i]);
                            sb.Append(" or ");
                        }
                        sb.Append(tcpPorts[tcpPorts.Count - 1]);
                    }

                    sb.Append(")");

                    SmprMonitoringService.Log("Filter generated: " + sb.ToString());

                    using (BerkeleyPacketFilter filter = _communicator.CreateFilter(sb.ToString()))
                    {
                        _communicator.SetFilter(filter);
                    }
                    _communicator.ReceivePackets(0, PacketHandler);
                }
            }
            else
            {
                SmprMonitoringService.Log("Destination list is empty");
            }
        }

        internal static void Stop()
        {
            if (!_started) return;


            _communicator.Break();
            _timer1.Stop();
            _server.Stop();

            _started = false;
        }

        private static void OnConnectionEvent(object parameter, ClientConnection connection, ClientConnectionEvent eventType)
        {
            SmprMonitoringService.Log("connection event " + eventType.ToString() + " " + connection.RemoteEndpoint);
        }

        private static bool OnConnectionRequest(object parameter, IPAddress ipAddress)
        {
            if (_allowedIpAddresses.Count == 0 || _allowedIpAddresses.Contains(ipAddress))
            {
                if (_server.HasActiveConnections)
                {
                    SmprMonitoringService.Log("Connection from " + ipAddress.ToString() + " is not allowed because the server already has an active connection");
                    return false;
                }

                SmprMonitoringService.Log("Allowed connection request from " + ipAddress.ToString());
                return true;
            }
            else
            {
                SmprMonitoringService.Log("Denied connection request from " + ipAddress.ToString());
                return false;
            }
        }

        private static void Timer1Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            uint newTime = (uint)(DateTime.UtcNow - _unixOrigin).TotalSeconds;


            if (_elapsedTime < newTime)
            {
                _elapsedTime = newTime;

                uint requestTime = _elapsedTime - _settings.RequestDepth;

                bool nextPeriod = false;
                if (_nextPeriodTime <= newTime)
                {
                    _nextPeriodTime = newTime + 60;
                    nextPeriod = true;
                }

                foreach (var dest in _settings.Destinations)
                {
                    dest.SetMaxNumber(requestTime, _firstPeriodPassed);
                }

                if (_server.HasActiveConnections)
                {
                    foreach (var dest in _settings.Destinations)
                    {
                        var result = dest.GetSecond(requestTime);
                        CP56Time2a time = new CP56Time2a(_unixOrigin.AddSeconds(requestTime));

                        if (result != null)
                        {
                            if (dest.UseStatus) AddToASDU(new MeasuredValueShortWithCP56Time2a(dest.IOAPrefixMultiplied + 1, (float)result.Status, _qd, time));
                            if (dest.UseLostPackets) AddToASDU(new MeasuredValueShortWithCP56Time2a(dest.IOAPrefixMultiplied + 2, result.LostPackets, _qd, time));
                            if (dest.UseReceivedPackets) AddToASDU(new MeasuredValueShortWithCP56Time2a(dest.IOAPrefixMultiplied + 3, result.ReceivedPackets, _qd, time));
                            if (dest.UseAverageTransmissionDelay) AddToASDU(new MeasuredValueShortWithCP56Time2a(dest.IOAPrefixMultiplied + 4, (float)result.AverageTransmissionDelay, _qd, time));
                            if (dest.UseJitter) AddToASDU(new MeasuredValueShortWithCP56Time2a(dest.IOAPrefixMultiplied + 5, (float)result.Jitter, _qd, time));
                            if (dest.UseMaxTransmissionDelay) AddToASDU(new MeasuredValueShortWithCP56Time2a(dest.IOAPrefixMultiplied + 6, (float)result.MaxTransmissionDelay, _qd, time));
                            if (dest.UseMinTransmissionDelay) AddToASDU(new MeasuredValueShortWithCP56Time2a(dest.IOAPrefixMultiplied + 7, (float)result.MinTransmissionDelay, _qd, time));
                            if (dest.UseDuplicatePackets) AddToASDU(new MeasuredValueShortWithCP56Time2a(dest.IOAPrefixMultiplied + 10, result.DuplicatePackets, _qd, time));
                        }
                        else
                        {
                            float delay = (requestTime - dest.LastRequestedRecievedTime + _settings.RequestDepth - 1) * 1000f;
                            if (dest.UseStatus) AddToASDU(new MeasuredValueShortWithCP56Time2a(dest.IOAPrefixMultiplied + 1, (float)SecondStatus.NotRecieved, _qd, time));
                            if (dest.UseLostPackets) AddToASDU(new MeasuredValueShortWithCP56Time2a(dest.IOAPrefixMultiplied + 2, 50, _qd, time));
                            if (dest.UseReceivedPackets) AddToASDU(new MeasuredValueShortWithCP56Time2a(dest.IOAPrefixMultiplied + 3, 0, _qd, time));
                            if (dest.UseAverageTransmissionDelay) AddToASDU(new MeasuredValueShortWithCP56Time2a(dest.IOAPrefixMultiplied + 4, delay, _qd, time));
                            if (dest.UseJitter) AddToASDU(new MeasuredValueShortWithCP56Time2a(dest.IOAPrefixMultiplied + 5, 0, _qd, time));
                            if (dest.UseMaxTransmissionDelay) AddToASDU(new MeasuredValueShortWithCP56Time2a(dest.IOAPrefixMultiplied + 6, delay, _qd, time));
                            if (dest.UseMinTransmissionDelay) AddToASDU(new MeasuredValueShortWithCP56Time2a(dest.IOAPrefixMultiplied + 7, delay, _qd, time));
                            if (dest.UseDuplicatePackets) AddToASDU(new MeasuredValueShortWithCP56Time2a(dest.IOAPrefixMultiplied + 10, 0, _qd, time));
                        }

                        if (_firstPeriodPassed && nextPeriod && dest.UseLostPacketsPerPeriod)
                        {
                            AddToASDU(new MeasuredValueShortWithCP56Time2a(dest.IOAPrefixMultiplied + 8, dest.LostPacketsPerMinute, _qd, time));
                        }

                        if (dest.UseLastRecievedTime)
                        {
                            float receivedSecondsAgo = (float)(Math.Truncate((DateTime.UtcNow - dest.LastRecievedDateTime).TotalSeconds));

                            if (receivedSecondsAgo <= _settings.IgnoreChannelLostSeconds) receivedSecondsAgo = 0f;
                            AddToASDU(new MeasuredValueShortWithCP56Time2a(dest.IOAPrefixMultiplied + 9, receivedSecondsAgo, _qd, time));
                        }
                    }

                    if (_asdu.NumberOfElements > 0) SendASDU();
                }

                if (nextPeriod)
                {
                    _firstPeriodPassed = true;
                    foreach (var dest in _settings.Destinations)
                    {
                        dest.ResetLostPacketsPerMinute();
                    }
                }
            }
        }

        static private void SendASDU()
        {
            _server.EnqueueASDU(_asdu);
            _asdu = new ASDU(_alp, TypeID.M_ME_TF_1, CauseOfTransmission.SPONTANEOUS, false, false, 0, 0, false);
        }

        static private void AddToASDU(InformationObject io)
        {
            if (_asdu.AddInformationObject(io)) return;
            else
            {
                SendASDU();
                if (_asdu.AddInformationObject(io) == false) throw new Exception("Cannot add information object to ASDU");
            }
        }

        private static void PacketHandler(Packet packet)
        {
            IpV4Datagram ip = packet.Ethernet.IpV4;

            Datagram datagram;
            ushort? port = null;
            if (ip.Protocol == IpV4Protocol.Udp)
            {
                UdpDatagram udp = ip.Udp;
                datagram = udp.Payload;
                port = udp.DestinationPort;
            }
            else if (ip.Protocol == IpV4Protocol.Tcp)
            {
                TcpDatagram tcp = ip.Tcp;
                datagram = tcp.Payload;
                port = tcp.SourcePort;
            }
            else throw new Exception("recieved packet with unknown protocol: " + ip.Protocol);

            bool found = false;
            for (int i = 0; i < _settings.Destinations.Count; i++)
            {
                if (port == _settings.Destinations[i].Port)
                {
                    found = true;

                    _settings.Destinations[i].ProccesDatagram(datagram, (packet.Timestamp.ToUniversalTime() - _unixOrigin).TotalMilliseconds);
                    break;
                }
            }
            if (found == false)
            {
                SmprMonitoringService.Log("Unknown DestinationPort");
            }
        }
    }
}
