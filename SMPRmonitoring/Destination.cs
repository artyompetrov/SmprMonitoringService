using Newtonsoft.Json;
using PcapDotNet.Packets;
using System;
using System.Collections.Generic;
using System.Threading;

namespace SmprMonitoring
{
    public class Destination
    {
        List<Second> _secondList = new List<Second>();
        uint _maxNumber = 0;
        private int _destinationNumber;
        byte[] nextElement = new byte[2];
        byte[] timeBytes = new byte[4];
        byte[] FOCBytes = new byte[4];

        [JsonIgnore]
        public DateTime LastRecievedDateTime;

        [JsonIgnore]
        public int ListLength { get { return _secondList.Count; } }

        [JsonIgnore]
        public uint LastRequestedRecievedTime { get; set; }

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
        public bool UseReceivedPackets { get; set; }
        public bool UseAverageTransmissionDelay { get; set; }
        public bool UseJitter { get; set; }
        public bool UseMaxTransmissionDelay { get; set; }
        public bool UseMinTransmissionDelay { get; set; }
        public bool UseLostPacketsPerPeriod { get; set; }
        public bool UseLastRecievedTime { get; set; }
        public bool UseDuplicatePackets { get; set; }

        public int IOAPrefix
        {
            get
            {
                return _destinationNumber;
            }
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

        public void ProccesDatagram(Datagram datagram, double receiveTimeMS)
        {
            List<Tuple<uint, uint>> packets = new List<Tuple<uint, uint>>();

            try
            {
                int byteNumber = 0;
                while (byteNumber < datagram.Length)
                {
                    if (datagram[byteNumber++] != 0xaa || datagram[byteNumber++] != 0x01) break;

                    nextElement[1] = datagram[byteNumber++];
                    nextElement[0] = datagram[byteNumber++];

                    byteNumber += 2;

                    timeBytes[3] = datagram[byteNumber++];
                    timeBytes[2] = datagram[byteNumber++];
                    timeBytes[1] = datagram[byteNumber++];
                    timeBytes[0] = datagram[byteNumber++];

                    byteNumber++;

                    FOCBytes[2] = datagram[byteNumber++];
                    FOCBytes[1] = datagram[byteNumber++];
                    FOCBytes[0] = datagram[byteNumber++];

                    packets.Add(new Tuple<uint, uint>(BitConverter.ToUInt32(timeBytes, 0), BitConverter.ToUInt32(FOCBytes, 0)));

                    byteNumber += BitConverter.ToInt16(nextElement, 0) - 14;
                }
            }
            catch (IndexOutOfRangeException) {  }

            lock (_secondList)
            {

                bool firstElementProccesed = false;
                foreach (var packet in packets)
                {
                    if (packet.Item1 <= _maxNumber) continue;

                    double transmissionDelay = receiveTimeMS - (packet.Item1 * 1000.0 + packet.Item2 / FOSSize);

                    bool found = false;
                    int insertAt = _secondList.Count;
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

                    bool packetWasRegistred = false;
                    if (found)
                    {
                        packetWasRegistred = _secondList[i].RegisterReceivedPacket(packet.Item2, transmissionDelay);
                    }
                    else
                    {
                        var second = new Second(packet.Item1);
                        packetWasRegistred = second.RegisterReceivedPacket(packet.Item2, transmissionDelay);
                        _secondList.Insert(insertAt, second);
                    }

                    if (UseLastRecievedTime && !firstElementProccesed && packetWasRegistred)
                    {
                        firstElementProccesed = true;
                        LastRecievedDateTime = DateTime.UtcNow;
                    }
                }
            }
        }

        public void SetMaxNumber(uint maxNumber, bool firstPeriodPassed)
        {
            _maxNumber = maxNumber;

            lock (_secondList)
            {
                int toDelete = 0;
                for (int i = 0; i < _secondList.Count; i++)
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

        public Second GetSecond(uint Number)
        {
            lock (_secondList)
            {
                bool found = false;
                int i;
                for (i = 0; i < _secondList.Count; i++)
                {
                    if (_secondList[i].Number >= Number)
                    {
                        if (_secondList[i].Number == Number) found = true;
                        break;
                    }
                }

                Second result = null;
                if (found)
                {
                    result = _secondList[i];
                    LastRequestedRecievedTime = Number;
                }
                return result;
            }
        }
        public override string ToString()
        {
            return string.Format("{0} : {1}", Name, Port);
        }

        public Destination(string name, ushort port, int destinationNumber, uint FOCSize = 1)
        {
            Name = name;
            Port = port;
            FOSSize = FOCSize;
            IOAPrefix = destinationNumber;
        }

        public Destination()
        {
            LastRecievedDateTime = DateTime.UtcNow;
        }
    }
}
