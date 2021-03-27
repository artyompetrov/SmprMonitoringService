using lib60870;
using lib60870.CS101;
using lib60870.CS104;
using Newtonsoft.Json;
using PcapDotNet.Core;
using PcapDotNet.Packets;
using PcapDotNet.Packets.IpV4;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using SMPRmonitoring;
using Timer = System.Timers.Timer;

namespace SmprMonitoringService
{
    internal static class SmprMonitoringWorker
    {
        private static readonly DateTime _unixOrigin = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly Timer _timer1 = new Timer(100);
        private static readonly ApplicationLayerParameters _alp = new ApplicationLayerParameters();
        private static readonly QualityDescriptor _qd = new QualityDescriptor();
        private static readonly Dictionary<int, float> _lastSentData = new Dictionary<int, float>();
        private static readonly Dictionary<long, Destination> _destinationDictionary = new Dictionary<long, Destination>();
        private static uint _elapsedTime;
        private static uint _nextPeriodTime;
        private static Server _server;
        private static Settings _settings;
        private static PacketCommunicator _communicator;
        private static bool _started;
        private static bool _firstPeriodPassed;
        private static ASDU _asdu;
        
        internal static void Start()
        {
            if (_started) return;
            _started = true;

            using (var file = File.OpenText(AppDomain.CurrentDomain.BaseDirectory + "settings.json"))
            {
                var serializer = new JsonSerializer();
                _settings = (Settings)serializer.Deserialize(file, typeof(Settings));
            }

            var checkSettings = _settings.CheckSettings();
            if (checkSettings != null) throw new Exception("Wrong settings: " + checkSettings);

            foreach (var destination in _settings.Destinations)
            {
                foreach (var ipAddress in destination.IpAddresses)
                {
                    long ipPort = ipAddress.AsUint * 65536 + destination.Port;
                    _destinationDictionary.Add(ipPort, destination);
                }
            }
            
            _timer1.Elapsed += Timer1Elapsed;

            PacketDevice selectedDevice = null;
            var localIpAddress = string.Empty;

#if DEBUG
            selectedDevice = new OfflinePacketDevice(_settings.DebugPcapFile);

            SmprMonitoringService.Log("Selected offline packet device: " + _settings.DebugPcapFile);
            localIpAddress = _settings.DebugLocalIP;
#else
            IList<LivePacketDevice> allDevices = LivePacketDevice.AllLocalMachine;

            if (allDevices.Count == 0)
            {
                throw new Exception("No interfaces found! Make sure WinPcap is installed");
            }
            
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
            
            bool ipFound = false;
            for (int i = 0; i < selectedDevice.Addresses.Count; i++)
            {
                if (selectedDevice.Addresses[i].Address.Family != SocketAddressFamily.Internet) continue;

                if (ipFound) throw new Exception("Devices with two or more IPv4 addresses are not supported");
                ipFound = true;
                localIpAddress = selectedDevice.Addresses[i].Address.ToString().Remove(0, 9);
            }
#endif
            _asdu = new ASDU(_alp, TypeID.M_ME_TF_1, CauseOfTransmission.SPONTANEOUS, false, false, 0, _settings.RTUID, false);
            _server = new Server(_settings.Iec104Port) {ServerMode = ServerMode.SINGLE_REDUNDANCY_GROUP};

            _server.SetConnectionRequestHandler(OnConnectionRequest, null);
            _server.SetConnectionEventHandler(OnConnectionEvent, null);
            _server.SetInterrogationHandler(OnInterrogation, null);
#if !DEBUG
           _server.Start(); 
#endif
            
            if (_settings.Destinations.Count > 0)
            {
                uint startTime = (uint)(DateTime.UtcNow - _unixOrigin).TotalSeconds - _settings.RequestDepth + 1;

                foreach (var dest in _settings.Destinations)
                {
                    dest.LastRequestedReceivedTime = startTime;
                }

                _nextPeriodTime = (uint)(DateTime.UtcNow - _unixOrigin).TotalSeconds + _settings.AveragingPeriod;
                _elapsedTime = (uint)(DateTime.UtcNow - _unixOrigin).TotalSeconds + _settings.RequestDepth;
#if !DEBUG
                _timer1.Start();
#endif

                using (_communicator = selectedDevice.Open(65536, PacketDeviceOpenAttributes.Promiscuous, 1000))
                {
                    if (_communicator.DataLink.Kind != DataLinkKind.Ethernet)
                    {
                        SmprMonitoringService.Log("This program works only on Ethernet networks.");
                        return;
                    }
                    
                    /*генерация PacketFilter*/
                    var sb = new StringBuilder();
                    sb.Append("ip dst host ");
                    sb.Append(localIpAddress);
                    sb.Append(" and (");

                    var destinationCount = _settings.Destinations.Count - 1;
                    for (var i = 0; i <= destinationCount; i++)
                    {
                        var currentDestination = _settings.Destinations[i];
                        sb.Append("(");

                        if (currentDestination.IpAddresses.Count > 0)
                        {
                            sb.Append("ip src host ");
                            sb.Append(string.Join(" or ", currentDestination.IpAddresses));
                            sb.Append(" and ");
                        }

                        switch (currentDestination.Protocol)
                        {
                            case DestinationProtocol.TCP:
                                sb.Append("tcp src ");
                                break;
                            case DestinationProtocol.UDP:
                                sb.Append("udp dst ");
                                break;
                            default:
                                throw new Exception("Unknown destination protocol: " + currentDestination.Name);
                        }
                        
                        sb.Append($"port {currentDestination.Port.ToString()}");
                        
                        sb.Append(")");

                        if (i != destinationCount)
                        {
                            sb.Append(" or ");
                        }
                    }

                    sb.Append(")");
                    /*генерация PacketFilter*/

                    SmprMonitoringService.Log("Filter generated: " + sb);

                    using (var filter = _communicator.CreateFilter(sb.ToString()))
                    {
                        _communicator.SetFilter(filter);
                    }
                    _communicator.ReceivePackets(0, PacketHandler);
#if DEBUG
                    using (var sw = new StreamWriter(_settings.DebugPcapFile + ".json"))
                    {
                        var serializer = new JsonSerializer() { Formatting = Formatting.Indented };
                        serializer.Serialize(sw, _settings);
                    }
#endif
                }
            }
            else
            {
                SmprMonitoringService.Log("Destination list is empty");
            }
        }

