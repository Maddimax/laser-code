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
            if (verbose)
                Console.WriteLine("Executing: " + cmd);

            _port.Write(cmd + "\n");
            //if (!cmd.StartsWith("G1"))
            {
                string ret = _port.ReadLine();
                if (verbose)
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
G1 F2000.0
M117 Burning ...
";

            return result;

        }

        public void executeGCode(string code, bool verbose = false, bool sound = false)
        {
            int numLines = code.Count(f => f == '\n');

            System.Media.SoundPlayer player = new System.Media.SoundPlayer(@"e:\done2.wav");
            if (sound)
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
                    Console.Write(String.Format("Progress: {0}%         ", progress * 100.0f));
                    Console.CursorLeft = l;
                    Console.CursorTop = t;

                    count++;
                    //Console.WriteLine("{1}", count, line);

                    if (line.StartsWith(";"))
                        continue;
                    if (line.StartsWith("L"))
                    {
                        float v = float.Parse(line.Substring(1));

                        _ultimaker.waitForBufferDone();

                        _ultimaker.executeCmd(String.Format("M42 P13 S{0}", (int)(v * 255.0f)), verbose);
                    }
                    else if (line.StartsWith("G") || line.StartsWith("M"))
                    {
                        _ultimaker.executeCmd(line, verbose);
                    }
                }

            }

            if (sound)
                player.Play();
        }
    }

    /*    class ImgToDistanceField
        {
            Bitmap _bitmap;
            Bitmap _output;
            RectangleF _bounds;

            public ImgToDistanceField(string fileName)
            {
                _bitmap = new Bitmap(fileName);
                _output = new Bitmap(_bitmap.Width, _bitmap.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            }
            public void generateDistanceField()
            {
                GraphicsUnit units = GraphicsUnit.Pixel;

                _bounds = _bitmap.GetBounds(ref units);

                Debug.Assert(units == GraphicsUnit.Pixel);

                for (int x = 0; x < _bounds.Width; x++)
                {
                    for (int y = 0; y < _bounds.Height; y++)
                    {
                        int d = findSmallestDistance(new Point(x, y));
                        _output.SetPixel(x, y, Color.FromArgb(d, d, d, d));
                    }
                }
            }
            public int findSmallestDistance(Point point)
            {
                if (_bitmap.GetPixel(point.X, point.Y).R == 0)
                    return 0;
                return 255;
            }
        }*/

    class ImgToGCode
    {
        Bitmap _bitmap;
        RectangleF _bounds;

        public PointF _offset;
        public float _widthInMM;
        public float _heightInMM;

        public float _beamDiameter;

        protected int _dx;
        protected int _dy;

        public ImgToGCode(string fileName, PointF offset)
        {
            _beamDiameter = 0.001f;
            _bitmap = new Bitmap(fileName);
            _offset = offset;

            GraphicsUnit units = GraphicsUnit.Millimeter;

            _bounds = _bitmap.GetBounds(ref units);

            float f = _bitmap.HorizontalResolution;

            _widthInMM = (_bounds.Width / _bitmap.HorizontalResolution) * 25.4f;
            _heightInMM = (_bounds.Height / _bitmap.VerticalResolution) * 25.4f;

            Console.WriteLine("Image size: " + _widthInMM.ToString() + "mm x " + _heightInMM + "mm");


            float beamDiameterInInch = (_beamDiameter / 25.4f);
            int bdHorz = (int)(Math.Max(1.0f, beamDiameterInInch * _bitmap.HorizontalResolution));
            int bdVert = (int)(Math.Max(1.0f, beamDiameterInInch * _bitmap.VerticalResolution));

            if (bdHorz % 2 == 0)
                bdHorz++;
            if (bdVert % 2 == 0)
                bdVert++;

            _dx = bdHorz / 2;
            _dy = bdVert / 2;
        }

        public string generateVectorGCodeFromDistImage()
        {
            return generateVectorGCodeFromDistImageAt(1);
        }

        protected HashSet<Point> _targetPixels;

        public string generateVectorGCodeFromDistImageAt(int distance)
        {
            string result = "";
            _targetPixels = new HashSet<Point>();
            for(int y=0;y<_bounds.Height;y++)
            {
                for (int x = 0; x < _bounds.Width; x++)
                {
                    Color clr = _bitmap.GetPixel(x, y);
                    if (255-clr.R == distance)
                    {
                        _targetPixels.Add(new Point(x, y));
                    }
                }
            }

            while (_targetPixels.Count > 0)
            {
                result += tracePixel(_targetPixels.ElementAt(0), 1, false);
            }

            return result;
        }

        public string tracePixel(Point pixel, float laserStrength, bool laserStatus, int entryDirection = -1)
        {
            string result = "";
            bool wasOn = laserStatus;

            // Remove myself from the list ...
            _targetPixels.Remove(pixel);

            // TODO: Check wheter to add Goto to this point ?

            // Add Goto:

            float xCoordInMM = ((float)pixel.X / _bitmap.HorizontalResolution) * 25.4f;
            float yCoordInMM = ((float)pixel.Y / _bitmap.VerticalResolution) * 25.4f;


            string myMoveCmd = String.Format("G{2} X{0} Y{1}\n", xCoordInMM+_offset.X, yCoordInMM + _offset.Y, laserStatus == true ? 1 : 0).Replace(",", ".");
            string myMove = myMoveCmd;

            // Are we coming in with a disabled laser ?
            if (!laserStatus)
            {
                // Start by turning the laser on ...
                myMove += String.Format("L{0}\n", laserStrength);
                laserStatus = true;
            }

            string[] childResults = new string[9];

            int x = 0;
            int y = 0;
            int i = 0;

            bool anyChildFound = false;

            // Check if we can go further in a straigt line ...
            if (entryDirection != -1)
            {
                i = entryDirection;
                x = (i % 3)-1;
                y = (i / 3)-1;
                Point check = new Point(x+pixel.X,y+pixel.Y);
                if (check.X >= 0 && check.X <= _bounds.Width &&
                   check.Y >= 0 && check.Y <= _bounds.Height)
                {
                    if (_targetPixels.Contains(check))
                    {
                        anyChildFound = true;
                        // The laser should aready be on ...
                        Debug.Assert(laserStatus);
                        childResults[entryDirection] = tracePixel(check, laserStrength, laserStatus, entryDirection);
                        // Did we turn the laser on ?
                        if (!wasOn && laserStatus)
                        {
                            // Then we have to disable it as well!
                            childResults[entryDirection] += "L0\n";
                            // And return to our home position ...
                            childResults[entryDirection] += myMoveCmd;
                        }

                    }
                }
            }

            // Find bordering pixels to branch off into
            i = 0; 
            for (y = -1; y < 2; y++)
            {
                for (x = -1; x < 2; x++)
                {
                    if (!(x == 0 && y == 0))
                    {
                        bool bOut;
                        Point check = new Point(x + pixel.X, y + pixel.Y);
                        if (check.X >= 0 && check.X <= _bounds.Width &&
                           check.Y >= 0 && check.Y <= _bounds.Height &&
                           i != entryDirection)
                        {
                            if (_targetPixels.Contains(check))
                            {
                                anyChildFound = true;
                                childResults[i] = tracePixel(check, laserStrength, laserStatus, i);
                                // Did we turn the laser on ? ( This should only be true if we have not found a "straigt" neighbor )
                                if (!wasOn && laserStatus)
                                {
                                    // Then we need to disable the laser ...
                                    childResults[i] += "L0\n";
                                    // And return to our home position ...
                                    childResults[i] += myMoveCmd;
                                    laserStatus = false;
                                }
                            }
                        }
                    }
                    i++;

                }
            }

            bool firstChild = true;

            // Either add our move, or the one from our straight neighbor ...
            if (entryDirection != -1)
            {
                // Was there a straight neighbor ?
                if (childResults[entryDirection] != null)
                {
                    result += childResults[entryDirection];
                    firstChild = false;
                }
                else
                {
                    result += myMove;
                }
            }
            else
            {
                result += myMove;
            }

            // Now we add the remaining neighbors ...
            for (i = 0; i < childResults.Count(); i++)
            {
                // But not the straight one!
                if (i != entryDirection && childResults[i] != null)
                {
                    if (!firstChild)
                    {
                        // Disable the laser ...
                        result += "L0\n";
                        // Move to us ...
                        result += myMoveCmd;
                        // Re-enable the laser
                        result += String.Format("L{0}\n", laserStrength); 
                    }
                    result += childResults[i];

                    firstChild = false;
                }
            }

            // If we turned the laser on, and its still on ...
            if(!wasOn && laserStatus)
            {
                // Turn it off now ...
                result += "L0\n";
            }

            return result;
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

                float progress = (float)i / _bounds.Height;
                int l = Console.CursorLeft;
                int t = Console.CursorTop;

                Console.Write(String.Format("Generating: {0}%           ", progress * 100.0f));
                Console.CursorLeft = l;
                Console.CursorTop = t;
            }

            return result;
        }

        //! Returns true if there are any black pixel withing the reach of the beam, so we don't expose stuff we don't want.
        public float blackAround(Point center)
        {
            int numBlack = 0;
            int max = 0;
            for (int x = Math.Max(0, center.X - _dx); x <= Math.Min(_bounds.Width - 1, center.X + _dx); x++)
            {
                for (int y = Math.Max(0, center.Y - _dy); y <= Math.Min(_bounds.Height - 1, center.Y + _dy); y++)
                {
                    Color clr = _bitmap.GetPixel(x, y);
                    if (clr.R < 1)
                        numBlack++;

                    max++;
                }
            }

            float amount = (float)numBlack / (float)max;

            return amount;
        }

        public string scanLine(int line, bool reverse)
        {
            string lineCode = "";

            float yCoordInMM = (line / _bitmap.VerticalResolution) * 25.4f;

            float laserStatus = -1.0f;
            bool didLaserTurnOn = false;

            lineCode += String.Format("G1 Y{0}\n", yCoordInMM + _offset.Y).Replace(",", ".");

            PointF currentPositionInMM = new PointF(0.0f, 0.0f);

            for (int i = (reverse ? (int)_bounds.Width - 1 : 0); reverse ? i >= 0 : i < _bounds.Width; i += reverse ? -1 : 1)
            {
                currentPositionInMM = new PointF((i / _bitmap.HorizontalResolution) * 25.4f + _offset.X, yCoordInMM + _offset.Y);
                /*                Color clr = _bitmap.GetPixel(i, line);
                                int newStatus = clr.R < 128 ? 0 : 1;
                                */

                float newStatus = 1.0f - blackAround(new Point(i, line));

                if (laserStatus != newStatus)
                {
                    if (laserStatus > 0.0f)
                        didLaserTurnOn = true;

                    laserStatus = newStatus;
                    lineCode += String.Format("G1 X{0}\n", currentPositionInMM.X).Replace(",", ".");
                    lineCode += String.Format("L{0}\n", laserStatus);
                }
            }

            if (laserStatus != 0.0f)
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
            imgToGCode = new ImgToGCode("e:\\smd2distorg.tif", new PointF(30.0f, 5.0f));

            while (true)
            {
                Console.WriteLine("1: Trace outline");
                Console.WriteLine("2: Move head");
                Console.WriteLine("3: Burn Laser strength test pattern");
                Console.WriteLine("4: Raster Burn image");
                Console.WriteLine("5: Vector Burn image");

                string input = Console.ReadLine();

                if (input == "1")
                {
                    laserDriver.executeGCode("L1");
                    laserDriver.executeGCode(String.Format("G1 X{0} Y{1}", imgToGCode._offset.X, imgToGCode._offset.Y));
                    Console.WriteLine("Press enter to move to next point");
                    Console.ReadLine();
                    laserDriver.executeGCode(String.Format("G1 X{0} Y{1}", imgToGCode._offset.X + imgToGCode._widthInMM, imgToGCode._offset.Y));
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
                            pos.X = Math.Max(0.0f, pos.X - movDist);
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
                            laserDriver.executeGCode(laserStatus ? "L1" : "L0");
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
                else if (input == "3")
                {
                    string code = "";

                    float x = 10.0f;
                    float y = 10.0f;


                    bool reverse = false;

                    for (int i = 50; i < 256; i+=4)
                    {
                        // Start At ...
                        code += String.Format("G1 X{0} Y{1}\n", x, reverse ? y : y + 20.0f).Replace(",", ".");
                        // Turn Laser on
                        code += String.Format("L{0}\n", (float)i / 255.0f);
                        // End At ...
                        code += String.Format("G1 X{0} Y{1}\n", x, reverse ? y + 20.0f : y).Replace(",", ".");
                        // Turn laser off ..
                        code += "L0\n";

                        x += 1.0f;

                        reverse = !reverse;
                    }

                    string gCode = laserDriver.startCode() + code;

                    Console.WriteLine("Calculation done, executing code ...");
                    laserDriver.executeGCode(gCode, false, true);
                }
                else if (input == "4")
                {
                    Console.WriteLine("Calculating GCode from Image ...");

                    string gCode = laserDriver.startCode() + imgToGCode.generateGCode();
                    System.IO.StreamWriter file = new System.IO.StreamWriter("e:\\test.txt");
                    file.WriteLine(gCode);
                    file.Close();

                    Console.WriteLine("Calculation done, executing code ...");
                    laserDriver.executeGCode(gCode, false, true);
                }
                else if (input == "5")
                {
                    Console.WriteLine("Calculating GCode from Image ...");
                    string vectorCode = imgToGCode.generateVectorGCodeFromDistImage();

                    string gCode = laserDriver.startCode() + vectorCode;

                    System.IO.StreamWriter file = new System.IO.StreamWriter("e:\\test.txt");
                    file.WriteLine(gCode);
                    file.Close();
                    Console.WriteLine("Calculation done, executing code ...");
                    laserDriver.executeGCode(gCode, false, true);
                }
            }

        }

    }

}
