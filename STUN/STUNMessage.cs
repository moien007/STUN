using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.IO;

namespace STUN
{
    public class STUNMessage
    {
        public STUNMessageTypes MessageType { get; set; }
        public byte[] TransactionID { get; set; }
        public List<STUNAttribute> Attributes { get; set; }

        public STUNMessage(STUNMessageTypes messageType, byte[] transactionID)
        {
            MessageType = messageType;
            TransactionID = transactionID;
            Attributes = new List<STUNAttribute>();
        }

        public bool TryParse(byte[] buffer)
        {
            try
            {
                Parse(buffer);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Parse(byte[] buffer)
        {
            Parse(buffer, 0, buffer.Length);
        }

        public void Parse(byte[] buffer, int index, int count)
        {
            using (MemoryStream memory = new MemoryStream(buffer, index, count))
            using (STUNBinaryReader binary = new STUNBinaryReader(memory))
            {
                Parse(binary);
            }
        }

        public void Parse(STUNBinaryReader binary)
        {
            MessageType = (STUNMessageTypes)binary.ReadUInt16();
            int messageLength = binary.ReadUInt16();
            TransactionID = binary.ReadBytes(16);

            Attributes = new List<STUNAttribute>();

            int attrType;
            int attrLength;

            while ((binary.BaseStream.Position - 20) < messageLength)
            {
                attrType = binary.ReadUInt16();
                attrLength = binary.ReadUInt16();

                var type = STUNAttribute.GetAttribute(attrType);

                if (type == null)
                {
                    binary.BaseStream.Position += attrLength;
                    continue;
                }

                var attr = Activator.CreateInstance(type) as STUNAttribute;
                attr.Parse(binary, attrLength);
                Attributes.Add(attr);
            }
        }

        public void Write(Stream stream)
        {
            using (STUNBinaryWriter binary = new STUNBinaryWriter(stream))
            {
                Write(binary);
            }
        }

        public void Write(STUNBinaryWriter binary)
        {
            binary.Write((ushort)MessageType);
            binary.Write((ushort)0);
            binary.Write(TransactionID);

            long length = 0;

            foreach (var attribute in Attributes)
            {
                var startPos = binary.BaseStream.Position;
                attribute.Write(binary);
                length += binary.BaseStream.Position - startPos;
            }

            binary.BaseStream.Position = 2;
            binary.Write((ushort)length);
        }

        public byte[] GetBytes()
        {
            using (MemoryStream memory = new MemoryStream())
            {
                Write(memory);
                return memory.ToArray();
            }
        }
        
        public static byte[] GenerateTransactionID()
        {
            Guid guid = Guid.NewGuid();
            return guid.ToByteArray();
        }
    }
}
