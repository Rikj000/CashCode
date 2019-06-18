using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CashCode.Net
{
    public enum ResponseType { ACK, NAK };

    public sealed class Package
    {
        #region Fields

        private const int POLYNOMIAL =  0x08408;     // Needed to calculate CRC
        private const byte _Sync =      0x02;        // Synchronization bit (fixed)
        private const byte _Adr =       0x03;        // The peripheral address of the equipment. For the bill acceptor from the documentation is 0x03

        private byte _Cmd;
        private byte[] _Data;

        #endregion

        #region Class constructor

        public Package()
        {}

        public Package(byte cmd, byte[] data)
        {
            this._Cmd = cmd;
            this.Data = data;
        }

        #endregion

        #region Properties

        public byte Cmd
        {
            get { return _Cmd; }
            set { _Cmd = value; }
        }

        public byte[] Data
        {
            get { return _Data; }
            set 
            {
                if (value.Length + 5 > 250)
                {

                }
                else
                {
                    _Data = new byte[value.Length];
                    _Data = value;
                }
            }
        }

        #endregion

        #region Methods

        // Returns an array of packet bytes.
        public byte[] GetBytes()
        {
            // Package buffer (without 2 bytes CRC). The first four bytes are SYNC, ADR, LNG, CMD
            List<byte> Buff = new List<byte>();

            // Byte 1: Sync flag
            Buff.Add(_Sync);

            // Byte 2: device address
            Buff.Add(_Adr);

            // Byte 3: packet length
            // calculate the packet length
            int result = this.GetLength();

            // If the packet length with SYNC, ADR, LNG, CRC, CMD bytes is more than 250
            if (result > 250)
            {
                // then we make the length byte equal to 0, and the actual length of the message will be in DATA
                Buff.Add(0);
            }
            else
            {
                Buff.Add(Convert.ToByte(result));
            }

            // Byte 4: Team
            Buff.Add(this._Cmd);

            // Byte 4: Team
            if (this._Data != null)
            {
                for (int i = 0; i < _Data.Length; i++)
                { Buff.Add(this._Data[i]); }
            }

            // Last byte - CRC
            byte[] CRC = BitConverter.GetBytes(GetCRC16(Buff.ToArray(), Buff.Count));

            byte[] tempPackage = new byte[Buff.Count + CRC.Length];
            Buff.ToArray().CopyTo(tempPackage, 0);
            CRC.CopyTo(tempPackage, Buff.Count);

            // Remove trailing zero's
            var l = tempPackage.Length - 1;
            while (tempPackage[l] == 0)
            {
                --l;
            }
            var package = new byte[l + 1];
            Array.Copy(tempPackage, package, l + 1);

            return package;
        }

        // Returns a hexadecimal string of packet bytes
        public string GetBytesHex()
        {
            byte[] package = GetBytes();

            StringBuilder hexString = new StringBuilder(package.Length);
            for (int i = 0; i < package.Length; i++)
            {
                hexString.Append(package[i].ToString("X2"));
            }

            return "0x" + hexString.ToString();
        }

        // Package length
        public int GetLength()
        {
            return (this._Data == null ? 0 : this._Data.Length) + 6;
        }

        // Checksum calculation
        private static int GetCRC16(byte[] BufData, int SizeData)
        {
            int TmpCRC, CRC;
            CRC = 0;

            for (int i = 0; i < SizeData; i++)
            {
                TmpCRC = CRC ^ BufData[i];

                for (byte j = 0; j < 8; j++)
                {
                    if ((TmpCRC & 0x0001) != 0) { TmpCRC >>= 1; TmpCRC ^= POLYNOMIAL; }
                    else { TmpCRC >>= 1; }
                }

                CRC = TmpCRC;
            }

            return CRC;
        }

        public static bool CheckCRC(byte[] Buff)
        {
            bool result = true;

            byte[] OldCRC = new byte[] { Buff[Buff.Length - 2], Buff[Buff.Length - 1]};

            // The last two bytes in length are removed, since this is the original CRC
            byte[] NewCRC = BitConverter.GetBytes(GetCRC16(Buff, Buff.Length - 2));

            for (int i = 0; i < 2; i++)
            {
                if (OldCRC[i] != NewCRC[i])
                {
                    result = false;
                    break;
                }
            }

            return result;
        }

        public static byte[] CreateResponse(ResponseType type)
        {
            // Packet buffer (without 2 bytes CRC). The first four bytes are SYNC, ADR, LNG, CMD
            List<byte> Buff = new List<byte>();

            // Byte 1: Sync flag
            Buff.Add(_Sync);

            // Byte 2: device address
            Buff.Add(_Adr);

            // Byte 3: packet length, always 6
            Buff.Add(0x06);

            // Byte 4: Data
            if (type == ResponseType.ACK) { Buff.Add(0x00); }
            else if (type == ResponseType.NAK) { Buff.Add(0xFF); }

            // Last byte - CRC
            byte[] CRC = BitConverter.GetBytes(GetCRC16(Buff.ToArray(), Buff.Count));

            byte[] package = new byte[Buff.Count + CRC.Length];
            Buff.ToArray().CopyTo(package, 0);
            CRC.CopyTo(package, Buff.Count);

            return package;
        }

        #endregion
    }
}
