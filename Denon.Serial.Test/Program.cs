using CommandLine;
using Denon.Serial.Test;
using Microsoft.Extensions.Configuration;
using SerialPortLib;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Timers;

CommandLine.Parser.Default.ParseArguments<OptionsMutuallyExclusive>(args)
  .WithParsed(RunProcess)
  .WithNotParsed(HandleParseError);


static void RunProcess(OptionsMutuallyExclusive opts)
{

    int exitCode = 0;
    System.Timers.Timer responseWaitTimer;

    AssemblyMarker assemMarker = new();

    // Get default App Configuration
    var appConfig = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", true)
            .Build();

    // Setup Logging
    var loggerLevelSwitch = new LoggingLevelSwitch();
    Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(appConfig)
            .MinimumLevel.ControlledBy(loggerLevelSwitch)
            .CreateLogger();

    // Send SerialPortLib's log messages through Serilog (the "Serilog" section of appsettings.json,
    // Information and above). By default it wrote debug lines straight to the console, in the
    // middle of the command being typed.
    GLabs.Logging.LogManager.Initialize(new Serilog.Extensions.Logging.SerilogLoggerFactory(Log.Logger));

    var serialPort = new SerialPortInput();
    var lineReader = new CommandLineReader(CommandLineReader.DefaultHistoryFile("denon.serial.test"), excludeFromHistory: new[] { "q" });

    try
    {
        String? inputCommand;
        StringBuilder responseCache = new();
        Boolean comPortConnected = false;
        Boolean responseReady = false;
        Boolean responsReturned = false;
        Boolean inCommandResponseCapture = false;


        // Message header
        if (!opts.Quiet) Console.WriteLine(assemMarker.AppNameVersion);


        // Setup serial port

        serialPort.ConnectionStatusChanged += delegate (object sender, ConnectionStatusChangedEventArgs args)
        {
            comPortConnected = args.Connected;
            //Console.Write("\b\b\b\b\b\b\b\b\bChatter: {0}\nConnection: ", args.Connected);
        };


        serialPort.MessageReceived += delegate (object sender, MessageReceivedEventArgs args)
        {
            responsReturned = true;

            String response = Encoding.UTF8.GetString(args.Data, 0, args.Data.Length);
            if (!opts.Quiet)
            {
                response = response.Replace("\r", "\\r");
                response = response.Replace("\n", "\\n");
            }

            if (inCommandResponseCapture)
            {
                if (opts.HexDisplay && !opts.Quiet)
                {
                    Console.WriteLine($"Recieving: {BitConverter.ToString(System.Text.Encoding.UTF8.GetBytes(response))}");
                }
                responseCache.Append(response);
            }
            else
            {
                // Shown above the command being typed, which is then redrawn below it.
                if (!opts.Quiet) lineReader.WriteAbove($"Chatter: {response}");
            }

        };

        // Set port options
        serialPort.SetPort(opts.ComPortName, opts.ComSpeed, GetStopBits(opts), GetParity(opts), GetDataBits(opts));

        // Connect the serial port
        serialPort.Connect();
        if (!opts.Quiet)
        {
            if (comPortConnected)
            {
                Console.WriteLine("Serial port is connected");
                Console.WriteLine("Type Denon Commands Like 'PW?', 'ZM?', 'MV?', or 'q' to quit");
            }
            else
            {
                Console.WriteLine($"Serial port failed to connect with port=[{opts.ComPortName}], speed=[{opts.ComSpeed}], stop bits=[{opts.StopBits}], parity=[{opts.Parity}], data bits=[{opts.DataBits}]");
            }
        }


        // Read and process commands
        while (true)
        {
            // Run a command supplied to read commands interactively
            if (!String.IsNullOrEmpty(opts.Command))
            {
                inputCommand = opts.Command;
            }
            else
            {
                // Get input (Up/Down arrows recall earlier commands)
                inputCommand = lineReader.ReadLine("Command: ");
                if (inputCommand is null || inputCommand == "q") { break; }
            }

            // Send command to serial port, wait for response
            var message = System.Text.Encoding.UTF8.GetBytes($"{inputCommand}\r");
            responseReady = false;
            responsReturned = false;
            inCommandResponseCapture = true;

            if (opts.HexDisplay && !opts.Quiet) Console.WriteLine($"Sending: {BitConverter.ToString(message)}");
            serialPort.SendMessage(message);

            // Set response timer
            responseWaitTimer = new System.Timers.Timer(opts.ResponseWaitTime);
            responseWaitTimer.Elapsed += delegate (Object source, ElapsedEventArgs e)
            {
                responseReady = true;
            };
            responseWaitTimer.AutoReset = false;
            responseWaitTimer.Enabled = true;


            int timeout = 100;
            //int currentTimeout = 0;
            while (responseReady == false)
            {

                Thread.Sleep(timeout);
            }

            if (responsReturned)
            {
                if (!opts.Quiet)
                {
                    Console.WriteLine("Response: {0}", responseCache.ToString());
                }
                else
                {
                    Console.Write(responseCache.ToString());
                }
            }
            else
            {
                if (!opts.Quiet) Console.WriteLine("Timeout: Command did not respond within the Response Wait Time.");
            }
            inCommandResponseCapture = false;
            responseCache.Clear();

            if (!String.IsNullOrEmpty(opts.Command)) break; // exit if a command was supplied.
        }

    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
    finally
    {
        if (serialPort.IsConnected) serialPort.Disconnect();

        Log.CloseAndFlush();
        exitCode = exitCode + 1;
    }
}

static void HandleParseError(IEnumerable<Error> errs)
{

    Log.CloseAndFlush();
    Environment.Exit(-1);

}

static System.IO.Ports.StopBits GetStopBits(OptionsMutuallyExclusive opts)
{
    System.IO.Ports.StopBits returnValue = System.IO.Ports.StopBits.One;

    if (opts.StopBits == 0) returnValue = System.IO.Ports.StopBits.None;
    if (opts.StopBits == 1) returnValue = System.IO.Ports.StopBits.One;
    if (opts.StopBits == 1.5) returnValue = System.IO.Ports.StopBits.OnePointFive;
    if (opts.StopBits == 2) returnValue = System.IO.Ports.StopBits.Two;

    return returnValue;
}

static System.IO.Ports.Parity GetParity(OptionsMutuallyExclusive opts)
{
    System.IO.Ports.Parity returnValue = System.IO.Ports.Parity.None;

    if (opts.Parity.ToLower().Equals("none")) returnValue = System.IO.Ports.Parity.None;
    if (opts.Parity.ToLower().Equals("mark")) returnValue = System.IO.Ports.Parity.Mark;
    if (opts.Parity.ToLower().Equals("odd")) returnValue = System.IO.Ports.Parity.Odd;
    if (opts.Parity.ToLower().Equals("even")) returnValue = System.IO.Ports.Parity.Even;
    if (opts.Parity.ToLower().Equals("space")) returnValue = System.IO.Ports.Parity.Space;

    return returnValue;
}

static DataBits GetDataBits(OptionsMutuallyExclusive opts)
{
    DataBits returnValue = DataBits.Eight;

    if (opts.DataBits == 5) returnValue = DataBits.Five;
    if (opts.DataBits == 6) returnValue = DataBits.Six;
    if (opts.DataBits == 7) returnValue = DataBits.Seven;
    if (opts.DataBits == 8) returnValue = DataBits.Eight;

    return returnValue;
}
