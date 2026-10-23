using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;


namespace Denon.Serial.Test;

public class OptionsMutuallyExclusive
{
    [Option('n', "portName", Required = true, HelpText = "COM port name.", SetName = "CommandLine")]
    public string ComPortName { get; set; }

    [Option('b', "baudRate", Required = false, Default = 9600, HelpText = "Baud rate.", SetName = "CommandLine")]
    public int ComSpeed { get; set; }
    
    [Option('d', "dataBits", Required = false, Default = 8, HelpText = "Data bits.", SetName = "CommandLine")]
    public int DataBits { get; set; }

    [Option('s', "stopBits", Required = false, Default = 1, HelpText = "Stop bits.", SetName = "CommandLine")]
    public int StopBits { get; set; }

    [Option('p', "parity", Required = false, Default="None", HelpText = "Parity.", SetName = "CommandLine")]
    public string Parity { get; set; }

    [Option('h', "hexDisplay", Required = false, Default = false, HelpText = "Display communication with serail port in hex.", SetName = "CommandLine")]
    public Boolean HexDisplay { get; set; }

    //[Option('r', "responseTimeout", Required = false, Default = 10, HelpText = "Response timeout in seconds.", SetName = "CommandLine")]
    //public int ResponseTimeout { get; set; }

    [Option('w', "responseWaitTime", Required = false, Default = 2000, HelpText = "Wait time for complete response in milliseconds.", SetName = "CommandLine")]
    public int ResponseWaitTime { get; set; }

    [Option('c', "command", Required = false, Default = "", HelpText = "Command name to transmit and quit.", SetName = "CommandLine")]
    public string Command { get; set; }

    [Option('q', "quiet", Required = false, Default = false, HelpText = "Disable all output but recieved data.", SetName = "CommandLine")]
    public Boolean Quiet { get; set; }

    [Option('l', "loglevel", Required = false, Default = "Information", HelpText = "Logging level ('Error', 'Warning', 'Information', 'Debug')", SetName = "CommandLine")]
    public string Logging { get; set; }

}
