using System;
using System.Net;

namespace STUN.Attributes
{
    public class STUNXorMappedAddressAttribute : STUNEndPointAttribute
    {
        public override string ToString()
        {
            return string.Format("XOR-MAPPED-ADDRESS {0}", EndPoint);
        }
        
        public override void Parse(STUNMessage msg, STUNBinaryReader binary, int length)
        {
            binary.BaseStream.Position++;
            
            var ipFamily = binary.ReadByte();
            // Port is computed by taking the mapped port in host byte order,
            // XOR'ing it with the most significant 16 bits of the magic cookie.
            byte[] p = binary.ReadBytes(2);
            for (int i = 0; i < 2; ++i)
            {
                p[i] = (byte)(p[i] ^ msg.TransactionID[i]);
            }

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(p);
            }

            var port = BitConverter.ToUInt16(p, 0);
            
            IPAddress address;

            if (ipFamily == 1)
            {
                // IPv4 address is computed by taking the mapped IP
                // address in host byte order and XOR'ing it with the magic cookie.
                byte [] a = binary.ReadBytes(4);
                for (int i = 0; i < 4; ++i)
                {
                    a[i] = (byte)(a[i] ^ msg.TransactionID[i]);
                }
                address = new IPAddress(a);
            }
            else if (ipFamily == 2)
            {
                // IPv6 address is computed by taking the mapped IP address
                // in host byte order, XOR'ing it with the concatenation of the magic
                // cookie and the 96 - bit transaction ID.
                byte[] a = binary.ReadBytes(16);
                for (int i = 0; i < 16; ++i)
                {
                    a[i] = (byte)(a[i] ^ msg.TransactionID[i]);
                }
                address = new IPAddress(a);
            }
            else
            {
                throw new Exception("Unsupported IP Family " + ipFamily.ToString());
            }

            EndPoint = new IPEndPoint(address, port);
        }
    }
}