using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SyslogLogging;

namespace ContainerFS
{
    public class Container
    {
        #region Private-Members

        private string Filename { get; set; }     
        private long TotalSizeBytes { get; set; }
        private long CurrentFileSizeBytes { get; set; }
        private FileStream Filestream { get; set; }
        private BitArray UnusedBlocks { get; set; }      // (blockCount / 8) @ 1024
        private LoggingModule Logging { get; set; }
        private string DateTimeFormat = "MM/dd/yyyy HH:mm:ss.ffffff";

        private byte[] Signature { get; set; }           // 4 bytes @ 0
        private int Version { get; set; }                // 4 bytes @ 8
        private string Name { get; set; }                // max 256 bytes @ 16 
        private int BlockSizeBytes { get; set; }         // 4 bytes @ 288 
        private int BlockCount { get; set; }             // 4 bytes @ 296 
        private string CreatedUtc { get; set; }        // 32 bytes @ 304

        #endregion

        #region Static-Members

        /// <summary>
        /// The signature found in the first four bytes of a container header block.
        /// </summary>
        public static byte[] SignatureBytes = new byte[4] { 0x01, 0x01, 0x01, 0x01 };

        #endregion

        #region Constructors

        /// <summary>
        /// Parameterless constructor.  Please do not use.
        /// </summary>
        public Container()
        {
        }

        /// <summary>
        /// Create a new container.
        /// </summary>
        /// <param name="filename">The name of the file to use.</param>
        /// <param name="blockSize">The block size, in bytes.</param>
        /// <param name="blockCount">The number of blocks.</param>
        /// <param name="loggingEnable">Whether or not you wish to have log statements sent to the console and localhost syslog.</param>
        public Container(string filename, string name, int blockSize, int blockCount, bool loggingEnable)
        {
            #region Check-Inputs

            if (String.IsNullOrEmpty(nameof(filename))) throw new ArgumentNullException(nameof(filename));
            if (String.IsNullOrEmpty(nameof(name))) throw new ArgumentNullException(nameof(name));
            if (blockSize < 4096) throw new ArgumentOutOfRangeException("Block size must be at least 4096 bytes");
            if (blockSize % 4096 != 0) throw new ArgumentOutOfRangeException("Block size must be divisible evenly by 4096 bytes");
            if (blockCount < 4096) throw new ArgumentOutOfRangeException("Block count must be at least 4096 blocks");
            if (blockCount % 4096 != 0) throw new ArgumentOutOfRangeException("Block count must be divisible evenly by 4096 bytes");
            if (blockSize < (blockCount / 4)) throw new ArgumentOutOfRangeException("Block size must be at least 1/4 the size of block count");
            if (File.Exists(filename)) throw new IOException("File already exists");

            #endregion

            #region Initialize-Logging

            if (loggingEnable) Logging = new LoggingModule("127.0.0.1", 514, true, LoggingModule.Severity.Debug, false, true, true, true, true, true);
            else Logging = null;

            #endregion

            #region Set-Values

            Filename = filename;
            TotalSizeBytes = (blockSize * blockCount);
            Filestream = new FileStream(filename, FileMode.CreateNew);
            CurrentFileSizeBytes = (blockSize * 256);
            SetFileSize(blockSize * 256);
            Version = 1;
            Name = name;
            BlockSizeBytes = blockSize;
            BlockCount = blockCount;
            CreatedUtc = DateTime.Now.ToUniversalTime().ToString(DateTimeFormat);

            #endregion

            #region Set-Unused-Blocks-Map

            UnusedBlocks = new BitArray(blockCount);
            for (int i = 0; i < blockCount; i++)
            {
                if (i > 1) UnusedBlocks[i] = true;
                else UnusedBlocks[i] = false;
            }
            
            #endregion

            #region Initialize-File

            // block 0: header
            CfsCommon.WriteAtPosition(Filestream, 0, ToBytes());
            SetBlockUsed(0);

            // block 1: root
            long rootPosition = blockSize;
            MetadataBlock rootMetadataBlock = new MetadataBlock(Filestream, blockSize, 0, -1, 0, 1, 0, ".", null, Logging);
            CfsCommon.WriteAtPosition(Filestream, rootPosition, rootMetadataBlock.ToBytes());
            SetBlockUsed(1);

            #endregion

            LogDebug("Container initialized at " + filename);
            LogDebug(ToString());
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// User-readable string containing details about the container.
        /// </summary>
        /// <returns>User-readable string containing details about the container.</returns>
        public override string ToString()
        {
            string ret = "---" + Environment.NewLine;
            ret += "Container Header: " + Environment.NewLine;
            ret += "  Version       : " + Version + Environment.NewLine;
            ret += "  Name          : " + Name + Environment.NewLine;
            ret += "  Block Size    : " + BlockSizeBytes + " bytes" + Environment.NewLine;
            ret += "  Block Count   : " + BlockCount + Environment.NewLine;
            ret += "  Unused Blocks : " + GetUnusedBlocks() + Environment.NewLine;
            ret += "  Total Size    : " + TotalSizeBytes + " bytes " + Environment.NewLine;
            ret += "  Free Space    : " + GetFreeSpace() + " bytes " + Environment.NewLine;
            ret += "  Created       : " + CreatedUtc + Environment.NewLine;
            Console.WriteLine("");
            return ret;
        }

        /// <summary>
        /// Retrieve the total number of blocks in the container.
        /// </summary>
        /// <returns>Integer.</returns>
        public int GetTotalBlocks()
        {
            return BlockCount;
        }

        /// <summary>
        /// Retrieve the number of unused, unallocated blocks in the container.
        /// </summary>
        /// <returns>Integer.</returns>
        public int GetUnusedBlocks()
        {
            int unused = 0;
            for (int i = 0; i < BlockCount; i++)
            {
                if (UnusedBlocks[i]) unused++;
            }
            return unused;
        } 

        /// <summary>
        /// Retrieve the total capacity of the container.
        /// </summary>
        /// <returns>Long.</returns>
        public long GetTotalSpace()
        {
            return BlockCount * BlockSizeBytes;
        }

        /// <summary>
        /// Retrieve the total unallocated, unused capacity of the container.
        /// </summary>
        /// <returns>Long.</returns>
        public long GetFreeSpace()
        {
            int unused = GetUnusedBlocks();
            return unused * BlockSizeBytes;
        }

        /// <summary>
        /// Read a specific block's byte data by its block index.
        /// </summary>
        /// <param name="id">The index of the block.</param>
        /// <returns>Byte data containing the block.</returns>
        public byte[] ReadRawBlockByIndex(int id)
        {
            return ReadRawBlock((long)(BlockSizeBytes * id));
        }

        /// <summary>
        /// Read a specific block's byte data by its byte position in the container file.
        /// </summary>
        /// <param name="position">The position of the block.</param>
        /// <returns>Byte data containing the block.</returns>
        public byte[] ReadRawBlock(long position)
        {
            if (position < 0) throw new ArgumentOutOfRangeException("Position must be greater than or equal to zero");
            if (position % BlockSizeBytes != 0) throw new ArgumentOutOfRangeException("Position must be evenly divisble by " + BlockSizeBytes);

            byte[] ret = CfsCommon.ReadFromPosition(Filestream, position, BlockSizeBytes);
            return ret;
        }

        /// <summary>
        /// Display statistics about a block found at a specific block index within the container file.
        /// </summary>
        /// <param name="id">The index of the block.</param>
        /// <returns>String.</returns>
        public string EnumerateBlockByIndex(int id)
        {
            return EnumerateBlock((long)(BlockSizeBytes * id));
        }

        /// <summary>
        /// Display statistics about a block by its byte position in the container file.
        /// </summary>
        /// <param name="position">The position of the block.</param>
        /// <returns>String.</returns>
        public string EnumerateBlock(long position)
        {
            if (position < 0) throw new ArgumentOutOfRangeException("Position must be greater than or equal to zero");
            if (position % BlockSizeBytes != 0) throw new ArgumentOutOfRangeException("Position must be evenly divisble by " + BlockSizeBytes);

            byte[] data = CfsCommon.ReadFromPosition(Filestream, position, BlockSizeBytes);
            if (data == null || data.Length < 4)
            {
                LogDebug("EnumerateBlock data returned is null or too small");
                throw new IOException("EnumerateBlock data returned is null or too small");
            }

            byte[] signature = new byte[4];
            Buffer.BlockCopy(data, 0, signature, 0, 4);

            if (CfsCommon.ByteArraysIdentical(signature, Container.SignatureBytes))
            {
                // header
                LogDebug("EnumerateBlock position " + position + " contains container metadata");
                Container tempContainer = Container.FromBytes(CfsCommon.ReadFromPosition(Filestream, position, BlockSizeBytes));
                return tempContainer.ToString();
            }
            else if (CfsCommon.ByteArraysIdentical(signature, MetadataBlock.SignatureBytes))
            {
                // metadata
                LogDebug("EnumerateBlock position " + position + " contains metadata block");
                MetadataBlock tempMetadata = MetadataBlock.FromPosition(Filestream, BlockSizeBytes, position, Logging);
                return tempMetadata.ToString();
            }
            else if (CfsCommon.ByteArraysIdentical(signature, DataBlock.SignatureBytes))
            {
                // data
                LogDebug("EnumerateBlock position " + position + " contains data block");
                DataBlock tempData = DataBlock.FromPosition(Filestream, BlockSizeBytes, position, Logging);
                return tempData.ToString();
            }

            LogDebug("EnumerateBlock unknown block type");
            throw new IOException("Unknown block type");
        }

        /// <summary>
        /// Read the contents of a file.
        /// </summary>
        /// <param name="path">The directory path, i.e. / or /directory.</param>
        /// <param name="name">The name of the file.</param>
        /// <returns>Byte data for the entire file.</returns>
        public byte[] ReadFile(string path, string name)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (String.IsNullOrEmpty(path)) path = "/";

            // check if path exists
            long currPosition = 0;
            MetadataBlock dirMetadata = FindDirectoryMetadata(path, out currPosition);
            if (dirMetadata == null)
            {
                LogDebug("ReadFile unable to find " + path);
                throw new DirectoryNotFoundException(path);
            }
            else
            {
                LogDebug("ReadFile found parent directory at position " + currPosition);
            }

            // check if file exists in path
            long filePosition = 0;
            if (!FindFileMetadata(path, name, out filePosition))
            {
                LogDebug("ReadFile file " + name + " does not exist");
                throw new FileNotFoundException("File does not exist");
            }

            // read file metadata
            MetadataBlock fileMetadata = MetadataBlock.FromPosition(Filestream, BlockSizeBytes, filePosition, Logging);
            if (fileMetadata == null)
            {
                LogDebug("ReadFile unable to retrieve file metadata for " + name);
                throw new FileNotFoundException("File does not exist");
            }

            // ReadDataFromMetadata
            byte[] fileData = fileMetadata.GetAllData();
            if (fileData != null)
            {
                LogDebug("ReadFile successfully read file " + name + " (" + fileData.Length + " bytes)");
            }
            else
            {
                LogDebug("ReadFile successfully read file " + name + " (no data)");
            }
            return fileData;
        }

