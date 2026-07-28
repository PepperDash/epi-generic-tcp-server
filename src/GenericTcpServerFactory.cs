using PepperDash.Core;
using PepperDash.Essentials.Core;
using System.Collections.Generic;

namespace PepperDash.Essentials.Plugins
{
	/// <summary>
	/// Plugin device factory for devices that use IBasicCommunication
	/// </summary>
	public class GenericTcpServerFactory : EssentialsPluginDeviceFactory<GenericTcpServer>
	{
		/// <summary>
		/// Plugin device factory constructor
		/// </summary>
		public GenericTcpServerFactory()
		{
			// Set the minimum Essentials Framework Version
			MinimumEssentialsFrameworkVersion = "2.24.4";

			// In the constructor we initialize the list with the typenames that will build an instance of this device
			// TODO [ ] Update the TypeNames for the plugin being developed
			TypeNames = new List<string>() { "tcpServer" };
		}

		/// <summary>
		/// Builds and returns an instance of EssentialsPluginDeviceTemplate
		/// </summary>
		/// <param name="dc">device configuration</param>
		/// <returns>plugin device or null</returns>
		public override EssentialsDevice BuildDevice(PepperDash.Essentials.Core.Config.DeviceConfig dc)
		{
			Debug.LogVerbose("[{key}] Factory Attempting to create new device from type: {type}", dc.Key, dc.Type);

			// get the plugin device properties configuration object & check for null 
			var propertiesConfig = dc.Properties.ToObject<GenericTcpServerConfig>();
			if (propertiesConfig != null) return new GenericTcpServer(dc.Key, dc.Name, propertiesConfig);

			Debug.LogError("[{key}] Factory: failed to read properties config for {name}", dc.Key, dc.Name);
			return null;
		}
	}
}

