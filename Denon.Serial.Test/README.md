# denon.serial.test
Serial communications cli for testing the Denon RS232 control protocaol

## Features
- Can be run in interactive and single command modes.
- Has option to display HEX values of transmitted and received data.
- Allows configuration of serial port.
- Has adjustable recieved data timeout to allow for multiple transmissions from AVR.

## Purpose
Like many of you, I have grown tired of purchasing single purposes devices to interconnect my home. I am on a mission to unify all this using open source tools and cheap single board computers (SBC). This is the first in a series of repositories which I have been using for years to accomplish my mission.

Specifically, I have enjoyed the sound, features and power of my Denon 4308CI AVR. As streaming music became more mainstream, I filed my CDs away and jumped in feet first. I stream my music to a Raspotify Raspberry Pi and output quality sound through an external DAC to my Denon 4308CI AVR. This required me to create tools to control the AVR from my mobile device.

Denon.Serial.Test is the toool I have created to test the RS232 serial connection on the Denon 4308CI AVR.

## Running the command
```
dotnet Denon.Serial.Test.dll
```

## Command options
  -n, --portName            Required. COM port name.

  -b, --baudRate            (Default: 9600) Baud rate.

  -d, --dataBits            (Default: 8) Data bits.

  -s, --stopBits            (Default: 1) Stop bits.

  -p, --parity              (Default: None) Parity.

  -h, --hexDisplay          (Default: false) Display communication with serail port in hex.

  -w, --responseWaitTime    (Default: 2000) Wait time for complete response in milliseconds.

  -c, --command             (Default: null) Command name to transmit and quit.

  -q, --quiet               (Default: false) Disable all output but recieved data.

  -l, --loglevel            (Default: Information) Logging level ('Error', 'Warning', 'Information', 'Debug')

  --help                    Display this help screen.

  --version                 Display version information.

## Setup
There are great internet resources regarding connecting to the RS232 on Denon AVRs. I waded through a number of them but wanted to distill the important aspects needed for this repository.
- Connect your computer and AVR using a straight through M/F serial cable.
- If using a SBC, the easiest way to surface a serial connection is using a USB to RS232 converter.
- The RS232, at least on my AVR, will transmit data when the AVR state is changed from the front panel or any other controlling device.
- The control protocol supported, at least on my AVR, is a subset of the full control protocol that the telnet control mechanism uses.
- When transmitting commands to the AVR, the ending carriage return '\r' is not needed.

## Example Usage
```
echo $(dotnet Denon.Serial.Test.dll -n /dev/ttyUSB0 -c "PW?" -q)
```
Run the master power status command and return the received data to the cli output

```
dotnet Denon.Serial.Test.dll -n /dev/ttyUSB0
```
Enter interactive mode to run commands from the command line

```
function dst() {
  (cd $HOME/denon.serial.test && $HOME/.dotnet/dotnet $HOME/denon.serial.test/Denon.Serial.Test.dll "$@")
}
```
This creates a command alias `dst` for easy usage. Place this code in your account startup script like `.bashrc`. Make sure the paths to .Net and this program are correct for your environment.

## Attribution
This project utilizes open source code. The following are some of the repositories used:
- [serialport-lib-dotnet](https://github.com/genielabs/serialport-lib-dotnet)
- [commandline](https://github.com/commandlineparser/commandline)

## License
This repository is open source software and licensed under the terms of [GNU GENERAL PUBLIC LICENSE](LICENSE).