        /// <summary>
        /// Read a byte range within a file.
        /// </summary>
        /// <param name="path">The directory path, i.e. / or /directory.</param>
        /// <param name="name">The name of the file.</param>
        /// <param name="startPosition">The starting position from which to read.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>Byte data for the requested range.</returns>
        public byte[] ReadFile(string path, string name, long startPosition, long count)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (String.IsNullOrEmpty(path)) path = "/";

            // check if path exists
            long currPosition = 0;
            MetadataBlock dirMetadata = FindDirectoryMetadata(path, out currPosition);
            if (dirMetadata == null)
            {
                LogDebug("ReadFile unable to find " + path);
                throw new DirectoryNotFoundException(path);
            }
            else
            {
                LogDebug("ReadFile found parent directory at position " + currPosition);
            }

            // check if file exists in path
            long filePosition = 0;
            if (!FindFileMetadata(path, name, out filePosition))
            {
                LogDebug("ReadFile file " + name + " does not exist");
                throw new FileNotFoundException("File does not exist");
            }

            // read file metadata
            MetadataBlock fileMetadata = MetadataBlock.FromPosition(Filestream, BlockSizeBytes, filePosition, Logging);
            if (fileMetadata == null)
            {
                LogDebug("ReadFile unable to retrieve file metadata for " + name);
                throw new FileNotFoundException("File does not exist");
            }

            // check if position out of range
            if ((startPosition < 0) || (startPosition > fileMetadata.FullDataLength))
            {
                LogDebug("ReadFile requested start position out of range");
                throw new IOException("Out of range");
            }

            // check if sum out of range
            if ((startPosition + count) > fileMetadata.FullDataLength)
            {
                LogDebug("ReadFile requested byte range exceeds file length");
                throw new IOException("File length exceeded");
            }
            
            // ReadDataFromMetadata
            byte[] fileData = fileMetadata.GetAllData();
            if (fileData != null)
            {
                LogDebug("ReadFile successfully read file " + name + " (" + fileData.Length + " bytes)");
            }
            else
            {
                LogDebug("ReadFile successfully read file " + name + " (no data)");
            }

            byte[] ret = new byte[(int)count];
            Buffer.BlockCopy(fileData, (int)startPosition, ret, 0, (int)count);
            return ret;
        }

        /// <summary>
        /// Write a file to the container at a specified path and filename.
        /// </summary>
        /// <param name="path">The directory path, i.e. / or /directory.</param>
        /// <param name="name">The name of the file.</param>
        /// <param name="data">Byte data for the entire file.</param>
        public void WriteFile(string path, string name, byte[] data)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (String.IsNullOrEmpty(path)) path = "/";

            // check if path exists
            long currPosition = 0;
            MetadataBlock dir = FindDirectoryMetadata(path, out currPosition);
            if (dir == null)
            {
                LogDebug("WriteFile unable to find " + path);
                throw new DirectoryNotFoundException(path);
            }
            else
            {
                LogDebug("WriteFile found parent directory at position " + currPosition);
            }
            
            // check if file exists in path
            if (FileExists(path, name))
            {
                LogDebug("WriteFile file " + name + " already exists");
                throw new IOException("File already exists");
            }

            // check if free space is available
            int blocksRequired = DataBlocksRequired(data.Length - (BlockSizeBytes - MetadataBlock.BytesReservedPerBlock)); 
            int unusedBlocks = GetUnusedBlocks();
            if (unusedBlocks < blocksRequired)
            {
                LogDebug("WriteFile insufficient capacity to write " + data.Length + " bytes for file " + name);
                throw new IOException("Disk is full");
            }

