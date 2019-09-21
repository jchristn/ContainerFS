using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SyslogLogging;

namespace ContainerFS
{
    /// <summary>
    /// Block of data.
    /// </summary>
    public class DataBlock
    {
        #region Private-Members

        private LoggingModule Logging { get; set; }
        private int BlockSizeBytes { get; set; }

        #endregion

        #region Public-Members
        
        /// <summary>
        /// The FileStream used to interact with the container file.
        /// </summary>
        public FileStream Filestream { get; set; }

        /// <summary>
        /// The first four bytes of the block.
        /// </summary>
        public byte[] Signature { get; set; }           // 4 bytes @ 0

        /// <summary>
        /// The position of the parent block.
        /// </summary>
        public long ParentBlock { get; set; }           // 8 bytes @ 4

        /// <summary>
        /// The position of the next child block.
        /// </summary>
        public long ChildBlock { get; set; }            // 8 bytes @ 12

        /// <summary>
        /// The amount of data written to this block.
        /// </summary>
        public int DataLength { get; set; }             // 4 bytes @ 20 

        /// <summary>
        /// Byte data stored in this block.
        /// </summary>
        public byte[] Data { get; set; }                // from 64 on

        #endregion

        #region Static-Members

        /// <summary>
        /// The signature found in the first four bytes of a data block.
        /// </summary>
        public static byte[] SignatureBytes = new byte[4] { 0xFF, 0xFF, 0xFF, 0xFF };

        /// <summary>
        /// The number of bytes reserved at the beginning of a block for metadata.
        /// </summary>
        public static int BytesReservedPerBlock = 64;

        #endregion

        #region Constructors

        /// <summary>
        /// Parameterless constructor.  Please do not use.
        /// </summary>
        public DataBlock()
        {

        }

