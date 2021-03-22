using System;
using System.Collections.Generic;

namespace SMPRmonitoring
{
    public class Second
    {
        static DateTime _unixOrigin = new DateTime(1970, 1, 1, 0, 0, 0, 0);
        HashSet<uint> _milliseconds = new HashSet<uint>();
        double _transmissionDelaySumm = 0.0;

        public uint Number { get; }

        public int ReceivedPackets => _milliseconds.Count;

        public double AverageTransmissionDelay => _transmissionDelaySumm / ReceivedPackets;

        public int LostPackets => 50 - _milliseconds.Count;

        public double MaxTransmissionDelay { get; private set; } = double.NegativeInfinity;

        public double MinTransmissionDelay { get; private set; } = double.PositiveInfinity;

        public uint DuplicatePackets { get; private set; } = 0;


        public double Jitter => MaxTransmissionDelay - MinTransmissionDelay;

        public SecondStatus Status
        {
            get
            {
                if (ReceivedPackets > 50) return SecondStatus.Error;

                if (ReceivedPackets == 0) return SecondStatus.NotRecieved;

                if (DuplicatePackets > 0) return SecondStatus.ExcessDuplicates;

                if (ReceivedPackets == 50) return SecondStatus.ReceivedAll;

                return SecondStatus.ReceivedNotAll;
            }
        }

        public bool RegisterReceivedPacket(uint fractionOfSecond, double transmissionDelay)
        {
            if (_milliseconds.Add(fractionOfSecond))
            {
                _transmissionDelaySumm += transmissionDelay;

                if (MaxTransmissionDelay < transmissionDelay)
                {
                    MaxTransmissionDelay = transmissionDelay;
                }

                if (MinTransmissionDelay > transmissionDelay)
                {
                    MinTransmissionDelay = transmissionDelay;
                }

                return true;
            }
            else
            {
                DuplicatePackets++;
                return false;
            }
        }

        public Second(uint number)
        {
            Number = number;
        }

    }
}