            // allocate blocks
            List<long> allocatedPositions = new List<long>();
            allocatedPositions = AllocateBlocks(blocksRequired + 1); // +1 for the metadata position
            if (allocatedPositions.Count < 1)
            {
                LogDebug("WriteFile unable to retrieve " + blocksRequired + " blocks");
                throw new IOException("Unable to allocate blocks");
            }
            long metadataPosition = allocatedPositions[0];
            long childDataPosition = -1;
            if (allocatedPositions.Count > 1) childDataPosition = allocatedPositions[1];    // set up first child data ock

            // define pointers
            int currDataPointer = 0;
            int dataRemaining = data.Length;

            // build buffer for data contained within metadata block
            byte[] buffer = new byte[(BlockSizeBytes - MetadataBlock.BytesReservedPerBlock)];
            buffer = CfsCommon.InitByteArray(buffer.Length, 0x00);

            if (dataRemaining <= buffer.Length) Buffer.BlockCopy(data, 0, buffer, 0, dataRemaining);
            else Buffer.BlockCopy(data, 0, buffer, 0, (BlockSizeBytes - MetadataBlock.BytesReservedPerBlock));

            // write the metadata object and set local data length
            MetadataBlock md = new MetadataBlock(Filestream, BlockSizeBytes, currPosition, childDataPosition, data.Length, 0, 1, name, buffer, Logging);
            if (dataRemaining <= buffer.Length) md.LocalDataLength = data.Length;
            else md.LocalDataLength = BlockSizeBytes - MetadataBlock.BytesReservedPerBlock;
            
            // update pointers for metadata data payload
            currDataPointer += buffer.Length;
            dataRemaining -= buffer.Length;

            if (allocatedPositions.Count > 1)
            {
                #region Write-Data-Blocks

                LogDebug("WriteFile file requires " + (allocatedPositions.Count - 1) + " data blocks");

                // write to data blocks
                for (int i = 1; i < allocatedPositions.Count; i++) 
                {
                    int blockSize = (BlockSizeBytes - DataBlock.BytesReservedPerBlock);
                    if (blockSize > dataRemaining) blockSize = (int)dataRemaining;

                    buffer = new byte[blockSize];
                    LogDebug("WriteFile copying " + blockSize + " bytes from data position " + currDataPointer);
                    Buffer.BlockCopy(data, currDataPointer, buffer, 0, blockSize);

                    DataBlock d = new DataBlock(Filestream, BlockSizeBytes, buffer, Logging);
                    if (i < (allocatedPositions.Count - 1)) d.ChildBlock = allocatedPositions[i + 1];
                    else d.ChildBlock = -1;
                    d.DataLength = blockSize;
                    d.ParentBlock = allocatedPositions[i - 1]; // safe, starting at i = 1

                    CfsCommon.WriteAtPosition(Filestream, allocatedPositions[i], d.ToBytes());
                    LogDebug("WriteFile write " + d.DataLength + " for " + name + " at position " + allocatedPositions[i]);

                    // shift pointers
                    currDataPointer += blockSize;
                    dataRemaining -= blockSize;
                }

                #endregion
            }
            else
            {
                // fits in metadata, assign values
                LogDebug("WriteFile file requires no data blocks");
                md.Data = data;
            }

            // write metadata
            LogDebug("WriteFile writing new file metadata to position " + allocatedPositions[0] + " for file " + md.Name);
            CfsCommon.WriteAtPosition(Filestream, allocatedPositions[0], md.ToBytes());

            // update directory with link
            AppendPositionToMetadata(currPosition, metadataPosition);

