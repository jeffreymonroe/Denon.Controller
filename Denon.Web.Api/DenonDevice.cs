using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Denon.Web.Api
{

    public interface IDenonDevice
    {
        string GetTargetHost();

        bool LoadDeviceConfig(string filename);

        Denon.Core.Model.ConfigurationItem GetDeviceConfiguration();

        // The "model" from the device schema.
        string GetDeviceModel();

        Denon.Core.Model.CommandItem GetDeviceCommand(string commandName);

    }


    public class DenonDevice : IDenonDevice
    {
        private Denon.Core.Model.Device _targetDevice;
        private readonly string _targetHost;

        public DenonDevice(string filename, string targetHost)
        {
            if (!LoadDeviceConfig(filename))
            {
                throw new InvalidOperationException($"Device schema '{filename}' could not be loaded.");
            }

            _targetHost = targetHost;
        }

        public string GetTargetHost()
        {
            return _targetHost;
        }

        // Loads (or reloads) the device schema. Returns false if the file is empty or has no
        // commands; a missing file or invalid JSON throws, so the cause isn't hidden.
        public bool LoadDeviceConfig(string filename)
        {
            JsonSerializerOptions options = new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true
            };

            var device = JsonSerializer.Deserialize<Denon.Core.Model.Device>(System.IO.File.ReadAllText(filename), options);

            if (device?.Commands is null || device.Commands.Count == 0) return false;

            device.Configuration ??= new Denon.Core.Model.ConfigurationItem();
            _targetDevice = device;

            return true;
        }


        public string GetDeviceModel()
        {
            return _targetDevice?.Model;
        }


        public Denon.Core.Model.ConfigurationItem GetDeviceConfiguration()
        {
            if (_targetDevice is null) throw new InvalidOperationException("Device has not been loaded.");

            return _targetDevice.Configuration;
        }


        public Denon.Core.Model.CommandItem GetDeviceCommand(string commandName)
        {
            if (_targetDevice is null) throw new NullReferenceException("Device has not been loaded.");

            Denon.Core.Model.CommandItem commandItem = null;

            if (!_targetDevice.Commands.Exists(d => d.Name == commandName)) throw new NullReferenceException(string.Format("Command [{0}] not found.", commandName));

            commandItem = _targetDevice.Commands.Where(d => d.Name == commandName).FirstOrDefault();

            return CreateDeepCopy<Denon.Core.Model.CommandItem>(commandItem);

        }



        // Help functions
        private static T CreateDeepCopy<T>(T obj)
        {
            T clone;

            using (MemoryStream stream = new MemoryStream())
            {
                System.Xml.XmlWriterSettings ws = new System.Xml.XmlWriterSettings
                {
                    NewLineHandling = System.Xml.NewLineHandling.Entitize
                };

                System.Xml.Serialization.XmlSerializer x = new System.Xml.Serialization.XmlSerializer(obj.GetType());
                using (System.Xml.XmlWriter wr = System.Xml.XmlWriter.Create(stream, ws))
                {
                    x.Serialize(wr, obj);
                }
                stream.Position = 0;

                System.Xml.XmlReaderSettings rs = new System.Xml.XmlReaderSettings();

                using System.Xml.XmlReader rd = System.Xml.XmlReader.Create(stream, rs);
                clone = (T)x.Deserialize(rd, "");
            }

            return clone;
        }

    }
}