        private static bool OnInterrogation(object parameter, IMasterConnection connection, ASDU asdu, byte qoi)
        {
            if (qoi != 20 && qoi != 21) return false;

            var cp = connection.GetApplicationLayerParameters();

            connection.SendACT_CON(asdu, false);

            var newAsdu = new ASDU(cp, TypeID.M_ME_TF_1, CauseOfTransmission.INTERROGATED_BY_STATION, false, false, 0, _settings.RTUID, false);

            var time = new CP56Time2a(DateTime.UtcNow.AddSeconds(-_settings.RequestDepth));

            lock (_lastSentData)
            {
                foreach (var pair in _lastSentData)
                {
                    if (!newAsdu.AddInformationObject(new MeasuredValueShortWithCP56Time2a(pair.Key, pair.Value, _qd, time)))
                    {
                        connection.SendASDU(newAsdu);
                        newAsdu = new ASDU(cp, TypeID.M_ME_TF_1, CauseOfTransmission.INTERROGATED_BY_STATION, false, false, 0, _settings.RTUID, false);
                    }
                }
            }

            if (newAsdu.NumberOfElements>0) connection.SendASDU(newAsdu);

            connection.SendACT_TERM(asdu);

            return true;
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
            var allowedIp = false;
            foreach (var r in _settings.AllowedIPAddresses)
            {
                if (Equals(r, ipAddress))
                {
                    allowedIp = true;
                    break;
                }
            }

            if (allowedIp)
            {
                if (_server.HasActiveConnections)
                {
                    SmprMonitoringService.Log("Connection from " + ipAddress + " is not allowed because the server already has an active connection");
                    return false;
                }

                SmprMonitoringService.Log("Allowed connection request from " + ipAddress);
                return true;
            }
            else
            {
                SmprMonitoringService.Log("Denied connection request from " + ipAddress);
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
                        var time = new CP56Time2a(_unixOrigin.AddSeconds(requestTime));

                        if (result != null)
                        {
                            if (dest.UseStatus) AddToASDU(dest.IOAPrefixMultiplied + 1, (float)result.Status, time);
                            if (dest.UseLostPackets) AddToASDU(dest.IOAPrefixMultiplied + 2, result.LostPackets, time);
                            if (dest.UseAverageTransmissionDelay) AddToASDU(dest.IOAPrefixMultiplied + 3, (float)result.AverageTransmissionDelay, time);
                            if (dest.UseJitter) AddToASDU(dest.IOAPrefixMultiplied + 4, (float)result.Jitter, time);
                        }
                        else
                        {
                            var delay = (requestTime - dest.LastRequestedReceivedTime + _settings.RequestDepth - 1) * 1000f;
                            if (dest.UseStatus) AddToASDU(dest.IOAPrefixMultiplied + 1, (float)SecondStatus.NotReceived, time);
                            if (dest.UseLostPackets) AddToASDU(dest.IOAPrefixMultiplied + 2, 50, time);
                            if (dest.UseAverageTransmissionDelay) AddToASDU(dest.IOAPrefixMultiplied + 3, delay, time);
                            if (dest.UseJitter) AddToASDU(dest.IOAPrefixMultiplied + 4, 0, time);
                        }
                        
                        if (dest.UseLastReceivedTime)
                        {
                            var receivedSecondsAgo = (float)(Math.Truncate((DateTime.UtcNow - dest.LastReceivedDateTime).TotalSeconds));

                            if (receivedSecondsAgo <= _settings.IgnoreChannelLostSeconds) receivedSecondsAgo = 0f;
                            AddToASDU(dest.IOAPrefixMultiplied + 5, receivedSecondsAgo, time);
                        }

                        if (_firstPeriodPassed && nextPeriod && dest.UseLostPacketsPerPeriod)
                        {
                            AddToASDU(dest.IOAPrefixMultiplied + 6, dest.LostPacketsPerMinute, time);
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

        private static void SendASDU()
        {
            _server.EnqueueASDU(_asdu);
            _asdu = new ASDU(_alp, TypeID.M_ME_TF_1, CauseOfTransmission.SPONTANEOUS, false, false, 0, _settings.RTUID, false);
        }

        private static void AddToASDU(int objectAddress, float value, CP56Time2a time)
        {
            lock (_lastSentData)
            {
                if (_lastSentData.ContainsKey(objectAddress))
                {
                    if (Math.Abs(_lastSentData[objectAddress] - value) < 0.0000001) return;

                    _lastSentData[objectAddress] = value;
                }
                else _lastSentData.Add(objectAddress, value);
            }

            var io = new MeasuredValueShortWithCP56Time2a(objectAddress, value, _qd, time);

            if (!_asdu.AddInformationObject(io))
            {
                SendASDU();
                if (_asdu.AddInformationObject(io) == false) throw new Exception("Cannot add information object to ASDU");
            }
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
            var receiveTimeMs = (packet.Timestamp.ToUniversalTime() - _unixOrigin).TotalMilliseconds;
            _destinationDictionary[ipPort].ProcessDatagram(datagram, receiveTimeMs);
        }
    }
}
