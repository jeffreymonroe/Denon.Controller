using CommandLine;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using System.Reflection;
using Denon.Telnet.Test;
using Denon.Core.Command;
using System.Text.RegularExpressions;

// RunProcess is awaited so the program keeps running until it finishes. (As "async void" it
// returned at its first await and the program exited before sending any command.)
var parsed = CommandLine.Parser.Default.ParseArguments<OptionsMutuallyExclusive>(args);
await parsed.WithParsedAsync(RunProcess);
parsed.WithNotParsed(HandleParseError);

static async Task RunProcess(OptionsMutuallyExclusive opts)
{
    int exitCode = 0;
    //System.Timers.Timer responseWaitTimer;

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

    // The same connection class the Web API uses, so the tool exercises the same code.
    TcpConnection? connection = null;

    try
    {
        String? inputCommand;
        String response = string.Empty;
        var lineReader = new CommandLineReader(CommandLineReader.DefaultHistoryFile("denon.telnet.test"), excludeFromHistory: new[] { "q" });

        // Message header
        if (!opts.Quiet) Console.WriteLine($"Denon.Telnet.Test {Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion}");


        // Connect now, so a wrong address or port is reported before asking for commands.
        connection = new TcpConnection(opts.Host, opts.Port);
        try
        {
            await connection.ConnectAsync();
        }
        catch (DeviceUnreachableException e)
        {
            Console.WriteLine(e.Message);
            return;
        }

        if (!opts.Quiet)
        {
            Console.WriteLine($"Connected to {opts.Host}:{opts.Port}");
            Console.WriteLine("Type Denon Commands Like 'PW?', 'ZM?', 'MV?', or 'q' to quit");
        }

        // Reading a reply stops at the first match of the terminator (default: CR), or after the wait time.
        string terminatorMatch = Regex.Escape(opts.ResponseTerminator);


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


            // Send the command (Denon commands end with a carriage return) and wait for the reply.
            string command = $"{inputCommand}\r";
            if (opts.HexDisplay && !opts.Quiet) Console.WriteLine($"Sending: {BitConverter.ToString(System.Text.Encoding.Latin1.GetBytes(command))}");
            response = await connection.Send(command, terminatorMatch, opts.ResponseWaitTime);

            if (!opts.Quiet)
            {
                // Show the receiver's CR/LF line endings as \r and \n (as the serial tool does);
                // printed raw, a CR returns to the start of the line and the next reply line overwrites it.
                Console.WriteLine("Response: {0}", response.Replace("\r", "\\r").Replace("\n", "\\n"));
            }
            else
            {
                Console.Write(response);
            }

            if (!String.IsNullOrEmpty(opts.Command)) break; // exit if a command was supplied.
        }

    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
    finally
    {
        connection?.Dispose();

        Log.CloseAndFlush();
        exitCode++;
    }
}

static void HandleParseError(IEnumerable<Error> errs)
{

    Log.CloseAndFlush();
    Environment.Exit(-1);

}

