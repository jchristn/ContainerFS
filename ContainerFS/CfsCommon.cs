using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ContainerFS
{
    /// <summary>
    /// Commonly-used static methods.
    /// </summary>
    public static class CfsCommon
    {
        public static byte[] InitByteArray(int len, byte b)
        {
            if (len <= 0) return null;
            byte[] ret = new byte[len];
            for (int i = 0; i < len; i++)
            {
                ret[i] = b;
            }
            return ret;
        }

        public static void WriteAtPosition(FileStream fs, long position, byte[] data)
        {
            if (fs == null) throw new ArgumentNullException(nameof(fs));
            if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
            if (data == null || data.Length < 1) return;

            fs.Seek(position, SeekOrigin.Begin);
            fs.Write(data, 0, data.Length);
            return;
        }

        public static byte[] ReadFromPosition(FileStream fs, long position, int count)
        {
            if (fs == null) throw new ArgumentNullException(nameof(fs));
            if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));

            fs.Seek(position, SeekOrigin.Begin);
            byte[] ret = new byte[count];
            int read = fs.Read(ret, 0, count);
            if (read == count) return ret;
            else throw new IOException("Expected to read " + count + " bytes, only read " + read + " bytes");
        }

        public static bool IsTrue(int? val)
        {
            if (val == null) return false;
            if (Convert.ToInt32(val) == 1) return true;
            return false;
        }

        public static bool IsTrue(int val)
        {
            if (val == 1) return true;
            return false;
        }

        public static bool IsTrue(bool val)
        {
            return val;
        }

        public static bool IsTrue(bool? val)
        {
            if (val == null) return false;
            return Convert.ToBoolean(val);
        }

        public static bool IsTrue(string val)
        {
            if (String.IsNullOrEmpty(val)) return false;
            val = val.ToLower().Trim();
            int valInt = 0;
            if (Int32.TryParse(val, out valInt)) if (valInt == 1) return true;
            if (String.Compare(val, "true") == 0) return true;
            return false;
        }

        public static byte[] BitArrayToByteArray(BitArray bits)
        {
            byte[] ret = new byte[(bits.Length - 1) / 8 + 1];
            bits.CopyTo(ret, 0);
            return ret;
        }

        public static BitArray ByteArrayToBitArray(byte[] bytes)
        {
            return new BitArray(bytes);
        }

        public static byte[] TrimNullBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 1) return null;
            int pos = bytes.Length - 1;

            while (pos > 0)
            {
                if (bytes[pos] == 0x00) pos--;
                else break;
            }

            byte[] ret = new byte[pos + 1];
            Buffer.BlockCopy(bytes, 0, ret, 0, pos + 1);
            return ret;
        }

        public static bool ByteArraysIdentical(byte[] ba1, byte[] ba2)
        {
            int i;
            if (ba1.Length == ba2.Length)
            {
                i = 0;
                while (i < ba1.Length && (ba1[i] == ba2[i]))
                {
                    i++;
                }
                if (i == ba1.Length)
                {
                    return true;
                }
            }

            return false;
        }

        public static string BytesToHexString(byte[] data)
        {
            if (data == null || data.Length < 1) return null;
            string hex = BitConverter.ToString(data);
            return hex.Replace("-", "");
        }
    }
}
