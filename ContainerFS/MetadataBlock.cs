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
    /// Block of metadata.
    /// </summary>
    public class MetadataBlock
    {
        #region Private-Members

        private LoggingModule Logging;
        private int BlockSizeBytes { get; set; }
        private string DateTimeFormat = "MM/dd/yyyy HH:mm:ss.ffffff";
        
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
        /// The position of the child data block.
        /// </summary>
        public long ChildDataBlock { get; set; }        // 8 bytes @ 12

        /// <summary>
        /// The full length of the data represented by this metadata block.
        /// </summary>
        public long FullDataLength { get; set; }        // 8 bytes @ 20

        /// <summary>
        /// The amount of data written to this block.
        /// </summary>
        public int LocalDataLength { get; set; }        // 4 bytes @ 28

        /// <summary>
        /// Indicates if this metadata block describes a directory.
        /// </summary>
        public int IsDirectory { get; set; }            // 4 bytes @ 32

        /// <summary>
        /// Indicates if this metadata block describes a file.
        /// </summary>
        public int IsFile { get; set; }                 // 4 bytes @ 36

        /// <summary>
        /// The name of the file or directory.
        /// </summary>
        public string Name { get; set; }                // 256 bytes @ 40

        /// <summary>
        /// The UTC time when the object was created.
        /// </summary>
        public string CreatedUtc { get; set; }          // 32 bytes @ 296

        /// <summary>
        /// The UTC time when the object was last updated
        /// </summary>
        public string LastUpdateUtc { get; set; }       // 32 bytes @ 328

        /// <summary>
        /// Byte data stored in this block.
        /// </summary>
        public byte[] Data { get; set; }                // from 512 on

        #endregion

        #region Static-Members

        /// <summary>
        /// The signature found in the first four bytes of a metadata block.
        /// </summary>
        public static byte[] SignatureBytes = new byte[4] { 0x0F, 0x0F, 0x0F, 0x0F };
        
        /// <summary>
        /// The number of bytes reserved at the beginning of a block for metadata.
        /// </summary>
        public static int BytesReservedPerBlock = 512;

        #endregion

        #region Constructors

        /// <summary>
        /// Parameterless constructor.  Please do not use.
        /// </summary>
        public MetadataBlock()
        {
        }

        /// <summary>
        /// Create a new metadata block.
        /// </summary>
        /// <param name="fs">The FileStream instance to use.</param>
        /// <param name="blockSize">The block size, in bytes.</param>
        /// <param name="parentBlock">The position of the parent block.</param>
        /// <param name="childDataBlock">The position of the child data block.</param>
        /// <param name="fullDataLength">The full length of the data represented by this metadata block.</param>
        /// <param name="isDirectory">Indicates if this metadata block describes a directory.</param>
        /// <param name="isFile">Indicates if this metadata block describes a file.</param>
        /// <param name="name">The name of the file or directory.</param>
        /// <param name="data">Byte data stored in this block.</param>
        /// <param name="logging">Instance of LoggingModule to use for logging events.</param>
        public MetadataBlock(
            FileStream fs,
            int blockSize,
            long parentBlock,
            long childDataBlock,
            long fullDataLength,
            int isDirectory,
            int isFile,
            string name,
            byte[] data,
            LoggingModule logging)
        {
            if (fs == null) throw new ArgumentNullException(nameof(fs));
            if (blockSize < 4096) throw new ArgumentOutOfRangeException("Block size must be greater than or equal to 4096");
            if (blockSize % 4096 != 0) throw new ArgumentOutOfRangeException("Block size must be evenly divisible by 4096");
            if (!CfsCommon.IsTrue(isFile) && !CfsCommon.IsTrue(isDirectory)) throw new ArgumentException("Either isFile or isDirectory must be set");
            if (CfsCommon.IsTrue(isFile) && CfsCommon.IsTrue(isDirectory)) throw new ArgumentException("Only one of isFile and isDirectory must be set");

            Logging = logging;
            BlockSizeBytes = blockSize;
            ParentBlock = parentBlock;
            ChildDataBlock = childDataBlock;

            if (data == null) LocalDataLength = 0;
            else LocalDataLength = data.Length;
            Data = data;

            FullDataLength = fullDataLength;
            IsDirectory = isDirectory;
            IsFile = isFile;
            Name = name;
            DateTime ts = DateTime.Now.ToUniversalTime();
            CreatedUtc = ts.ToString(DateTimeFormat);
            LastUpdateUtc = ts.ToString(DateTimeFormat);
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
            ret += "Metadata Block  : " + Environment.NewLine;
            ret += "  Parent Block  : " + ParentBlock + Environment.NewLine;
            ret += "  Data Block    : " + ChildDataBlock + Environment.NewLine;
            ret += "  Is Directory  : " + IsDirectory + Environment.NewLine;
            ret += "  Is File       : " + IsFile + Environment.NewLine;
            ret += "  Name          : " + Name + Environment.NewLine;
            ret += "  Full Data     : " + FullDataLength + " bytes" + Environment.NewLine;
            ret += "  Local Data    : " + LocalDataLength + " bytes" + Environment.NewLine;
            ret += "  Created       : " + CreatedUtc + Environment.NewLine;
            ret += "  Last Update   : " + LastUpdateUtc + Environment.NewLine;
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
                Buffer.BlockCopy(MetadataBlock.SignatureBytes, 0, ret, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(ParentBlock), 0, ret, 4, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(ChildDataBlock), 0, ret, 12, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(FullDataLength), 0, ret, 20, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(LocalDataLength), 0, ret, 28, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(IsDirectory), 0, ret, 32, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(IsFile), 0, ret, 36, 4);

                if (Name.Length > 256) Name = Name.Substring(0, 256);
                byte[] nameByteArray = Encoding.UTF8.GetBytes(Name);
                byte[] nameBytesFixed = new byte[256];
                Buffer.BlockCopy(nameByteArray, 0, nameBytesFixed, 0, nameByteArray.Length);
                Buffer.BlockCopy(nameBytesFixed, 0, ret, 40, 256);

                byte[] tsByteArray = Encoding.UTF8.GetBytes(CreatedUtc);
                byte[] tsBytesFixed = CfsCommon.InitByteArray(32, 0x00);
                Buffer.BlockCopy(tsByteArray, 0, tsBytesFixed, 0, tsByteArray.Length);
                Buffer.BlockCopy(tsBytesFixed, 0, ret, 296, 32);

                tsByteArray = Encoding.UTF8.GetBytes(LastUpdateUtc);
                tsBytesFixed = CfsCommon.InitByteArray(32, 0x00);
                Buffer.BlockCopy(tsByteArray, 0, tsBytesFixed, 0, tsByteArray.Length);
                Buffer.BlockCopy(tsBytesFixed, 0, ret, 328, 32);

                if (Data != null)
                {
                    LogDebug("ToBytes copying data of length " + Data.Length + " to position 512");
                    Buffer.BlockCopy(Data, 0, ret, 512, Data.Length);
                }

                return ret;
            }
            catch (Exception e)
            {
                if (Logging != null) Logging.LogException("MetadataBlock", "ToBytes", e);
                else LoggingModule.ConsoleException("MetadataBlock", "ToBytes", e);
                throw;
            }
        }

        /// <summary>
        /// Retieve the positions of all linked metadata entries found in this block and associated child data blocks.
        /// </summary>
        /// <returns>Array of Long.</returns>
        public long[] GetMetadataBlocks()
        {
            if (CfsCommon.IsTrue(IsFile)) throw new InvalidOperationException("Get metadata blocks must only be called on directory metadata blocks");

            List<long> ret = new List<long>();
            if (Data == null || Data.Length < 8) return ret.ToArray();
            int pos = 0;

            LogDebug("GetMetadataBlocks retrieving metadata blocks from data length of " + Data.Length);

            while (pos < Data.Length)
            {
                byte[] currBlock = new byte[8];
                Buffer.BlockCopy(Data, pos, currBlock, 0, 8);
                Int64 block = BitConverter.ToInt64(currBlock, 0);

                LogDebug("GetMetadataBlocks adding metadata block position " + block);
                ret.Add(block);
                pos += 8;
            }

            if (ChildDataBlock > 0)
            {
                LogDebug("GetMetadataBlocks child data block referenced at " + ChildDataBlock);
                byte[] nextBlockBytes = CfsCommon.ReadFromPosition(Filestream, ChildDataBlock, 512);
                MetadataBlock nextBlock = MetadataBlock.FromBytes(Filestream, BlockSizeBytes, nextBlockBytes, Logging);
                nextBlock.Filestream = Filestream;
                long[] childBlocks = nextBlock.GetMetadataBlocks();

                foreach (long curr in childBlocks)
                {
                    ret.Add(curr);
                }
            }

            return ret.ToArray();
        }

        /// <summary>
        /// Retrieve all byte data contained in this block and associated child data blocks.
        /// </summary>
        /// <returns>Byte array.</returns>
        public byte[] GetAllData()
        {
            if (CfsCommon.IsTrue(IsDirectory)) throw new InvalidOperationException("Get all data must only be called on file metadata blocks");

            if (Data == null || Data.Length < 1) return null;

            byte[] localData = new byte[LocalDataLength];
            Buffer.BlockCopy(Data, 0, localData, 0, LocalDataLength);

            if (ChildDataBlock > 0)
            {
                // get child data
                byte[] nextBytes = CfsCommon.ReadFromPosition(Filestream, ChildDataBlock, 64);
                DataBlock nextBlock = DataBlock.FromBytes(Filestream, BlockSizeBytes, nextBytes, Logging);
                nextBlock.Filestream = Filestream;
                byte[] childData = nextBlock.GetAllData();

                // join
                if (childData != null)
                {
                    byte[] ret = new byte[(localData.Length + childData.Length)];
                    Buffer.BlockCopy(localData, 0, ret, 0, localData.Length);
                    Buffer.BlockCopy(childData, 0, ret, localData.Length, childData.Length);
                    return ret;
                }
                else
                {
                    return localData;
                }
            }
            else
            {
                return localData;
            }
        }

        /// <summary>
        /// Retrieve data from the specified range.
        /// </summary>
        /// <param name="startPosition">The starting position from which to read.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>Byte data for the requested range.</returns>
        public byte[] GetData(long startPosition, long count)
        {
            if (CfsCommon.IsTrue(IsDirectory)) throw new InvalidOperationException("Get data must only be called on file metadata blocks");

            if (Data == null || Data.Length < 1) return null;
            
            byte[] localData = new byte[LocalDataLength];
            Buffer.BlockCopy(Data, 0, localData, 0, LocalDataLength);

            if (ChildDataBlock > 0)
            {
                // get child data
                byte[] nextBytes = CfsCommon.ReadFromPosition(Filestream, ChildDataBlock, 64);
                DataBlock nextBlock = DataBlock.FromBytes(Filestream, BlockSizeBytes, nextBytes, Logging);
                nextBlock.Filestream = Filestream;
                byte[] childData = nextBlock.GetAllData();

                // join
                if (childData != null)
                {
                    byte[] ret = new byte[(localData.Length + childData.Length)];
                    Buffer.BlockCopy(localData, 0, ret, 0, localData.Length);
                    Buffer.BlockCopy(childData, 0, ret, localData.Length, childData.Length);
                    return ret;
                }
                else
                {
                    return localData;
                }
            }
            else
            {
                return localData;
            }

        }

        /// <summary>
        /// Retrieve the number of data blocks associated with the metadata object.
        /// </summary>
        /// <returns>Long.</returns>
        public long GetDataBlockCount()
        {
            if (ChildDataBlock < 1) return 0;

            int numBlocks = 0;
            long currPosition = ChildDataBlock;
            while (true)
            {
                DataBlock curr = DataBlock.FromPosition(Filestream, BlockSizeBytes, currPosition, Logging);
                if (curr == null)
                {
                    LogDebug("GetDataBlockCount unable to retrieve data block at position " + currPosition);
                    throw new IOException("Unable to retrieve data from position " + currPosition);
                }

                numBlocks++;
                if (curr.ChildBlock < 1)
                {
                    LogDebug("GetDataBlockCount reached end of data block chain at position " + currPosition);
                    break;
                }

                currPosition = curr.ChildBlock;
            }

            LogDebug("GetDataBlockCount metadata contains " + numBlocks + " child data blocks");
            return numBlocks;
        }

        #endregion

        #region Private-Members

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
        /// Create a new metadata block instance from a byte array from data found within the container file.
        /// </summary>
        /// <param name="fs">The FileStream instance to use.</param>
        /// <param name="blockSize">The block size, in bytes.</param>
        /// <param name="ba">The byte array containing the full block.</param>
        /// <param name="logging">Instance of LoggingModule to use for logging events.</param>
        /// <returns>A populated MetadataBlock object.</returns>
        public static MetadataBlock FromBytes(FileStream fs, int blockSize, byte[] ba, LoggingModule logging)
        {
            try
            {
                if (ba == null || ba.Length < 1) throw new ArgumentNullException(nameof(ba));
                if (ba.Length < 64) throw new ArgumentException("Byte array has length less than 64");
                if (blockSize < 4096) throw new ArgumentOutOfRangeException("Block size must be greater than or equal to 4096");
                if (blockSize % 4096 != 0) throw new ArgumentOutOfRangeException("Block size must be evenly divisible by 4096");
                if (fs == null) throw new ArgumentNullException(nameof(fs));

                MetadataBlock ret = new MetadataBlock();
                ret.Signature = MetadataBlock.SignatureBytes;
                ret.BlockSizeBytes = blockSize;
                ret.Logging = logging;
                ret.Filestream = fs;

                byte[] temp;
                
                Buffer.BlockCopy(ba, 0, ret.Signature, 0, 4);
         
                temp = new byte[8];
                Buffer.BlockCopy(ba, 4, temp, 0, 8);
                ret.ParentBlock = BitConverter.ToInt64(temp, 0);

                temp = new byte[8];
                Buffer.BlockCopy(ba, 12, temp, 0, 8);
                ret.ChildDataBlock = BitConverter.ToInt64(temp, 0);

                temp = new byte[4];
                Buffer.BlockCopy(ba, 20, temp, 0, 4);
                ret.FullDataLength = BitConverter.ToInt32(temp, 0);

                temp = new byte[4];
                Buffer.BlockCopy(ba, 28, temp, 0, 4);
                ret.LocalDataLength = BitConverter.ToInt32(temp, 0);

                temp = new byte[4];
                Buffer.BlockCopy(ba, 32, temp, 0, 4);
                ret.IsDirectory = BitConverter.ToInt32(temp, 0);

                temp = new byte[4];
                Buffer.BlockCopy(ba, 36, temp, 0, 4);
                ret.IsFile = BitConverter.ToInt32(temp, 0);

                temp = new byte[256];
                Buffer.BlockCopy(ba, 40, temp, 0, 256);
                ret.Name = Encoding.UTF8.GetString(CfsCommon.TrimNullBytes(temp)).Trim();

                temp = new byte[32];
                Buffer.BlockCopy(ba, 296, temp, 0, 32);
                ret.CreatedUtc = Encoding.UTF8.GetString(CfsCommon.TrimNullBytes(temp)).Trim();

                temp = new byte[32];
                Buffer.BlockCopy(ba, 328, temp, 0, 32);
                ret.LastUpdateUtc = Encoding.UTF8.GetString(CfsCommon.TrimNullBytes(temp)).Trim();

                temp = new byte[ret.LocalDataLength];
                Buffer.BlockCopy(ba, 512, temp, 0, ret.LocalDataLength);
                ret.Data = temp;

                return ret;
            }
            catch (Exception e)
            {
                if (logging != null) logging.LogException("MetadataBlock", "FromBytes", e);
                else LoggingModule.ConsoleException("MetadataBlock", "FromBytes", e);
                throw;
            }
        }

        /// <summary>
        /// Creates a new metadata block instance from a position within the container file.
        /// </summary>
        /// <param name="fs">The FileStream instance to use.</param>
        /// <param name="blockSize">The block size, in bytes.</param>
        /// <param name="position">The position of the data within the container file.</param>
        /// <param name="logging">Instance of LoggingModule to use for logging events.</param>
        /// <returns>A populated MetadataBlock object.</returns>
        public static MetadataBlock FromPosition(FileStream fs, int blockSize, long position, LoggingModule logging)
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
                if (logging != null) logging.LogException("MetadataBlock", "FromPosition", e);
                else LoggingModule.ConsoleException("MetadataBlock", "FromPosition", e);
                throw;
            }
        }

        #endregion
    }
}