        /// <summary>
        /// Create a new data block.
        /// </summary>
        /// <param name="fs">The FileStream instance to use.</param>
        /// <param name="blockSize">The block size, in bytes.</param>
        /// <param name="data">The byte data to include in the block payload.</param>
        /// <param name="logging">Instance of LoggingModule to use for logging events.</param>
        public DataBlock(
            FileStream fs, 
            int blockSize,
            byte[] data, 
            LoggingModule logging)
        {
            if (fs == null) throw new ArgumentNullException(nameof(fs));
            if (blockSize < 4096) throw new ArgumentOutOfRangeException("Block size must be greater than or equal to 4096");
            if (blockSize % 4096 != 0) throw new ArgumentOutOfRangeException("Block size must be evenly divisible by 4096");

            BlockSizeBytes = blockSize;
            Data = data;
            Logging = logging;
            Filestream = fs;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Return a user-readable string containing details about the block.
        /// </summary>
        /// <returns>String.</returns>
        public override string ToString()
        {
            string ret = "";
            ret += Environment.NewLine;
            ret += "---" + Environment.NewLine;
            ret += "Data Block      : " + Environment.NewLine;
            ret += "  Parent Block  : " + ParentBlock + Environment.NewLine;
            ret += "  Child Block   : " + ChildBlock + Environment.NewLine;
            ret += "  Data Length   : " + DataLength + " bytes" + Environment.NewLine;
            return ret;
        }

        /// <summary>
        /// Create a formatted byte array containing the block.
        /// </summary>
        /// <returns>Byte array.</returns>
        public byte[] ToBytes()
        {
            try
            {
                byte[] ret = CfsCommon.InitByteArray(BlockSizeBytes, 0x00);
                Buffer.BlockCopy(DataBlock.SignatureBytes, 0, ret, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(ParentBlock), 0, ret, 4, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(ChildBlock), 0, ret, 12, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(DataLength), 0, ret, 20, 4);

                if (Data != null)
                {
                    LogDebug("ToBytes copying data of length " + Data.Length + " to position 64");
                    Buffer.BlockCopy(Data, 0, ret, 64, Data.Length);
                }

                return ret;
            }
            catch (Exception e)
            {
                if (Logging != null) Logging.LogException("DataBlock", "ToBytes", e);
                else LoggingModule.ConsoleException("DataBlock", "ToBytes", e);
                throw;
            }
        }

        /// <summary>
        /// Retrieve all byte data containing in this block and associated child data blocks.
        /// </summary>
        /// <returns>Byte array.</returns>
        public byte[] GetAllData()
        {
            if (Data == null || Data.Length < 1) return null;
            if (ChildBlock < 0) return Data;

            // get local data
            byte[] ret = new byte[Data.Length];
            Buffer.BlockCopy(Data, 0, ret, 0, Data.Length);
            long currPointer = ChildBlock;

            while (true)
            {
                if (currPointer > 0)
                {
                    byte[] nextBlockBytes = CfsCommon.ReadFromPosition(Filestream, currPointer, BlockSizeBytes);
                    DataBlock nextBlock = DataBlock.FromBytes(Filestream, BlockSizeBytes, nextBlockBytes, Logging);
                    if (nextBlock.DataLength < 1 || nextBlock.Data == null || nextBlock.Data.Length < 1)
                    {
                        LogDebug("GetAllData reached end of file in data block at position " + currPointer);
                        break;
                    }
                    else
                    {
                        LogDebug("GetAllData appending " + nextBlock.DataLength + " bytes of data from data block at position " + currPointer);
                        byte[] temp = new byte[(ret.Length + nextBlock.DataLength)];
                        Buffer.BlockCopy(ret, 0, temp, 0, ret.Length);
                        Buffer.BlockCopy(nextBlock.Data, 0, temp, ret.Length, nextBlock.DataLength);
                        ret = temp;

                        // update pointer
                        currPointer = nextBlock.ChildBlock;
                    }
                }
                else
                {
                    LogDebug("GetAllData no child block specified, exiting with " + ret.Length + " bytes of data");
                    break;
                }
            }

            return ret;
        }

        #endregion

        #region Private-Methods

        private void LogDebug(string msg)
        {
            if (Logging != null) Logging.Log(LoggingModule.Severity.Debug, msg);
        }

        private void LogWarn(string msg)
        {
            if (Logging != null) Logging.Log(LoggingModule.Severity.Warn, msg);
        }

        private void LogAlert(string msg)
        {
            if (Logging != null) Logging.Log(LoggingModule.Severity.Alert, msg);
        }

        #endregion

        #region Public-Static-Methods

        /// <summary>
        /// Create a new data block instance from a byte array from data found within the container file.
        /// </summary>
        /// <param name="fs">The FileStream instance to use.</param>
        /// <param name="blockSize">The block size, in bytes.</param>
        /// <param name="ba">The byte array containing the full block.</param>
        /// <param name="logging">Instance of LoggingModule to use for logging events.</param>
        /// <returns>A populated DataBlock object.</returns>
        public static DataBlock FromBytes(FileStream fs, int blockSize, byte[] ba, LoggingModule logging)
        {
            try
            {
                if (ba == null || ba.Length < 1) throw new ArgumentNullException(nameof(ba));
                if (ba.Length < 64) throw new ArgumentException("Byte array has length less than 64");
                if (blockSize < 4096) throw new ArgumentOutOfRangeException("Block size must be greater than or equal to 4096");
                if (blockSize % 4096 != 0) throw new ArgumentOutOfRangeException("Block size must be evenly divisible by 4096");
                if (fs == null) throw new ArgumentNullException(nameof(fs));

                if (logging != null) logging.Log(LoggingModule.Severity.Debug, "FromBytes converting " + ba.Length + " bytes to DataBlock");

                DataBlock ret = new DataBlock(fs, blockSize, null, logging);
                ret.Signature = new byte[4];
                ret.Logging = logging;
                ret.Filestream = fs;

                byte[] temp;

                Buffer.BlockCopy(ba, 0, ret.Signature, 0, 4);
                
                temp = new byte[4];
                Buffer.BlockCopy(ba, 8, temp, 0, 4);
                ret.Signature = temp;

                temp = new byte[8];
                Buffer.BlockCopy(ba, 4, temp, 0, 8);
                ret.ParentBlock = BitConverter.ToInt64(temp, 0);

                temp = new byte[8];
                Buffer.BlockCopy(ba, 12, temp, 0, 8);
                ret.ChildBlock = BitConverter.ToInt32(temp, 0);

                temp = new byte[4];
                Buffer.BlockCopy(ba, 20, temp, 0, 4);
                ret.DataLength = BitConverter.ToInt32(temp, 0);

                if (logging != null) logging.Log(LoggingModule.Severity.Debug, "FromBytes reading data " + ret.DataLength + " bytes from position 64");
                temp = new byte[ret.DataLength];
                Buffer.BlockCopy(ba, 64, temp, 0, ret.DataLength);
                ret.Data = temp;
                
                return ret;
            }
            catch (Exception e)
            {
                if (logging != null) logging.LogException("DataBlock", "FromBytes", e);
                else LoggingModule.ConsoleException("DataBlock", "FromBytes", e);
                throw;
            }
        }

        /// <summary>
        /// Creates a new data block instance from a position within the container file.
        /// </summary>
        /// <param name="fs">The FileStream instance to use.</param>
        /// <param name="blockSize">The block size, in bytes.</param>
        /// <param name="position">The position of the data within the container file.</param>
        /// <param name="logging">Instance of LoggingModule to use for logging events.</param>
        /// <returns>A populated DataBlock object.</returns>
        public static DataBlock FromPosition(FileStream fs, int blockSize, long position, LoggingModule logging)
        {
            try
            {
                if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
                if (blockSize < 4096) throw new ArgumentOutOfRangeException("Block size must be greater than or equal to 4096");
                if (blockSize % 4096 != 0) throw new ArgumentOutOfRangeException("Block size must be evenly divisible by 4096");
                if (fs == null) throw new ArgumentNullException(nameof(fs));

                byte[] data = CfsCommon.ReadFromPosition(fs, position, blockSize);
                return FromBytes(fs, blockSize, data, logging);
            }
            catch (Exception e)
            {
                if (logging != null) logging.LogException("DataBlock", "FromPosition", e);
                else LoggingModule.ConsoleException("DataBlock", "FromPosition", e);
                throw;
            }
        }

        #endregion
    }
}
