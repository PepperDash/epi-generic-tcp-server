using Newtonsoft.Json;
using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugins
{
	/// <summary>
	/// Plugin device configuration object
	/// </summary>
	public class GenericTcpServerPropertiesConfig
	{
		[JsonProperty("control")]
        public EssentialsControlPropertiesConfig Control { get; set; }

		
		[JsonProperty("addressToAcceptConnectionsFrom", DefaultValueHandling = DefaultValueHandling.Ignore)]
		public string AddressToAcceptConnectionsFrom { get; set; }

		[JsonProperty("port", DefaultValueHandling = DefaultValueHandling.Ignore)]
		public int Port { get; set; }

		/// <summary>
		/// Maximum number of simultaneously connected clients; 0 or omitted means no limit
		/// </summary>
		[JsonProperty("maxNumberOfClients", DefaultValueHandling = DefaultValueHandling.Ignore)]
		public int MaxNumberOfClients { get; set; }

		/// <summary>
		/// Constuctor
		/// </summary>
		public GenericTcpServerPropertiesConfig()
		{

		}
	}
}