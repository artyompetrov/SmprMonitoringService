using Newtonsoft.Json;
using PcapDotNet.Packets;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;

namespace SMPRmonitoring
{
    public class Destination
    {
        readonly List<Second> _secondList = new List<Second>();

        private uint _maxNumber = 0;
        private int _destinationNumber;
        private readonly byte[] _nextElement = new byte[2];
        private readonly byte[] _timeBytes = new byte[4];
        private readonly byte[] _FOCBytes = new byte[4];

        public BindingList<Ip> IpAddresses { get; set; } = new BindingList<Ip>();

        [JsonIgnore]
        public DateTime LastReceivedDateTime;

        [JsonIgnore]
        public int ListLength => _secondList.Count;

        [JsonIgnore]
        public uint LastRequestedReceivedTime { get; set; }

        [JsonIgnore]
        public int IOAPrefixMultiplied { get; private set; }

        [JsonIgnore]
        public int LostPacketsPerMinute { get; private set; }

        public DestinationProtocol Protocol { get; set; }
        public uint FOSSize { get; set; } = 1;
        public ushort Port { get; set; }
        public string Name { get; set; }
        public bool UseStatus { get; set; }
        public bool UseLostPackets { get; set; }
        public bool UseAverageTransmissionDelay { get; set; }
        public bool UseJitter { get; set; }
        public bool UseLostPacketsPerPeriod { get; set; }
        public bool UseLastReceivedTime { get; set; }
        //public bool UseDuplicatePackets { get; set; }

        [JsonIgnore]
        public bool Enabled => (UseStatus || UseLostPackets || UseAverageTransmissionDelay || UseJitter ||
                                UseLostPacketsPerPeriod || UseLastReceivedTime);

        public int IOAPrefix
        {
            get => _destinationNumber;
            set
            {
                if (value >= 655 || value < 0) throw new Exception("Prefix value should be between 0..654");
                _destinationNumber = value;
                IOAPrefixMultiplied = 100 * value;
            }
        }

        public void ResetLostPacketsPerMinute()
        {
            LostPacketsPerMinute = 0;
        }

        public void ProcessDatagram(Datagram datagram, double receiveTimeMS)
        {
            var packets = new List<Tuple<uint, uint>>();

            try
            {
                var byteNumber = 0;
                while (byteNumber < datagram.Length)
                {
                    if (datagram[byteNumber++] != 0xaa || datagram[byteNumber++] != 0x01) break;

                    _nextElement[1] = datagram[byteNumber++];
                    _nextElement[0] = datagram[byteNumber++];

                    byteNumber += 2;

                    _timeBytes[3] = datagram[byteNumber++];
                    _timeBytes[2] = datagram[byteNumber++];
                    _timeBytes[1] = datagram[byteNumber++];
                    _timeBytes[0] = datagram[byteNumber++];

                    byteNumber++;

                    _FOCBytes[2] = datagram[byteNumber++];
                    _FOCBytes[1] = datagram[byteNumber++];
                    _FOCBytes[0] = datagram[byteNumber++];

                    byteNumber += BitConverter.ToInt16(_nextElement, 0) - 14;

                    if (byteNumber <= datagram.Length)
                        packets.Add(new Tuple<uint, uint>(BitConverter.ToUInt32(_timeBytes, 0), BitConverter.ToUInt32(_FOCBytes, 0)));
                }
            }
            catch (IndexOutOfRangeException) { }

            lock (_secondList)
            {

                bool firstElementProcessed = false;
                foreach (var packet in packets)
                {
                    if (packet.Item1 <= _maxNumber) continue;

                    var transmissionDelay = receiveTimeMS - (packet.Item1 * 1000.0 + packet.Item2 / FOSSize);

                    var found = false;
                    var insertAt = _secondList.Count;
                    int i;
                    for (i = _secondList.Count - 1; i >= 0; i--)
                    {
                        if (_secondList[i].Number > packet.Item1) insertAt = i;
                        else
                        if (packet.Item1 == _secondList[i].Number)
                        {
                            found = true;
                            break;
                        }
                        else break;
                    }

                    var packetWasRegistered = false;
                    if (found)
                    {
                        packetWasRegistered = _secondList[i].RegisterReceivedPacket(packet.Item2, transmissionDelay);
                    }
                    else
                    {
                        var second = new Second(packet.Item1);
                        packetWasRegistered = second.RegisterReceivedPacket(packet.Item2, transmissionDelay);
                        _secondList.Insert(insertAt, second);
                    }

                    if (UseLastReceivedTime && !firstElementProcessed && packetWasRegistered)
                    {
                        firstElementProcessed = true;
#if !DEBUG
                        LastReceivedDateTime = DateTime.UtcNow;
#endif
                    }
                }
            }
        }

        public void SetMaxNumber(uint maxNumber, bool firstPeriodPassed)
        {
            _maxNumber = maxNumber;

            lock (_secondList)
            {
                var toDelete = 0;
                for (var i = 0; i < _secondList.Count; i++)
                {
                    if (_secondList[i].Number < _maxNumber)
                    {
                        if (firstPeriodPassed) LostPacketsPerMinute += _secondList[i].LostPackets;
                        toDelete++;
                    }
                    else break;
                }

                if (toDelete > 0)
                    _secondList.RemoveRange(0, toDelete);
                else
                {
                    if (firstPeriodPassed) LostPacketsPerMinute += 50;
                }
            }
        }

        public Second GetSecond(uint number)
        {
            lock (_secondList)
            {
                var found = false;
                int i;
                for (i = 0; i < _secondList.Count; i++)
                {
                    if (_secondList[i].Number >= number)
                    {
                        if (_secondList[i].Number == number) found = true;
                        break;
                    }
                }
                
                if (!found) return null;

                var result = _secondList[i];
                LastRequestedReceivedTime = number;
                return result;
            }
        }
        public override string ToString()
        {
            return $"{Name} : {Port.ToString()}";
        }

        public Destination(string name, ushort port, int IOAPrefix, uint FOCSize = 1)
        {
            Name = name;
            Port = port;
            FOSSize = FOCSize;
            this.IOAPrefix = IOAPrefix;
        }

        public Destination()
        {
#if !DEBUG
            LastReceivedDateTime = DateTime.UtcNow;
#endif
        }

#if DEBUG
        public List<Second> SecondList { get => _secondList; }
#endif
    }
}
