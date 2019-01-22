namespace SmprMonitoring
{
    class PacketParameters
    {
        public uint SendTime;
        public uint FractionOfSecond;

        public PacketParameters(uint sendTime, uint fractionOfSecond)
        {
            SendTime = sendTime;
            FractionOfSecond = fractionOfSecond;
        }
    }
}
