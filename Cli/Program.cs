using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ContainerFS;

namespace CLINetCore
{
    class Program
    {
        static string Command;
        static string ContainerFile;
        static string Filename;
        static string Directory;
        static string CreateParams;
        static int BlockSize;
        static int BlockCount;
        static bool Debug = false;
        static Container CurrContainer;
        static string ParsedFilename;
        static string ParsedDirectory;
        static List<Tuple<string, long>> FilesInDirectory;
        static List<string> Subdirectories;
        static long Position;

        static byte[] RequestData = null;
        static byte[] ResponseData = null;

        static void Main(string[] args)
        {
            try
            {
                #region Parse-Arguments

                if (args == null || args.Length < 2)
                {
                    Usage("No arguments specified");
                    return;
                }

                ContainerFile = args[0];
                Command = args[1];

                for (int i = 2; i < args.Length; i++)
                {
                    if (String.IsNullOrEmpty(args[i])) continue;
                    if (args[i].StartsWith("--file=") && args[i].Length >= 7)
                    {
                        Filename = args[i].Substring(7);
                    }
                    else if (args[i].StartsWith("--path=") && args[i].Length >= 7)
                    {
                        Directory = args[i].Substring(7);
                    }
                    else if (String.Compare(args[i], "--debug") == 0)
                    {
                        Debug = true;
                    }
                    else if (args[i].StartsWith("--params=") && args[i].Length >= 9)
                    {
                        CreateParams = args[i].Substring(9);
                        if (new Regex(@"^\d+,\d+$").IsMatch(CreateParams))
                        {
                            string[] currParams = CreateParams.Split(',');
                            if (currParams.Length != 2)
                            {
                                Usage("Value for 'params' is invalid");
                                return;
                            }

                            if (!Int32.TryParse(currParams[0], out BlockSize)
                                || !Int32.TryParse(currParams[1], out BlockCount))
                            {
                                Usage("Cannot convert values from 'params' to two integers");
                                return;
                            }
                        }
                        else
                        {
                            Usage("Value for 'params' is not of the form integer,integer");
                            return;
                        }
                    }
                    else
                    {
                        Usage("Unknown argument: " + args[i]);
                        return;
                    }
                }

                #endregion

                #region Verify-Command

                List<string> validCommands = new List<string>() { "read", "write", "delete", "dir", "mkdir", "rmdir", "create", "stats" };
                if (!validCommands.Contains(Command))
                {
                    Usage("Invalid command: " + Command);
                    return;
                }

                #endregion

                #region Enumerate

                if (Debug)
                {
                    Console.WriteLine("Command        : " + Command);
                    Console.WriteLine("Container File : " + ContainerFile);
                    Console.WriteLine("Filename       : " + Filename);
                    Console.WriteLine("Directory      : " + Directory);
                    Console.WriteLine("Block Size     : " + BlockSize);
                    Console.WriteLine("Block Count    : " + BlockCount);
                }

                #endregion

                #region Create

                if (String.Compare(Command, "create") == 0)
                {
                    if ((BlockSize < 4096) || (BlockSize % 4096 != 0) || (BlockCount < 4096) || (BlockCount % 4096 != 0))
                    {
                        Usage("Block size and block count must be greater than and equally divisible by 4096");
                        return;
                    }

                    CurrContainer = new Container(ContainerFile, ContainerFile, BlockSize, BlockCount, Debug);
                    if (Debug) Console.WriteLine("Successfully wrote new container " + Filename);
                    return;
                }

                #endregion

                #region Initialize-Container

                if (!File.Exists(ContainerFile))
                {
                    Console.WriteLine("*** Container file " + ContainerFile + " not found");
                }

                CurrContainer = Container.FromFile(ContainerFile, Debug);

                #endregion

                #region Process-by-Command

                switch (Command)
                {
                    case "stats":
                        Console.WriteLine(CurrContainer.ToString());
                        return;

                    case "read":
                        if (String.IsNullOrEmpty(Filename))
                        {
                            Usage("Filename must be supplied");
                            return;
                        }
                        else
                        {
                            if (!ParseFilename(Filename, out ParsedDirectory, out ParsedFilename))
                            {
                                Usage("Unable to parse filename");
                                return;
                            }
                        }

                        ResponseData = CurrContainer.ReadFile(ParsedDirectory, ParsedFilename);
                        if (Debug)
                        {
                            Console.WriteLine("Read " + ParsedFilename + " from " + ParsedDirectory + " (" + ResponseData.Length + " bytes)");
                        }

                        WriteConsoleData(ResponseData);
                        return;

                    case "write":
                        if (String.IsNullOrEmpty(Filename))
                        {
                            Usage("Filename must be supplied");
                            return;
                        }
                        else
                        {
                            if (!ParseFilename(Filename, out ParsedDirectory, out ParsedFilename))
                            {
                                Usage("Unable to parse filename");
                                return;
                            }
                        }

                        if (Debug)
                        {
                            Console.WriteLine("Writing " + ParsedFilename + " to " + ParsedDirectory);
                        }

                        ReadConsoleData();
                        CurrContainer.WriteFile(ParsedDirectory, ParsedFilename, RequestData);

                        if (Debug)
                        {
                            Console.WriteLine("Wrote " + ParsedFilename + " from " + ParsedDirectory + " (" + RequestData.Length + " bytes)");
                        }

                        return;

                    case "delete":
                        if (String.IsNullOrEmpty(Filename))
                        {
                            Usage("Filename is empty");
                            return;
                        }
                        else
                        {
                            if (!ParseFilename(Filename, out ParsedDirectory, out ParsedFilename))
                            {
                                Usage("Unable to parse filename");
                                return;
                            }
                        }

                        CurrContainer.DeleteFile(ParsedDirectory, ParsedFilename);

                        if (Debug)
                        {
                            Console.WriteLine("Deleted " + ParsedFilename + " from " + ParsedDirectory);
                        }

                        return;

                    case "dir":
                        if (String.IsNullOrEmpty(Directory))
                        {
                            Usage("Directory is empty");
                            return;
                        }
                        else
                        {
                            CurrContainer.ReadDirectory(Directory, out FilesInDirectory, out Subdirectories, out Position);
                            Console.WriteLine("Directory : " + Directory);
                            Console.WriteLine("Position  : " + Position);
                            if (Subdirectories != null)
                            {
                                Console.WriteLine("  Child Directories  : " + Subdirectories.Count);
                                if (Subdirectories.Count > 0)
                                {
                                    foreach (string curr in Subdirectories)
                                    {
                                        Console.WriteLine("    <dir>  " + curr);
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine("  Child Directories  : (null)");
                            }
                            if (FilesInDirectory != null)
                            {
                                Console.WriteLine("  Files in Directory : " + FilesInDirectory.Count);
                                if (FilesInDirectory.Count > 0)
                                {
                                    foreach (Tuple<string, long> curr in FilesInDirectory)
                                    {
                                        string line = "    " + String.Format("{0,-11}", curr.Item2.ToString()) + " " + curr.Item1;
                                        Console.WriteLine(line);
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine("  Files in Directory : (null)");
                            }
                        }

                        return;

                    case "mkdir":
                        if (String.IsNullOrEmpty(Directory))
                        {
                            Usage("Directory is empty");
                            return;
                        }
                        else
                        {
                            CurrContainer.WriteDirectory(Directory);
                            if (Debug)
                            {
                                Console.WriteLine("Successfully created directory " + Directory);
                            }
                        }

                        return;

                    case "rmdir":
                        if (String.IsNullOrEmpty(Directory))
                        {
                            Usage("Directory is empty");
                            return;
                        }
                        else
                        {
                            CurrContainer.DeleteDirectory(Directory);
                            if (Debug)
                            {
                                Console.WriteLine("Successfully deleted directory " + Directory);
                            }
                        }

                        return;

                    default:
                        Usage("Unknown command: " + Command);
                        return;
                }

                #endregion
            }
            catch (Exception e)
            {
                ExceptionConsole("CFS", "Outer exception", e);
            }
        }

        static bool ParseFilename(string filename, out string path, out string file)
        {
            path = "";
            file = "";

            if (String.IsNullOrEmpty(filename)) return false;
            while (filename.StartsWith("/"))
            {
                if (filename.Length <= 1) return false;
                filename = filename.Substring(1);
            }

            string[] parts = filename.Split('/');

            if (parts.Length == 1)
            {
                path = "/";
                file = filename;
                return true;
            }
            else
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    //  0   1   2
                    // /foo/bar/baz.txt
                    // length = 3

                    if (Debug) Console.WriteLine("ParseFilename part " + i + ": " + parts[i]);

                    if (i < (parts.Length - 1))
                    {
                        if (String.IsNullOrEmpty(parts[i])) continue;
                        path += "/" + parts[i];
                        if (Debug) Console.WriteLine("ParseFilename path is now: " + path);
                    }
                    else
                    {
                        if (String.IsNullOrEmpty(parts[i])) continue;
                        file = parts[i];
                        if (Debug) Console.WriteLine("ParseFilename file is now: " + file);
                    }
                }

                return true;
            }
        }

        static void ExceptionConsole(string method, string text, Exception e)
        {
            var st = new StackTrace(e, true);
            var frame = st.GetFrame(0);
            int line = frame.GetFileLineNumber();
            string filename = frame.GetFileName();

            Console.WriteLine("---");
            Console.WriteLine("An exception was encountered which triggered this message.");
            Console.WriteLine("  Method: " + method);
            Console.WriteLine("  Text: " + text);
            Console.WriteLine("  Type: " + e.GetType().ToString());
            Console.WriteLine("  Data: " + e.Data);
            Console.WriteLine("  Inner: " + e.InnerException);
            Console.WriteLine("  Message: " + e.Message);
            Console.WriteLine("  Source: " + e.Source);
            Console.WriteLine("  StackTrace: " + e.StackTrace);
            Console.WriteLine("  Stack: " + StackToString());
            Console.WriteLine("  Line: " + line);
            Console.WriteLine("  File: " + filename);
            Console.WriteLine("  ToString: " + e.ToString());
            Console.WriteLine("---");

            return;
        }

        static string StackToString()
        {
            string ret = "";

            StackTrace t = new StackTrace();
            for (int i = 0; i < t.FrameCount; i++)
            {
                if (i == 0)
                {
                    ret += t.GetFrame(i).GetMethod().Name;
                }
                else
                {
                    ret += " <= " + t.GetFrame(i).GetMethod().Name;
                }
            }

            return ret;
        }

        static void WriteConsoleData(byte[] data)
        {
            if (data == null)
            {
                data = new byte[1];
                data[0] = 0x00;
            }

            using (Stream stdout = Console.OpenStandardOutput())
            {
                stdout.Write(data, 0, data.Length);
            }
        }

        static void ReadConsoleData()
        {
            using (Stream stdin = Console.OpenStandardInput())
            {
                byte[] buffer = new byte[2048];
                int bytes;
                while ((bytes = stdin.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (RequestData == null)
                    {
                        RequestData = new byte[bytes];
                        Buffer.BlockCopy(buffer, 0, RequestData, 0, bytes);
                    }
                    else
                    {
                        byte[] tempData = new byte[RequestData.Length + bytes];
                        Buffer.BlockCopy(RequestData, 0, tempData, 0, RequestData.Length);
                        Buffer.BlockCopy(buffer, 0, tempData, RequestData.Length, bytes);
                        RequestData = tempData;
                    }
                }
            }
        }

        static void Usage(string msg)
        {
            if (!String.IsNullOrEmpty(msg))
            {
                Console.WriteLine("*** " + msg);
                Console.WriteLine("");
            }

            //          1         2         3         4         5         6         7        
            // 12345678901234567890123456789012345678901234567890123456789012345678901234567890
            Console.WriteLine("ContainerFS CLI v" + Version());
            Console.WriteLine("Usage:");
            Console.WriteLine("$ cfs [container] [command] [options]");
            Console.WriteLine("");
            Console.WriteLine("Where:");
            Console.WriteLine("  container           Name of existing container or container to create");
            Console.WriteLine("");
            Console.WriteLine("Commands:");
            Console.WriteLine("  stats              View statistics of an existing container");
            Console.WriteLine("  create             Create container using params specified in --params");
            Console.WriteLine("  read               Read file specified in --file");
            Console.WriteLine("  write              Write file specified in --file");
            Console.WriteLine("  delete             Delete file specified in --file");
            Console.WriteLine("  dir                Enumerate directory specified in --path");
            Console.WriteLine("  mkdir              Create directory specified in --path");
            Console.WriteLine("  rmdir              Delete directory specified in --path, must be empty");
            Console.WriteLine("");
            Console.WriteLine("Options:");
            Console.WriteLine("  --file=[file]      The file associated with the command");
            Console.WriteLine("  --path=[path]      The path associated with the command");
            Console.WriteLine("  --params=[params]  Parameters to use in container creation");
            Console.WriteLine("  --debug            Enable verbose logging");
            Console.WriteLine("");
            Console.WriteLine("Creating a container");
            Console.WriteLine("  When creating a container, use the following value for --params:");
            Console.WriteLine("  [blocksize],[numblocks] where each is a multiple of 4096");
            Console.WriteLine("");
            Console.WriteLine("Writing data:");
            Console.WriteLine("Forward data to a file as follows:");
            Console.WriteLine("  $ cfs [container] write --file=[file] < existing_file.txt");
            Console.WriteLine("  $ echo This is some data! | cfs [container] write --file=[file]");
            Console.WriteLine("");
            Console.WriteLine("Files and paths:");
            Console.WriteLine("  Files and paths should be structured as /some/directory/file.txt");
            Console.WriteLine("  The root directory should be indicated by /");
            Console.WriteLine("");
        }

        static string Version()
        {
            Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            return fvi.FileVersion;
        }
    }
}
