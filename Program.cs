#define WINDOWS

using System.Collections.Generic;
using System.Linq;
using System.Text;

using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using System.IO;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace CmdGCode
{
    /*class Relais
    {
        SerialPort _port;

        public Relais()
        {
            _port = new SerialPort("COM6", 115200);
            _port.Open();
            _port.Write("0");
        }

        public void toggle(bool turnOn)
        {
            Console.WriteLine("Turning laser " +  ( turnOn ? "on" : "off") ); 
            if (turnOn)
                _port.Write("1");
            else
                _port.Write("0");
        }
    }*/

    class Ultimaker
    {
        SerialPort _port;
        public Ultimaker(bool initializePosition = false)
        {
            _port = new SerialPort("COM3", 250000);
            _port.Open();

            _port.Write("\n");

            Console.WriteLine(_port.ReadLine());

            displayOnHUD("MAD-PCB Lazer v0.1");

            if (initializePosition)
            {
                home(true, true);
                waitForBufferDone();
            }

            Console.WriteLine("Junk in Buffer:" + _port.ReadExisting());
        }

        public void executeCmd(string cmd, bool verbose = false)
        {
            if(verbose)
                Console.WriteLine("Executing: " + cmd);

            _port.Write(cmd + "\n");
            //if (!cmd.StartsWith("G1"))
            {
                string ret = _port.ReadLine();
                if(verbose)
                    Console.WriteLine("Returned: " + ret);
            }
        }

        //! Returns the printhead to its home position
        public void home(bool X = false, bool Y = false, bool Z = false)
        {
            string cmd = "G28";
            if (X)
                cmd += " X0";
            if (Y)
                cmd += " Y0";
            if (Z)
                cmd += " Z0";

            if (cmd != "G28")
                executeCmd(cmd);
        }

        //! Displays a message on the Ultimakers HUD
        public void displayOnHUD(string message)
        {
            executeCmd("M117 " + message);
        }

        //! Waits for all commands to be executed
        public void waitForBufferDone()
        {
            executeCmd("M400");
        }
    }

    class Driver
    {
        //Relais _relais;
        public Ultimaker _ultimaker;

        public Driver()
        {
            //Console.WriteLine("Opening Relais Driver ...");
            //_relais = new Relais();

            Console.WriteLine("Opening Ultimaker ...");
            _ultimaker = new Ultimaker(true);
        }

        public string startCode()
        {
            string result = @"
G21
G90
M106 S0
M205 B0
G28 X0 Y0
G1 F1000.0
M117 Burning ...
";

            return result;

        }

        public void executeGCode(string code, bool verbose = false)
        {
            int numLines = code.Count(f => f == '\n');

            System.Media.SoundPlayer player = new System.Media.SoundPlayer(@"e:\done2.wav");
            player.Play();

            using (StringReader reader = new StringReader(code))
            {
                int count = 0;
                string line;

                while ((line = reader.ReadLine()) != null)
                {
                    float progress = (float)count / (float)numLines;

                    int l = Console.CursorLeft;
                    int t = Console.CursorTop;
                    Console.Write(String.Format("Progress: {0}%         ", progress*100.0f));
                    Console.CursorLeft = l;
                    Console.CursorTop = t;

                    count++;
                    //Console.WriteLine("{1}", count, line);

                    if (line.StartsWith(";"))
                        continue;
                    if (line.StartsWith("L"))
                    {
                        _ultimaker.waitForBufferDone();
                        if (line == "L0")
                            _ultimaker.executeCmd("M42 P13 S0", verbose);
                        else if (line == "L1")
                            _ultimaker.executeCmd("M42 P13 S255", verbose);

                    }
                    else if (line.StartsWith("G") || line.StartsWith("M"))
                    {
                        _ultimaker.executeCmd(line, verbose);
                    }
                }

            }

            player.Play();
        }
    }

    class ImgToGCode
    {
        Bitmap _bitmap;
        RectangleF _bounds;

        public PointF _offset;
        public float _widthInMM;
        public float _heightInMM;

        public ImgToGCode(string fileName, PointF offset)
        {
            _bitmap = new Bitmap(fileName);
            _offset = offset;

            GraphicsUnit units = GraphicsUnit.Millimeter;

            _bounds = _bitmap.GetBounds(ref units);

            float f = _bitmap.HorizontalResolution;

            _widthInMM = (_bounds.Width / _bitmap.HorizontalResolution) * 25.4f;
            _heightInMM = (_bounds.Height / _bitmap.VerticalResolution) * 25.4f;

            Console.WriteLine("Image size: " + _widthInMM.ToString() + "mm x " + _heightInMM + "mm");
        }

        public string generateGCode()
        {
            string result = "";
            bool reverse = false;
            for (int i = 0; i < _bounds.Height; i += 1)
            {
                result += String.Format("; Y: {0}\n", i);

                result += scanLine(i, reverse);
                reverse = !reverse;
            }

            return result;
        }

        public string scanLine(int line, bool reverse)
        {
            string lineCode = "";

            float yCoordInMM = (line / _bitmap.VerticalResolution) * 25.4f;

            int laserStatus = -1;
            bool didLaserTurnOn = false;

            lineCode += String.Format("G1 Y{0}\n", yCoordInMM + _offset.Y).Replace(",", ".");

            PointF currentPositionInMM = new PointF(0.0f, 0.0f);

            for (int i = (reverse ? (int)_bounds.Width-1 : 0); reverse ? i >= 0 : i < _bounds.Width; i+= reverse?-1:1 )
            {
                currentPositionInMM = new PointF((i / _bitmap.HorizontalResolution) * 25.4f + _offset.X, yCoordInMM + _offset.Y);
                Color clr = _bitmap.GetPixel(i, line);
                int newStatus = clr.R < 128 ? 0 : 1;

                if (laserStatus != newStatus)
                {
                    if (laserStatus == 1)
                        didLaserTurnOn = true;

                    laserStatus = newStatus;
                    lineCode += String.Format("G1 X{0}\n", currentPositionInMM.X).Replace(",", ".");
                    lineCode += String.Format("L{0}\n", laserStatus);
                }
            }

            if (laserStatus == 1)
                didLaserTurnOn = true;

            lineCode += String.Format("G1 X{0}\n", currentPositionInMM.X).Replace(",", ".");
            lineCode += String.Format("L{0}\n", laserStatus);

            lineCode += "L0\n"; // Disable the laser after the current Line !

            if (didLaserTurnOn)
                return lineCode;
            else
                return "";
        }

    }

    class Program
    {

        static ImgToGCode imgToGCode;
        static Driver laserDriver;

        static void Main(string[] args)
        {
            Console.WriteLine("MAD-Laser-PCB v.2");

            laserDriver = new Driver();
            imgToGCode = new ImgToGCode("e:\\insmd.tiff", new PointF(30.0f, 5.0f));

            while (true)
            {
                Console.WriteLine("1: Trace outline");
                Console.WriteLine("2: Move head");
                Console.WriteLine("4: Burn image");

                string input = Console.ReadLine();

                if (input == "1")
                {
                    laserDriver.executeGCode("L1");
                    laserDriver.executeGCode(String.Format("G1 X{0} Y{1}", imgToGCode._offset.X, imgToGCode._offset.Y));
                    Console.WriteLine("Press enter to move to next point");
                    Console.ReadLine();
                    laserDriver.executeGCode(String.Format("G1 X{0} Y{1}", imgToGCode._offset.X+imgToGCode._widthInMM, imgToGCode._offset.Y));
                    Console.WriteLine("Press enter to move to next point");
                    Console.ReadLine();
                    laserDriver.executeGCode(String.Format("G1 X{0} Y{1}", imgToGCode._offset.X + imgToGCode._widthInMM, imgToGCode._offset.Y + imgToGCode._heightInMM));
                    Console.WriteLine("Press enter to move to next point");
                    Console.ReadLine();
                    laserDriver.executeGCode(String.Format("G1 X{0} Y{1}", imgToGCode._offset.X, imgToGCode._offset.Y + imgToGCode._heightInMM));
                    Console.WriteLine("Press enter to return to home");
                    Console.ReadLine();
                    laserDriver._ultimaker.home(true, true);
                    laserDriver.executeGCode("L0");
                }
                else if (input == "2")
                {
                    Console.WriteLine("Press up-down-left-right to move the head in 1 mm increments (+Shift = 10mm)");
                    Console.WriteLine("Press 'L' to toggle the laser");
                    Console.WriteLine("Press 'O' to change offset to current location");
                    Console.WriteLine("Press ESC to return home");

                    PointF pos = new PointF(0.0f, 0.0f);
                    bool laserStatus = false;

                    while (true)
                    {
                        bool move = false;
                        ConsoleKeyInfo k = Console.ReadKey(true);
                        if (k.Key == ConsoleKey.Escape)
                        {
                            break;
                        }

                        float movDist = 1.0f;

                        if ((k.Modifiers & ConsoleModifiers.Shift) != 0)
                        {
                            movDist = 10.0f;
                        }

                        if (k.Key == ConsoleKey.LeftArrow)
                        {
                            move = true;
                            pos.X = Math.Max(0.0f, pos.X-movDist);
                        }
                        if (k.Key == ConsoleKey.RightArrow)
                        {
                            move = true;
                            pos.X = Math.Min(255.0f, pos.X + movDist);
                        }
                        if (k.Key == ConsoleKey.UpArrow)
                        {
                            move = true;
                            pos.Y = Math.Max(0.0f, pos.Y - movDist);
                        }
                        if (k.Key == ConsoleKey.DownArrow)
                        {
                            move = true;
                            pos.Y = Math.Min(255.0f, pos.Y + movDist);
                        }
                        if (k.Key == ConsoleKey.L)
                        {
                            laserStatus = !laserStatus;
                            laserDriver.executeGCode(laserStatus ? "L0" : "L1");
                        }
                        if (k.Key == ConsoleKey.O)
                        {
                            Console.WriteLine(String.Format("Offset updated to: {0}x{1}", pos.X, pos.Y));
                            imgToGCode._offset = pos;
                        }

                        if (move)
                        {
                            int l = Console.CursorLeft;
                            int t = Console.CursorTop;

                            Console.Write(String.Format("Pos: {0}x{1}           ", pos.X, pos.Y));
                            laserDriver.executeGCode(String.Format("G1 X{0} Y{1}", pos.X, pos.Y));
                            Console.CursorLeft = l;
                            Console.CursorTop = t;
                        }
                    }

                    laserDriver.executeGCode("L0");
                    laserDriver._ultimaker.home(true, true);
                }
                else if (input == "4")
                {
                    Console.WriteLine("Calculating GCode from Image ...");

                    string gCode = laserDriver.startCode() + imgToGCode.generateGCode();
                    System.IO.StreamWriter file = new System.IO.StreamWriter("e:\\test.txt");
                    file.WriteLine(gCode);
                    file.Close();

                    Console.WriteLine("Calculation done, executing code ...");
                    laserDriver.executeGCode(gCode);

                    return;
                }
            }

        }
    }

}
