using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SMPRmonitoring
{
    [JsonConverter(typeof(IpConverter))]
    public class Ip : IPAddress
    {
        private bool _ipAsIntHasValue = false;
        private uint _ipAsUint = 0;
        public uint AsUint
        {
            get
            {
                if (!_ipAsIntHasValue)
                {
                    var bytes = GetAddressBytes();
                    var bytesInversedOrder = new byte[] {bytes[3], bytes[2], bytes[1], bytes[0]};

                    _ipAsUint = BitConverter.ToUInt32(bytesInversedOrder, 0);
                }

                return _ipAsUint;
            }
        }

        public Ip(string ip) : base(IPAddress.Parse(ip).GetAddressBytes())
        {

        }
    }

    public class IpConverter : JsonConverter
    {
        public override bool CanRead => true;
        
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return new Ip(reader.Value.ToString());
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Ip);
        }
    }
}
