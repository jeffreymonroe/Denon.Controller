using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;


namespace Denon.Telnet.Test;

public class OptionsMutuallyExclusive
{
    [Option('a', "hostAddress", Required = true, HelpText = "Host name or ip address of telnet endpoint.", SetName = "CommandLine")]
    public string Host { get; set; }

    [Option('p', "port", Required = false, Default = 23, HelpText = "Telnet port.", SetName = "CommandLine")]
    public int Port { get; set; }

    [Option('t', "responseTerminator", Required = false, Default = "\x0D", HelpText = "Character sent to determine response end.", SetName = "CommandLine")]
    public string ResponseTerminator { get; set; }

    [Option('h', "hexDisplay", Required = false, Default = false, HelpText = "Display the bytes sent in hex.", SetName = "CommandLine")]
    public Boolean HexDisplay { get; set; }

    [Option('w', "responseWaitTime", Required = false, Default = 2000, HelpText = "Wait time for complete response in milliseconds.", SetName = "CommandLine")]
    public int ResponseWaitTime { get; set; }

    [Option('c', "command", Required = false, Default = "", HelpText = "Command name to transmit and quit.", SetName = "CommandLine")]
    public string Command { get; set; }

    [Option('q', "quiet", Required = false, Default = false, HelpText = "Disable all output but recieved data.", SetName = "CommandLine")]
    public Boolean Quiet { get; set; }

    [Option('l', "loglevel", Required = false, Default = "Information", HelpText = "Logging level ('Error', 'Warning', 'Information', 'Debug')", SetName = "CommandLine")]
    public string Logging { get; set; }

}