            return;
        }

        /// <summary>
        /// Delete a file at a specified path and filename.
        /// </summary>
        /// <param name="path">The directory path, i.e. / or /directory.</param>
        /// <param name="name">The name of the file.</param>
        public void DeleteFile(string path, string name)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (String.IsNullOrEmpty(path)) path = "/";

            // check if path exists
            long currPosition = 0;
            MetadataBlock dir = FindDirectoryMetadata(path, out currPosition);
            if (dir == null)
            {
                LogDebug("DeleteFile unable to find " + path);
                throw new DirectoryNotFoundException(path);
            }
            else
            {
                LogDebug("DeleteFile found parent directory at position " + currPosition);
            }

            // check if file exists in path
            if (!FileExists(path, name))
            {
                LogDebug("DeleteFile file " + name + " does not exist in path " + path);
                throw new FileNotFoundException("File does not exist");
            }

            // get metadata
            long metadataPosition = 0;
            if (!FindFileMetadata(path, name, out metadataPosition))
            {
                LogDebug("DeleteFile unable to retrieve metadata position for file " + name);
                throw new IOException("Unable to retrieve file metadata position");
            }

            MetadataBlock fileMetadata = MetadataBlock.FromPosition(Filestream, BlockSizeBytes, metadataPosition, Logging);
            if (fileMetadata == null)
            {
                LogDebug("DeleteFile unable to retrieve file metadata for file " + name);
                throw new IOException("Unable to retrieve file metadata");
            }

            List<long> dataPositions = GetFileDataBlockPositions(fileMetadata);

            // unlink records
            SetBlockUnused((int)(metadataPosition / 8));
            CfsCommon.WriteAtPosition(Filestream, metadataPosition, CfsCommon.InitByteArray(BlockSizeBytes, 0x00));

            if (dataPositions != null && dataPositions.Count > 0)
            {
                foreach (long curr in dataPositions)
                {
                    SetBlockUnused((int)(curr / 8));
                    CfsCommon.WriteAtPosition(Filestream, curr, CfsCommon.InitByteArray(BlockSizeBytes, 0x00));
                }
            }

            // unlink from directory metadata
            RemovePositionFromMetadata(currPosition, metadataPosition);
            return;
        }

        /// <summary>
        /// Read the contents (files, directories) of a specific directory.
        /// </summary>
        /// <param name="path">The directory path, i.e. / or /directory.</param>
        /// <param name="files">List of filenames contained in the directory (string).</param>
        /// <param name="directories">List of subdirectories contained in the directory (string).</param>
        /// <param name="position">The position of the block within the container (long).</param>
        public void ReadDirectory(string path, out List<Tuple<string, long>> files, out List<string> directories, out long position)
        {
            files = new List<Tuple<string, long>>();
            directories = new List<string>();
            if (String.IsNullOrEmpty(path)) path = "/";
            if (String.Compare(path, ".") == 0) path = "/";
            path = path.Trim();

            // check if path exists
            long currPosition = 0;
            MetadataBlock dir = FindDirectoryMetadata(path, out currPosition);
            if (dir == null)
            {
                LogDebug("ReadDirectory unable to find " + path);
                throw new DirectoryNotFoundException(path);
            }
            else
            {
                LogDebug("ReadDirectory found parent directory at position " + currPosition);
            }

            // read data
            if (!EnumerateDirectory(path, out files, out directories, out position)) throw new IOException("Unable to iterate directory");
            return;
        }

        /// <summary>
        /// Create a directory at a specific path.  The parent directory structure must already exist.
        /// </summary>
        /// <param name="path">The directory path, i.e. /directory or /directory/subdirectory.</param>
        public void WriteDirectory(string path)
        {
            if (String.IsNullOrEmpty(path)) path = "/";

            // parse and find parent
            string[] pathParts = ParsePath(path);
            if (pathParts == null || pathParts.Length < 1)
            {
                LogDebug("WriteDirectory root directory already exists");
                throw new IOException("Root directory already exists");
            }
            else
            {
                string msg = "WriteDirectory parsed directory into " + pathParts.Length + " components: ";
                foreach (string curr in pathParts) msg += curr + " ";
                LogDebug(msg);
            }

            // extract target directory name
            string targetDirectory = pathParts[(pathParts.Length - 1)];

            // check if parent path exists
            string[] parentArray = new string[(pathParts.Length - 1)];
            string parentPath = "";
            long currPosition = 0;

            // check if directory already exists
            long tempPosition;
            MetadataBlock tempMetadata = FindDirectoryMetadata(path, out tempPosition);
            if (tempMetadata != null)
            {
                LogDebug("WriteDirectory path " + path + " already exists");
                throw new IOException("Directory already exists");
            }

            MetadataBlock parentMetadata = null;
            if (parentArray.Length == 0)
            {
                #region Write-to-Root

                LogDebug("WriteDirectory new directory is a child of root directory");
                parentMetadata = FindDirectoryMetadata(parentPath, out currPosition);
                if (parentMetadata == null)
                {
                    LogDebug("WriteDirectory parent path " + parentPath + " does not exist");
                    throw new DirectoryNotFoundException();
                }

                #endregion
            }
            else
            {
                #region Write-to-Nested-Directory

                // get parent path
                for (int i = 0; i < parentArray.Length; i++)
                {
                    parentPath += "/" + pathParts[i];
                }

                LogDebug("WriteDirectory checking for existence of parent path " + parentPath);
                parentMetadata = FindDirectoryMetadata(parentPath, out currPosition);
                if (parentMetadata == null)
                {
                    LogDebug("WriteDirectory parent path " + parentPath + " does not exist");
                    throw new DirectoryNotFoundException();
                }
                else
                {
                    LogDebug("WriteDirectory retrieved parent path " + parentPath + ":" + parentMetadata.ToString());
                }

                // check if already exists
                List<Tuple<string, long>> files = new List<Tuple<string, long>>();
                List<string> directories = new List<string>();
                if (!EnumerateDirectory(parentPath, out files, out directories, out currPosition))
                {
                    LogDebug("WriteDirectory unable to enumerate " + parentPath);
                    throw new IOException("Unable to enumerate " + parentPath);
                }

                if (files != null && files.Count > 0)
                {
                    foreach (Tuple<string, long> curr in files)
                    {
                        if (String.Compare(curr.Item1.ToLower(), targetDirectory.ToLower()) == 0)
                        {
                            LogDebug("WriteDirectory " + targetDirectory + " file already exists in " + parentPath);
                            throw new IOException("File already exists");
                        }
                    }
                }

                if (directories != null && directories.Count > 0)
                {
                    foreach (string curr in directories)
                    {
                        if (String.Compare(curr.ToLower(), targetDirectory.ToLower()) == 0)
                        {
                            LogDebug("WriteDirectory " + targetDirectory + " directory already exists in " + parentPath);
                            throw new IOException("Directory already exists");
                        }
                    }
                }

                #endregion
            }

            // allocate block for new directory
            List<long> newPositions = AllocateBlocks(1);

            // append new block to existing metadata
            AppendPositionToMetadata(currPosition, newPositions[0]);

            // write new metadata entry
            MetadataBlock newDirectory = new MetadataBlock(Filestream, BlockSizeBytes, currPosition, -1, 0, 1, 0, targetDirectory, null, Logging);
            CfsCommon.WriteAtPosition(Filestream, newPositions[0], newDirectory.ToBytes());
            LogDebug("WriteDirectory successfully wrote diretory " + path + ":" + newDirectory.ToString());

            // return
            return;
        }

        /// <summary>
        /// Delete a directory at a specific path.  The directory must be empty (no files, no subdirectories).
        /// </summary>
        /// <param name="path">The directory path, i.e. /directory or /directory/subdirectory.</param>
        public void DeleteDirectory(string path)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));

            // check if path exists
            long currPosition = 0;
            MetadataBlock dir = FindDirectoryMetadata(path, out currPosition);
            if (dir == null)
            {
                LogDebug("DeleteDirectory directory " + path + " not found");
                throw new DirectoryNotFoundException(path);
            }
            else
            {
                LogDebug("DeleteDirectory found parent directory at position " + currPosition);
            }

            // enumerate directory
            List<Tuple<string, long>> files = null;
            List<string> directories = null;
            if (!EnumerateDirectory(path, out files, out directories, out currPosition))
            {
                LogDebug("DeleteDirectory unable to enumerate " + path);
                throw new DirectoryNotFoundException(path);
            }
            else
            {
                LogDebug("DeleteDirectory successfully read directory " + path);
            }

            MetadataBlock tempMetadata = MetadataBlock.FromPosition(Filestream, BlockSizeBytes, currPosition, Logging);
            if (tempMetadata == null)
            {
                LogDebug("DeleteDirectory unable to read child metadata at position " + currPosition);
                throw new IOException("Unable to read child metadata at position " + currPosition);
            }
            else
            {
                LogDebug("DeleteDirectory read directory metadata for " + path + ": " + tempMetadata.ToString());
            }
            
            if (!IsDirectoryEmpty(files, directories))
            {
                LogDebug("DeleteDirectory directory " + path + " is not empty");
                throw new IOException("Directory is not empty");
            }

            // deallocate blocks from parent metadata
            LogDebug("DeleteDirectory deallocating directory from position " + currPosition);
            DeallocateDirectory(dir, currPosition);

            // remove from the parent directory
            LogDebug("DeleteDirectory removing position " + currPosition + " from parent metadata at " + tempMetadata.ParentBlock);
            RemovePositionFromMetadata(tempMetadata.ParentBlock, currPosition);

            return;
        }

        #endregion

        #region Private-Methods

        private bool IsDirectoryEmpty(List<string> files, List<string> directories)
        {
            if (files != null && files.Count > 0)
            {
                LogDebug("IsDirectoryEmpty directory contains " + files.Count + " files");
                return false;
            }
            if (directories != null && directories.Count > 0)
            {
                LogDebug("IsDirectoryEmpty directory contains " + directories.Count + " directories");
                return false;
            }
            return true;
        }

        private bool IsDirectoryEmpty(List<Tuple<string, long>> files, List<string> directories)
        {
            if (files != null && files.Count > 0)
            {
                LogDebug("IsDirectoryEmpty directory contains " + files.Count + " files");
                return false;
            }
            if (directories != null && directories.Count > 0)
            {
                LogDebug("IsDirectoryEmpty directory contains " + directories.Count + " directories");
                return false;
            }
            return true;
        }

        private bool FileExists(string path, string name)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (String.IsNullOrEmpty(path)) path = "/";

            List<Tuple<string, long>> files;
            List<string> directories;
            long currPosition = 0;
            if (!EnumerateDirectory(path, out files, out directories, out currPosition)) throw new IOException("Unable to iterate directory");

            if (files == null || files.Count < 1)
            {
                LogDebug("FileExists " + path + " is empty");
                return false;
            }
            else
            {
                foreach (Tuple<string, long> curr in files)
                {
                    if (String.Compare(curr.Item1.ToLower().Trim(), curr.Item1.ToLower().Trim()) == 0)
                    {
                        LogDebug("FileExists " + name + " exists in path " + path);
                        return true;
                    }
                }

                LogDebug("FileExists " + name + " does not exist in " + path);
                return false;
            }
        }

        private bool FindFileMetadata(string path, string name, out long position)
        {
            position = 0;
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (String.IsNullOrEmpty(path)) path = "/";

            long currPosition = 0;
            MetadataBlock metadata = FindDirectoryMetadata(path, out currPosition);
            if (metadata == null)
            {
                LogDebug("FindFileMetadata unable to find " + path);
                throw new DirectoryNotFoundException(path);
            }
            else
            {
                LogDebug("FindFileMetadata found parent directory at position " + currPosition);
            }

            if (metadata == null)
            {
                LogDebug("FindFileMetadata " + path + " not found");
                return false;
            }

            if (metadata.LocalDataLength <= 0 && metadata.ChildDataBlock < 0)
            {
                LogDebug("FindFileMetadata " + path + " is empty");
                return false;
            }

            byte[] data = new byte[metadata.LocalDataLength];
            Buffer.BlockCopy(metadata.Data, 0, data, 0, metadata.LocalDataLength);

            // append from any child metadata blocks
            while (true)
            {
                if (metadata.ChildDataBlock <= 0) break;

                metadata = MetadataBlock.FromPosition(Filestream, BlockSizeBytes, metadata.ChildDataBlock, Logging);
                if (metadata == null)
                {
                    LogDebug("FindFileMetadata child md not found at " + metadata.ChildDataBlock);
                    return false; // directory not found
                }

                if (metadata.Data == null || metadata.Data.Length < 1 || metadata.LocalDataLength < 1)
                {
                    LogDebug("FindFileMetadata no data found in md at " + metadata.ChildDataBlock);
                    break;
                }

                byte[] oldData = new byte[data.Length];
                Buffer.BlockCopy(data, 0, oldData, 0, data.Length);

                data = new byte[oldData.Length + metadata.LocalDataLength];
                Buffer.BlockCopy(oldData, 0, data, 0, oldData.Length);
                Buffer.BlockCopy(metadata.Data, 0, data, oldData.Length, metadata.LocalDataLength);
            }

            // convert byte array to list long
            long[] addresses = BytesToArrayLong(data);
            if (addresses == null || addresses.Length < 1)
            {
                LogDebug("FindFileMetadata no data or invalid data found in node and children");
                return false;
            }

            for (int i = 0; i < addresses.Length; i++)
            {
                // each address is a metadata node for a child in this directory
                MetadataBlock currMetadata = MetadataBlock.FromPosition(Filestream, BlockSizeBytes, addresses[i], Logging);
                if (currMetadata == null)
                {
                    LogDebug("FindFileMetadata unable to read md linked in metdata at position " + addresses[i]);
                    return false;
                }

                if (CfsCommon.IsTrue(currMetadata.IsDirectory)) continue;
                if (CfsCommon.IsTrue(currMetadata.IsFile))
                {
                    if (String.Compare(currMetadata.Name, name) == 0)
                    {
                        position = addresses[i];
                        LogDebug("FindFileMetadata found file " + name + " at position " + addresses[i]);
                        return true;
                    }
                }
            }

            LogDebug("FindFileMetadata unable to find file " + name);
            return false;
        }

        private int DataBlocksRequired(long data)
        {
            int blocksRequired = (int)Math.Ceiling((double)data / (double)(BlockSizeBytes - DataBlock.BytesReservedPerBlock));
            LogDebug("DataBlocksRequired data length " + data + " requires " + blocksRequired + " data blocks");
            return blocksRequired;
        }

        private List<long> AllocateBlocks(int count)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            if (GetUnusedBlocks() < count)
            {
                LogDebug("AllocateBlocks not enough free blocks to reserve " + count + " blocks");
                throw new IOException("Disk is full");
            }

            List<long> allocatedPositions = new List<long>();
            List<int> allocatedIndices = new List<int>();
            bool allocated = false;

            // find unused blocks
            for (int i = 0; i < UnusedBlocks.Length; i++)
            {
                if (UnusedBlocks[i])
                {
                    allocatedPositions.Add(i * BlockSizeBytes);  // relative address
                    allocatedIndices.Add(i);                     // index, cannot modify while iterating
                    if (allocatedPositions.Count >= count)
                    {
                        allocated = true;
                        break;
                    }
                }
            }

            if (!allocated)
            {
                LogDebug("AllocateBlocks unable to allocate " + count + " blocks");
                throw new IOException("Unable to allocate blocks");
            }

            // mark blocks used
            foreach (int i in allocatedIndices)
            {
                SetBlockUsed(i);
            }

            string msg = "AllocateBlocks allocated " + allocatedPositions.Count + " blocks: ";
            foreach (int i in allocatedIndices) msg += i.ToString() + " ";
            LogDebug(msg);
            return allocatedPositions;
        }

        private void DeallocateBlocks(List<long> blocks)
        {
            if (blocks == null || blocks.Count < 1) return;
            foreach (long curr in blocks)
            {
                int position = (int)(curr / BlockSizeBytes);
                SetBlockUnused(position);
            }
            return;
        }
        
        private void DeallocateBlock(long block)
        {
            int position = (int)(block / BlockSizeBytes);
            SetBlockUnused(position);
            return;
        }

        private void DeallocateDirectory(MetadataBlock directory, long position)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));

            long childPosition = directory.ChildDataBlock;
            while (childPosition > 0)
            {
                DataBlock child = DataBlock.FromPosition(Filestream, BlockSizeBytes, childPosition, Logging);
                if (child == null)
                {
                    LogDebug("DeallocateDirectory unable to read linked child data block at " + childPosition);
                    throw new IOException("Unable to read linked child data block");
                }

                SetBlockUnused((int)(childPosition / 8));
                CfsCommon.WriteAtPosition(Filestream, childPosition, CfsCommon.InitByteArray(BlockSizeBytes, 0x00));
                childPosition = child.ChildBlock;
            }
        }

        private void AppendPositionToMetadata(long metadataPosition, long positionToAdd)
        {
            MetadataBlock curr = MetadataBlock.FromPosition(Filestream, BlockSizeBytes, metadataPosition, Logging);
            if (curr == null)
            {
                LogDebug("AppendPositionToMetadata unable to retrieve metadata from position " + metadataPosition);
                throw new IOException("Unable to retrieve metadata from position " + metadataPosition);
            }

            // retrieve positions and add new
            long[] positions = curr.GetMetadataBlocks();
            List<long> positionsList = new List<long>();
            if (positions != null)
            {
                positionsList = new List<long>(positions);
                positionsList.Add(positionToAdd);
                positions = positionsList.ToArray();
            }
            else
            {
                positionsList.Add(positionToAdd);
                positions = positionsList.ToArray();
            }

            // determine number of blocks required
            long sizeRequired = positionsList.Count * 8;
            int blocksRequired = 0;
            if (sizeRequired < (BlockSizeBytes - MetadataBlock.BytesReservedPerBlock))
            {
                LogDebug("AppendPositionToMetadata updated positions list fits into metadata block");
            }
            else
            {
                sizeRequired = sizeRequired - BlockSizeBytes;
                blocksRequired = (int)Math.Ceiling((double)sizeRequired / (double)(BlockSizeBytes - DataBlock.BytesReservedPerBlock));
                LogDebug("AppendPositionToMetadata updated positions list requires metadata block plus " + blocksRequired + " blocks");
            }
            
            // if fits into metadata block
            if (blocksRequired == 0)
            {
                // fits into metadata, just rewrite and return
                CfsCommon.WriteAtPosition(Filestream, metadataPosition, CfsCommon.InitByteArray(BlockSizeBytes - MetadataBlock.BytesReservedPerBlock, 0x00));
                curr.Data = ArrayLongToBytes(positionsList.ToArray());

                if (curr.Data != null) curr.LocalDataLength = curr.Data.Length;
                else curr.LocalDataLength = 0;

                CfsCommon.WriteAtPosition(Filestream, metadataPosition, curr.ToBytes());
                LogDebug("AppendPositionToMetadata successfully rewrote metadata block at position " + metadataPosition);
                return;
            }

            // allocate new blocks 
            List<long> allocatedPositionsList = AllocateBlocks(blocksRequired);
            long[] allocatedPositions = allocatedPositionsList.ToArray();

            // rewrite metadata
            int metadataBytesAvailable = BlockSizeBytes - MetadataBlock.BytesReservedPerBlock;
            int currPositionsPointer = 0;
            int currMetadataBlockPointer = 0;
            curr.Data = new byte[metadataBytesAvailable];
            curr.Data = CfsCommon.InitByteArray(metadataBytesAvailable, 0x00);
            while (metadataBytesAvailable >= 8)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(positions[currPositionsPointer]), 0, curr.Data, currMetadataBlockPointer, 8);
                currPositionsPointer++;
                currMetadataBlockPointer += 8;
            }
            curr.LocalDataLength = curr.Data.Length;
            CfsCommon.WriteAtPosition(Filestream, metadataPosition, curr.ToBytes());
            LogDebug("AppendPositionToMetadata successfully rewrote metadata block at position " + metadataPosition + ", continuing to data blocks");

            // write datablocks
            long currParent = metadataPosition;
            for (int i = 0; i < allocatedPositions.Length; i++)
            {
                int dataBytesAvailable = BlockSizeBytes - DataBlock.BytesReservedPerBlock;
                int currDataBlockPointer = 0;

                DataBlock d = new DataBlock(Filestream, BlockSizeBytes, CfsCommon.InitByteArray(dataBytesAvailable, 0x00), Logging);
                if (i < (allocatedPositions.Length - 1)) d.ChildBlock = allocatedPositions[i + 1];
                else d.ChildBlock = -1;
                d.ParentBlock = currParent;
                
                while (dataBytesAvailable >= 8)
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(positions[currPositionsPointer]), 0, d.Data, currDataBlockPointer, 8);
                    currPositionsPointer++;
                    currDataBlockPointer += 8;
                    dataBytesAvailable -= 8;
                }
                
                d.DataLength = d.Data.Length;
                CfsCommon.WriteAtPosition(Filestream, allocatedPositions[i], d.ToBytes());
                LogDebug("AppendPositionToMetadata successfully wrote data block " + i);
            }

            // deallocate old blocks
            DeallocateBlocks(positionsList);

            return;
        }

        private void RemovePositionFromMetadata(long metadataPosition, long positionToRemove)
        {
            MetadataBlock curr = MetadataBlock.FromPosition(Filestream, BlockSizeBytes, metadataPosition, Logging);
            if (curr == null)
            {
                LogDebug("RemovePositionFromMetadata unable to retrieve metadata from position " + metadataPosition);
                throw new IOException("Unable to retrieve metadata from position " + metadataPosition);
            }

            // retrieve positions and remove
            long[] positions = curr.GetMetadataBlocks();
            List<long> positionsList = new List<long>();
            if (positions != null)
            {
                positionsList = new List<long>(positions);
                if (positionsList.Contains(positionToRemove)) positionsList.Remove(positionToRemove);
                positions = positionsList.ToArray();
            }

            // determine number of blocks required
            long sizeRequired = positionsList.Count * 8;
            int blocksRequired = 0;
            if (sizeRequired < (BlockSizeBytes - MetadataBlock.BytesReservedPerBlock))
            {
                LogDebug("RemovePositionFromMetadata updated positions list fits into metadata block");
            }
            else
            {
                sizeRequired = sizeRequired - BlockSizeBytes;
                blocksRequired = (int)Math.Ceiling((double)sizeRequired / (double)(BlockSizeBytes - DataBlock.BytesReservedPerBlock));
                LogDebug("RemovePositionFromMetadata updated positions list requires metadata block plus " + blocksRequired + " blocks");
            }

            // if fits into metadata block
            if (blocksRequired == 0)
            {
                // fits into metadata, just rewrite and return
                CfsCommon.WriteAtPosition(Filestream, metadataPosition, CfsCommon.InitByteArray(BlockSizeBytes - MetadataBlock.BytesReservedPerBlock, 0x00));
                curr.Data = ArrayLongToBytes(positionsList.ToArray());

                if (curr.Data != null) curr.LocalDataLength = curr.Data.Length;
                else curr.LocalDataLength = 0;

                CfsCommon.WriteAtPosition(Filestream, metadataPosition, curr.ToBytes());
                LogDebug("RemovePositionFromMetadata successfully rewrote metadata block at position " + metadataPosition + " (removed " + positionToRemove + ")");
                return;
            }

            // allocate new blocks 
            List<long> allocatedPositionsList = AllocateBlocks(blocksRequired);
            long[] allocatedPositions = allocatedPositionsList.ToArray();

            // rewrite metadata
            int metadataBytesAvailable = BlockSizeBytes - MetadataBlock.BytesReservedPerBlock;
            int currPositionsPointer = 0;
            int currMetadataBlockPointer = 0;
            curr.Data = new byte[metadataBytesAvailable];
            curr.Data = CfsCommon.InitByteArray(metadataBytesAvailable, 0x00);
            while (metadataBytesAvailable >= 8)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(positions[currPositionsPointer]), 0, curr.Data, currMetadataBlockPointer, 8);
                currPositionsPointer++;
                currMetadataBlockPointer += 8;
            }
            curr.LocalDataLength = curr.Data.Length;
            CfsCommon.WriteAtPosition(Filestream, metadataPosition, curr.ToBytes());
            LogDebug("RemovePositionFromMetadata successfully rewrote metadata block at position " + metadataPosition + ", continuing to data blocks");

            // write datablocks
            long currParent = metadataPosition;
            for (int i = 0; i < allocatedPositions.Length; i++)
            {
                int dataBytesAvailable = BlockSizeBytes - DataBlock.BytesReservedPerBlock;
                int currDataBlockPointer = 0;

                DataBlock d = new DataBlock(Filestream, BlockSizeBytes, CfsCommon.InitByteArray(dataBytesAvailable, 0x00), Logging);
                if (i < (allocatedPositions.Length - 1)) d.ChildBlock = allocatedPositions[i + 1];
                else d.ChildBlock = -1;
                d.ParentBlock = currParent;
                
                while (dataBytesAvailable >= 8)
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(positions[currPositionsPointer]), 0, d.Data, currDataBlockPointer, 8);
                    currPositionsPointer++;
                    currDataBlockPointer += 8;
                    dataBytesAvailable -= 8;
                }

                d.DataLength = d.Data.Length;
                CfsCommon.WriteAtPosition(Filestream, allocatedPositions[i], d.ToBytes());
                LogDebug("RemovePositionFromMetadata successfully wrote data block " + i);
            }

            // deallocate old blocks
            DeallocateBlocks(positionsList);

            return;
        }

        private MetadataBlock FindDirectoryMetadata(string dir, out long position)
        {
            position = -1;
            MetadataBlock ret = null;

            if (String.IsNullOrEmpty(dir) || String.Compare(dir, "/") == 0)
            {
                #region Root

                ret = ReadRootMetadata();
                position = BlockSizeBytes;  // root metadata
                LogDebug("FindDirectoryMetadata returning root dir md");
                return ret;

                #endregion
            }
            else
            {
                #region Nested

                if (dir.StartsWith("/")) dir = dir.Substring(1);
                string[] path = ParsePath(dir);
                LogDebug("FindDirectoryMetadata processing nested directory");

                // curr will be overwritten while iterating through the path
                position = BlockSizeBytes;  // root metadata
                MetadataBlock curr = ReadRootMetadata();
                LogDebug("FindDirectoryMetadata processing directory: " + curr.ToString());

                for (int i = 0; i < path.Length; i++)
                {
                    // check to see if subdirectory exists
                    LogDebug("FindDirectoryMetadata read md for " + curr.Name + " from " + dir);
                    long[] blocks = curr.GetMetadataBlocks();
                    if (blocks == null || blocks.Length < 1)
                    {
                        LogDebug("FindDirectoryMetadata no metadata blocks exist in metadata");
                        return null;
                    }
                    else
                    {
                        LogDebug("FindDirectoryMetadata retrieved " + blocks.Length + " metadata blocks in metadata");
                    }

                    // iterate child nodes
                    bool childFound = false;
                    for (int j = 0; j < blocks.Length; j++)
                    {
                        LogDebug("FindDirectoryMetadata reading metadata block " + j + "/" + blocks.Length + " for " + curr.Name + " in " + dir);

                        position = blocks[j];
                        MetadataBlock tempMd = MetadataBlock.FromPosition(Filestream, BlockSizeBytes, blocks[j], Logging);
                        if (tempMd == null)
                        {
                            // unable to read block
                            LogDebug("FindDirectoryMetadata unable to read metadata block at position " + position);
                            return null;
                        }
                        if (String.Compare(path[i], tempMd.Name) == 0)
                        {
                            LogDebug("FindDirectoryMetadata found matching child metadata: " + tempMd.Name);
                            curr = tempMd;
                            childFound = true;
                            break;
                        }
                    }

                    if (childFound)
                    {
                        LogDebug("FindDirectoryMetadata child found, continuing");
                        continue;
                    }

                    // unable to find child
                    LogDebug("FindDirectoryMetadata unable to find " + curr.Name + " from " + dir);
                    return null;
                }

                // code will only reach here if each child is found
                // and curr will be set accordingly
                LogDebug("FindDirectoryMetadata returning dir md for " + dir + " (position " + position + ")");
                return curr;

                #endregion
            }
        }
        
        private MetadataBlock ReadRootMetadata()
        {
            LogDebug("ReadRootMetadata reading root metadata");
            return MetadataBlock.FromPosition(Filestream, BlockSizeBytes, BlockSizeBytes, Logging);
        }
        
        private string[] ParsePath(string path)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            string[] temp = path.Split('/');
            List<string> ret = new List<string>();
            foreach (string curr in temp)
            {
                if (String.IsNullOrEmpty(curr)) continue;
                if (String.Compare(curr, ".") == 0) continue;
                ret.Add(curr);
            }
            return ret.ToArray();
        }

        private bool EnumerateDirectory(
            string path, 
            out List<Tuple<string, long>> files, 
            out List<string> directories,
            out long currPosition)
        {
            currPosition = 0;
            files = new List<Tuple<string, long>>();
            directories = new List<string>();
            
            if (String.IsNullOrEmpty(path)) path = "/";
            LogDebug("EnumerateDirectory enumerating " + path);
            MetadataBlock metadata = FindDirectoryMetadata(path, out currPosition);
            if (metadata == null)
            {
                LogDebug("EnumerateDirectory unable to find " + path);
                throw new DirectoryNotFoundException(path);
            }
            else
            {
                LogDebug("EnumerateDirectory found parent directory at position " + currPosition);
            }

            if (metadata == null)
            {
                LogDebug("EnumerateDirectory " + path + " not found");
                return false;     
            }

            if (metadata.LocalDataLength <= 0 && metadata.ChildDataBlock < 0)
            {
                LogDebug("EnumerateDirectory " + path + " is empty");
                return true;   
            }

            byte[] data = new byte[metadata.LocalDataLength];
            Buffer.BlockCopy(metadata.Data, 0, data, 0, metadata.LocalDataLength);
            
            // append from any child metadata blocks
            while (true)
            {
                if (metadata.ChildDataBlock <= 0) break;

                metadata = MetadataBlock.FromPosition(Filestream, BlockSizeBytes, metadata.ChildDataBlock, Logging);
                if (metadata == null)
                {
                    LogDebug("EnumerateDirectory child md not found at " + metadata.ChildDataBlock);
                    return false; // directory not found
                }

                if (metadata.Data == null || metadata.Data.Length < 1 || metadata.LocalDataLength < 1)
                {
                    LogDebug("EnumerateDirectory no data found in md at " + metadata.ChildDataBlock);
                    break;
                }

                byte[] oldData = new byte[data.Length];
                Buffer.BlockCopy(data, 0, oldData, 0, data.Length);

                data = new byte[oldData.Length + metadata.LocalDataLength];
                Buffer.BlockCopy(oldData, 0, data, 0, oldData.Length);
                Buffer.BlockCopy(metadata.Data, 0, data, oldData.Length, metadata.LocalDataLength);
            }

            // convert byte array to list long
            long[] addresses = BytesToArrayLong(data);
            if (addresses == null || addresses.Length < 1)
            {
                LogDebug("EnumerateDirectory no data or invalid data found in node and children");
                return false;    
            }

            for (int i = 0; i < addresses.Length; i++)
            {
                // each address is a metadata node for a child in this directory
                MetadataBlock currMetadata = MetadataBlock.FromPosition(Filestream, BlockSizeBytes, addresses[i], Logging);
                if (currMetadata == null)
                {
                    LogDebug("EnumerateDirectory unable to read md linked in metdata at position " + addresses[i]);
                    return false;  
                }

                if (CfsCommon.IsTrue(currMetadata.IsDirectory)) directories.Add(currMetadata.Name);
                if (CfsCommon.IsTrue(currMetadata.IsFile)) files.Add(new Tuple<string, long>(currMetadata.Name, currMetadata.FullDataLength));
            }

            LogDebug("EnumerateDirectory returning md for " + path + " (" + files.Count + " file, " + directories.Count + " directory)");
            return true;
        }
        
        private List<long> GetFileDataBlockPositions(MetadataBlock md)
        {
            List<long> ret = new List<long>();
            if (md == null) throw new ArgumentNullException(nameof(md));
            if (md.ChildDataBlock <= 0)
            {
                LogDebug("GetFileDataBlockPositions no associated data blocks");
                return null;
            }

            ret.Add(md.ChildDataBlock);
            long currPosition = md.ChildDataBlock;
            while (true)
            {
                DataBlock curr = DataBlock.FromPosition(Filestream, BlockSizeBytes, currPosition, Logging);
                if (curr == null)
                {
                    LogDebug("GetFileDataBlockPositions unable to retrieve child data block at " + currPosition);
                    throw new IOException("Unable to retrieve child data block");
                }

                if (curr.ChildBlock <= 0)
                {
                    LogDebug("GetFileDatBlockPositions end of data block chain reached");
                    break;
                }

                currPosition = curr.ChildBlock;
            }

            return ret;
        }

        private byte[] ArrayLongToBytes(long[] data)
        {
            if (data == null || data.Length < 1) return null;
            byte[] ret = new byte[data.Length * 8];
            for (int i = 0; i < data.Length; i++)
            {
                byte[] curr = BitConverter.GetBytes(data[i]);
                Buffer.BlockCopy(curr, 0, ret, (i * 8), 8);
            }
            return ret;
        }

        private long[] BytesToArrayLong(byte[] data)
        {
            if (data == null || data.Length < 1) return null;
            if (data.Length % 8 != 0) return null;                  // malformed data

            List<long> retList = new List<long>();
            for (int i = 0; i < data.Length; i += 8)
            {
                byte[] tempData = new byte[8];
                Buffer.BlockCopy(data, i, tempData, 0, 8);
                long curr = BitConverter.ToInt64(tempData, 0);
                retList.Add(curr);
            }

            return retList.ToArray();
        }

        private byte[] ToBytes()
        {
            byte[] ret = CfsCommon.InitByteArray(BlockSizeBytes, 0x00);
            Buffer.BlockCopy(Container.SignatureBytes, 0, ret, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(Version), 0, ret, 8, 4);

            if (Name.Length > 256) Name = Name.Substring(0, 256);
            byte[] nameByteArray = Encoding.UTF8.GetBytes(Name);
            byte[] nameBytesFixed = new byte[256];
            Buffer.BlockCopy(nameByteArray, 0, nameBytesFixed, 0, nameByteArray.Length);
            Buffer.BlockCopy(nameBytesFixed, 0, ret, 16, 256);

            Buffer.BlockCopy(BitConverter.GetBytes(BlockSizeBytes), 0, ret, 288, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(BlockCount), 0, ret, 296, 4);

            byte[] tsByteArray = Encoding.UTF8.GetBytes(CreatedUtc);
            byte[] tsBytesFixed = CfsCommon.InitByteArray(32, 0x00);
            Buffer.BlockCopy(tsByteArray, 0, tsBytesFixed, 0, tsByteArray.Length);
            Buffer.BlockCopy(tsBytesFixed, 0, ret, 304, 32);

            byte[] unusedBlocks = CfsCommon.BitArrayToByteArray(UnusedBlocks);
            Buffer.BlockCopy(unusedBlocks, 0, ret, 1024, unusedBlocks.Length);
            
            return ret;
        }
        
        private void SetFileSize(long size)
        {
            Filestream.SetLength(size);
        }
        
        private void SetBlockUsed(int blockNum)
        {
            if (blockNum < 0 || blockNum > (BlockCount - 1)) throw new ArgumentOutOfRangeException(nameof(blockNum));
            UnusedBlocks[blockNum] = false;
            CfsCommon.WriteAtPosition(Filestream, 1024, CfsCommon.BitArrayToByteArray(UnusedBlocks));
        }

        private void SetBlockUnused(int blockNum)
        {
            if (blockNum < 0 || blockNum > (BlockCount - 1)) throw new ArgumentOutOfRangeException(nameof(blockNum));
            UnusedBlocks[blockNum] = true;
            CfsCommon.WriteAtPosition(Filestream, 1024, CfsCommon.BitArrayToByteArray(UnusedBlocks));
        }

        private void LoadUnusedBlocksFromDisk()
        {
            byte[] data = CfsCommon.ReadFromPosition(Filestream, 1024, (BlockCount / 8));
            UnusedBlocks = CfsCommon.ByteArrayToBitArray(data);
        }
        
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
        /// Instantiate a container by loading it from an existing file.
        /// </summary>
        /// <param name="filename">The file to load.</param>
        /// <param name="loggingEnable">Enable or disable debug logging.</param>
        /// <returns>A ready-to-use container object.</returns>
        public static Container FromFile(string filename, bool loggingEnable)
        {
            if (String.IsNullOrEmpty(filename)) throw new ArgumentNullException(nameof(filename));
            FileStream filestream = new FileStream(filename, FileMode.Open);

            // determine block size by reading BlockSizeBytes from start of file
            byte[] tempData = CfsCommon.ReadFromPosition(filestream, 0, 1024);
            if (tempData == null || tempData.Length < 1024) throw new IOException("Unable to read first 1KB from file");

            byte[] temp = new byte[4];
            Buffer.BlockCopy(tempData, 288, temp, 0, 4);
            int blockSizeBytes = BitConverter.ToInt32(temp, 0);

            // read the whole block
            byte[] data = CfsCommon.ReadFromPosition(filestream, 0, blockSizeBytes);
            if (data == null || data.Length < blockSizeBytes) throw new IOException("Unable to read first " + blockSizeBytes + " bytes from file");

            Container ret = Container.FromBytes(data);
            ret.Filestream = filestream;
            ret.TotalSizeBytes = ret.BlockCount * ret.BlockSizeBytes;

            if (loggingEnable) ret.Logging = new LoggingModule("127.0.0.1", 514, true, LoggingModule.Severity.Debug, false, true, true, true, true, true);
            else ret.Logging = null;

            return ret;
        }

        #endregion

        #region Private-Static-Methods

        private static Container FromBytes(byte[] ba)
        {
            if (ba == null || ba.Length < 1) throw new ArgumentNullException(nameof(ba));
            if (ba.Length < 310) throw new ArgumentException("Byte array has length less than 310");

            Container ret = new Container();
            byte[] temp;

            ret.Signature = new byte[4];
            Buffer.BlockCopy(ba, 0, ret.Signature, 0, 4);

            temp = new byte[4];
            Buffer.BlockCopy(ba, 8, temp, 0, 4);
            ret.Version = BitConverter.ToInt32(temp, 0);

            temp = new byte[256];
            Buffer.BlockCopy(ba, 16, temp, 0, 256);
            ret.Name = Encoding.UTF8.GetString(CfsCommon.TrimNullBytes(temp)).Trim();

            temp = new byte[4];
            Buffer.BlockCopy(ba, 288, temp, 0, 4);
            ret.BlockSizeBytes = BitConverter.ToInt32(temp, 0);

            temp = new byte[4];
            Buffer.BlockCopy(ba, 296, temp, 0, 4);
            ret.BlockCount = BitConverter.ToInt32(temp, 0);

            temp = new byte[32];
            Buffer.BlockCopy(ba, 304, temp, 0, 32);
            ret.CreatedUtc = Encoding.UTF8.GetString(CfsCommon.TrimNullBytes(temp)).Trim();

            int unusedBlocksSize = ret.BlockCount / 8;
            temp = new byte[unusedBlocksSize];
            Buffer.BlockCopy(ba, 1024, temp, 0, unusedBlocksSize);
            ret.UnusedBlocks = CfsCommon.ByteArrayToBitArray(temp);

            return ret;
        }

        #endregion
    }
}
