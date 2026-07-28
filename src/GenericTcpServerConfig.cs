using Newtonsoft.Json;
using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugins
{
	/// <summary>
	/// Plugin device configuration object
	/// </summary>
	public class GenericTcpServerConfig
	{
		[JsonProperty("control")]
        public EssentialsControlPropertiesConfig Control { get; set; }

		
		[JsonProperty("addressToAcceptConnectionsFrom", DefaultValueHandling = DefaultValueHandling.Ignore)]
		public string AddressToAcceptConnectionsFrom { get; set; }

		[JsonProperty("port", DefaultValueHandling = DefaultValueHandling.Ignore)]
		public int Port { get; set; }

		[JsonProperty("maxNumberOfClients", DefaultValueHandling = DefaultValueHandling.Ignore)]
		public int MaxNumberOfClients { get; set; } = 5;

		/// <summary>
		/// Constuctor
		/// </summary>
		public GenericTcpServerConfig()
		{

		}
	}
}