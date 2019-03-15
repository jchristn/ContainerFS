using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ContainerFS;

namespace TestNetFramework
{
    class Program
    {
        public static Random random = new Random((int)DateTime.Now.Ticks);

        static void Main(string[] args)
        {
            try
            {
                Container c = null;
                bool logging = false;
                string userInput = "";
                string containerFilename = "";
                string path = "";
                string filename = "";
                string stringData = "";
                byte[] byteData;
                long startPosition;
                long byteCount;
                long position;

                List<Tuple<string, long>> files = new List<Tuple<string, long>>();
                List<string> directories = new List<string>();

                int count = 0;
                int fileSize = 0;
                List<string> testFilesList = new List<string>();
                
                #region Initialize-or-Load

                while (true)
                {
                    Console.Write("New or existing : ");
                    userInput = Console.ReadLine();
                    if (String.IsNullOrEmpty(userInput)) continue;
                    
                    switch (userInput.ToLower().Trim())
                    {
                        case "new":
                            Console.Write("           Name : ");
                            userInput = Console.ReadLine();
                            if (String.IsNullOrEmpty(userInput)) continue;
                            containerFilename = String.Copy(userInput);
                            c = new Container(containerFilename, containerFilename, 4096, 4096, logging);
                            break;

                        case "existing":
                            Console.Write("           Name : ");
                            userInput = Console.ReadLine();
                            if (String.IsNullOrEmpty(userInput)) continue;
                            containerFilename = String.Copy(userInput);
                            c = Container.FromFile(containerFilename, logging);
                            break;

                        default:
                            continue;
                    }

                    break;
                }

                Console.WriteLine(c.ToString());

                #endregion

                #region Console

                bool runForever = true;
                while (runForever)
                {
                    Console.Write("[" + containerFilename + "]# ");
                    userInput = Console.ReadLine();
                    if (String.IsNullOrEmpty(userInput)) continue;

                    switch (userInput)
                    {
                        case "?":
                            Console.WriteLine("---");
                            Console.WriteLine("Available commands:");
                            Console.WriteLine(" q               Quit");
                            Console.WriteLine(" cls             Clear the screen");
                            Console.WriteLine(" stats           Show container stats");
                            Console.WriteLine(" dir             Enumerate a directory");
                            Console.WriteLine(" read            Read a file");
                            Console.WriteLine(" read_range      Read a range of bytes from a file");
                            Console.WriteLine(" write           Write a file");
                            Console.WriteLine(" delete          Delete a file");
                            Console.WriteLine(" enum_block      Enumerate block contents");
                            Console.WriteLine(" read_raw_block  Read and enumerate raw contents of a block");
                            Console.WriteLine(" mkdir           Make a directory");
                            Console.WriteLine(" rmdir           Remove a directory (must be empty)");
                            Console.WriteLine("");
                            Console.WriteLine("Test commands:");
                            Console.WriteLine(" write_n         Write n files of fixed size");
                            Console.WriteLine(" read_n          Read those n files");
                            Console.WriteLine(" delete_n        Delete those n files");
                            Console.WriteLine("");
                            break;

                        case "q":
                            runForever = false;
                            break;

                        case "cls":
                            Console.Clear();
                            break;
                            
                        case "stats":
                            Console.WriteLine(c.ToString());
                            break;

                        case "dir":
                            Console.Write("Path: ");
                            path = Console.ReadLine();
                            try
                            {
                                c.ReadDirectory(path, out files, out directories, out position);
                                Console.WriteLine("Position    : " + position);
                                Console.WriteLine("Directories : " + directories.Count);
                                if (directories != null && directories.Count > 0)
                                {
                                    foreach (string curr in directories)
                                    {
                                        Console.WriteLine("    " + curr);
                                    }
                                }
                                else
                                {
                                    Console.WriteLine("    (none)");
                                }
                                Console.WriteLine("Files       : " + files.Count);
                                if (files != null && files.Count > 0)
                                {
                                    foreach (Tuple<string, long> curr in files)
                                    {
                                        string line = "    " + String.Format("{0,-11}", curr.Item2.ToString()) + " " + curr.Item1;
                                        Console.WriteLine(line);
                                    }
                                }
                                else
                                {
                                    Console.WriteLine("    (none)");
                                }
                            }
                            catch (DirectoryNotFoundException) { Console.WriteLine("Not found"); }
                            catch (IOException) { Console.WriteLine("Failed to iterate"); }
                            break;

                        case "read":
                            Console.Write("Path: ");
                            path = Console.ReadLine();
                            Console.Write("File: ");
                            filename = Console.ReadLine();
                            try
                            {
                                byteData = c.ReadFile(path, filename);
                                if (byteData == null || byteData.Length < 1) Console.WriteLine("(null)");
                                byte[] displayData = null;
                                if (byteData.Length > 256)
                                {
                                    displayData = new byte[256];
                                    Buffer.BlockCopy(byteData, 0, displayData, 0, 256);
                                    Console.WriteLine(Encoding.UTF8.GetString(displayData) + " ... truncated");
                                }
                                else
                                {
                                    Console.WriteLine(Encoding.UTF8.GetString(byteData));
                                }
                            }
                            catch (DirectoryNotFoundException) { Console.WriteLine("Directory not found"); }
                            catch (FileNotFoundException) { Console.WriteLine("File not found"); }
                            catch (IOException e) { Console.WriteLine("Exception: " + e.Message); }
                            break;

                        case "read_range":
                            Console.Write("Path: ");
                            path = Console.ReadLine();
                            Console.Write("File: ");
                            filename = Console.ReadLine();
                            Console.Write("Start: ");
                            startPosition = Convert.ToInt64(Console.ReadLine());
                            Console.Write("Count: ");
                            byteCount = Convert.ToInt64(Console.ReadLine());

                            try
                            {
                                byteData = c.ReadFile(path, filename, startPosition, byteCount);
                                if (byteData == null || byteData.Length < 1) Console.WriteLine("(null)");
                                byte[] displayData = null;
                                if (byteData.Length > 256)
                                {
                                    displayData = new byte[256];
                                    Buffer.BlockCopy(byteData, 0, displayData, 0, 256);
                                    Console.WriteLine(Encoding.UTF8.GetString(displayData) + " ... truncated");
                                }
                                else
                                {
                                    Console.WriteLine(Encoding.UTF8.GetString(byteData));
                                }
                            }
                            catch (DirectoryNotFoundException) { Console.WriteLine("Directory not found"); }
                            catch (FileNotFoundException) { Console.WriteLine("File not found"); }
                            catch (IOException e) { Console.WriteLine("Exception: " + e.Message); }
                            break;

                        case "write":
                            Console.Write("Path: ");
                            path = Console.ReadLine();
                            Console.Write("File: ");
                            filename = Console.ReadLine();
                            Console.Write("Data: ");
                            stringData = Console.ReadLine();
                            try
                            {
                                c.WriteFile(path, filename, Encoding.UTF8.GetBytes(stringData));
                            }
                            catch (DirectoryNotFoundException) { Console.WriteLine("Directory not found"); }
                            catch (IOException e) { Console.WriteLine("Exception: " + e.Message); }
                            break;

                        case "delete":
                            Console.Write("Path: ");
                            path = Console.ReadLine();
                            Console.Write("File: ");
                            filename = Console.ReadLine();
                            try
                            {
                                c.DeleteFile(path, filename);
                                Console.WriteLine("Success");
                            }
                            catch (DirectoryNotFoundException) { Console.WriteLine("Directory not found"); }
                            catch (FileNotFoundException) { Console.WriteLine("File not found"); }
                            catch (IOException e) { Console.WriteLine("Exception: " + e.Message); }
                            break;

                        case "enum_block":
                            Console.Write("Byte Position: ");
                            userInput = Console.ReadLine();
                            if (!Int64.TryParse(userInput, out position)) continue;
                            try
                            {
                                Console.WriteLine(c.EnumerateBlock(position));
                            }
                            catch (IOException e) { Console.WriteLine("Exception: " + e.Message); }
                            break;

                        case "read_raw_block":
                            Console.Write("Byte Position: ");
                            userInput = Console.ReadLine();
                            if (!Int64.TryParse(userInput, out position)) continue;
                            try
                            {
                                Console.WriteLine(
                                    "Bytes (hex):" + Environment.NewLine +
                                    CfsCommon.BytesToHexString(c.ReadRawBlock(position)));
                            }
                            catch (IOException e) { Console.WriteLine("Exception: " + e.Message); }
                            break;

                        case "mkdir":
                            Console.Write("Full Path: ");
                            path = Console.ReadLine();
                            try
                            {
                                c.WriteDirectory(path);
                            }
                            catch (DirectoryNotFoundException) { Console.WriteLine("Directory not found"); }
                            catch (IOException e) { Console.WriteLine("Exception: " + e.Message); }
                            break;

                        case "rmdir":
                            Console.Write("Full Path: ");
                            path = Console.ReadLine();
                            try
                            {
                                c.DeleteDirectory(path);
                            }
                            catch (DirectoryNotFoundException) { Console.WriteLine("Directory not found"); }
                            catch (IOException e) { Console.WriteLine("Exception: " + e.Message); }
                            break;
                            
                        case "write_n":
                            testFilesList = new List<string>();
                            count = 0;
                            fileSize = 0;
                            while (count <= 0)
                            {
                                Console.Write("Count: ");
                                userInput = Console.ReadLine();
                                if (!Int32.TryParse(userInput, out count)) continue;
                            }
                            while (fileSize <= 0)
                            {
                                Console.Write("Size: ");
                                userInput = Console.ReadLine();
                                if (!Int32.TryParse(userInput, out fileSize)) continue;
                            }

                            Console.WriteLine("Generating " + fileSize + " bytes of random data (slow for larger sizes, will be used across all files)");
                            byte[] randomBytes = RandomBytes(fileSize);

                            for (int i = 0; i < count; i++)
                            {
                                string currFilename = RandomString(8);
                                testFilesList.Add(currFilename);

                                try
                                {
                                    c.WriteFile("/", currFilename, randomBytes);
                                }
                                catch (DirectoryNotFoundException) { Console.WriteLine("Directory not found"); }
                                catch (IOException e) { Console.WriteLine("Exception: " + e.Message); }
                            }
                            break;

                        case "read_n":
                            if (testFilesList == null || testFilesList.Count < 1)
                            {
                                Console.WriteLine("Use write_n first");
                                break;
                            }
                            try
                            {
                                for (int i = 0; i < count; i++)
                                {
                                    try
                                    { 
                                        byteData = c.ReadFile(path, testFilesList[i]);
                                        if (byteData == null || byteData.Length < 1) Console.WriteLine("(null)");
                                        byte[] displayData = null;
                                        if (byteData.Length > 256)
                                        {
                                            displayData = new byte[256];
                                            Buffer.BlockCopy(byteData, 0, displayData, 0, 256);
                                            Console.WriteLine(Encoding.UTF8.GetString(displayData) + " ... truncated");
                                        }
                                        else
                                        {
                                            Console.WriteLine(Encoding.UTF8.GetString(byteData));
                                        }
                                    }
                                    catch (DirectoryNotFoundException) { Console.WriteLine("Directory not found"); }
                                    catch (FileNotFoundException) { Console.WriteLine("File not found"); }
                                    catch (IOException e) { Console.WriteLine("Exception: " + e.Message); }
                                }
                            }
                            catch (DirectoryNotFoundException) { Console.WriteLine("Directory not found"); }
                            catch (FileNotFoundException) { Console.WriteLine("File not found"); }
                            catch (IOException e) { Console.WriteLine("Exception: " + e.Message); }
                            break;

                        case "delete_n":
                            if (testFilesList == null || testFilesList.Count < 1)
                            {
                                Console.WriteLine("Use write_n first");
                                break;
                            }
                            try
                            {
                                for (int i = 0; i < count; i++)
                                {
                                    c.DeleteFile(path, testFilesList[i]);
                                    Console.WriteLine("Success");
                                }
                            }
                            catch (DirectoryNotFoundException) { Console.WriteLine("Directory not found"); }
                            catch (FileNotFoundException) { Console.WriteLine("File not found"); }
                            catch (IOException e) { Console.WriteLine("Exception: " + e.Message); }
                            break;

                        default:
                            break;
                    }
                }

                #endregion
            }
            catch (Exception e)
            {
                ExceptionConsole("Main", "Outer exception", e);
            }
            finally
            {
                Console.WriteLine("Press ENTER to exit");
                Console.ReadLine();
            }
        }

        private static void ExceptionConsole(string method, string text, Exception e)
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

        private static string StackToString()
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

        private static byte[] RandomBytes(int count)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            byte[] ret = new byte[count];

            int valid = 0;
            int num = 0;

            for (int i = 0; i < count; i++)
            {
                num = 0;
                valid = 0;
                while (valid == 0)
                {
                    num = random.Next(126);
                    if (((num > 47) && (num < 58)) ||
                        ((num > 64) && (num < 91)) ||
                        ((num > 96) && (num < 123)))
                    {
                        valid = 1;
                    }
                }
                ret[i] = (byte)num; 
            }

            return ret;
        }

        private static string RandomString(int count)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));

            string ret = "";
            int valid = 0;
            int num = 0;

            for (int i = 0; i < count; i++)
            {
                num = 0;
                valid = 0;
                while (valid == 0)
                {
                    num = random.Next(126);
                    if (((num > 47) && (num < 58)) ||
                        ((num > 64) && (num < 91)) ||
                        ((num > 96) && (num < 123)))
                    {
                        valid = 1;
                    }
                }
                ret += (char)num;
            }

            return ret;
        }
    }
}
