using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugins
{
	/// <summary>
	/// Plugin device Bridge Join Map
	/// </summary>
	public class GenericTcpServerBridgeJoinMap : JoinMapBaseAdvanced
	{
		#region Digital

		[JoinName("IsListening")]
		public JoinDataComplete IsListening = new JoinDataComplete(
			new JoinData
			{
				JoinNumber = 1,
				JoinSpan = 1
			},
			new JoinMetadata
			{
				Description = "Starts/stops the TCP server listening and reports listening status",
				JoinCapabilities = eJoinCapabilities.ToFromSIMPL,
				JoinType = eJoinType.Digital
			});

		[JoinName("DisconnectAllClients")]
		public JoinDataComplete DisconnectAllClients = new JoinDataComplete(
			new JoinData
			{
				JoinNumber = 2,
				JoinSpan = 1
			},
			new JoinMetadata
			{
				Description = "Disconnects all connected clients",
				JoinCapabilities = eJoinCapabilities.FromSIMPL,
				JoinType = eJoinType.Digital
			});

		#endregion


		#region Analog		
			
		[JoinName("ClientsConnected")]
		public JoinDataComplete ClientsConnected = new JoinDataComplete(
			new JoinData
			{
				JoinNumber = 2,
				JoinSpan = 1
			},
			new JoinMetadata
			{
				Description = "Clients Connected",
				JoinCapabilities = eJoinCapabilities.ToSIMPL,
				JoinType = eJoinType.Analog
			});

		#endregion


		#region Serial

		[JoinName("DataReceived")]
		public JoinDataComplete DataReceived = new JoinDataComplete(
			new JoinData
			{
				JoinNumber = 1,
				JoinSpan = 1
			},
			new JoinMetadata
			{
				Description = "Data received from client",
				JoinCapabilities = eJoinCapabilities.ToSIMPL,
				JoinType = eJoinType.Serial
			});

		
		[JoinName("DataSend")]
		public JoinDataComplete DataSend = new JoinDataComplete(
			new JoinData
			{
				JoinNumber = 1,
				JoinSpan = 1
			},
			new JoinMetadata
			{
				Description = "Data sent to client",
				JoinCapabilities = eJoinCapabilities.FromSIMPL,
				JoinType = eJoinType.Serial
			});

        [JoinName("DeviceName")]
        public JoinDataComplete DeviceName = new JoinDataComplete(
            new JoinData
            {
                JoinNumber = 2,
                JoinSpan = 1
            },
            new JoinMetadata
            {
                Description = "Device Name",
                JoinCapabilities = eJoinCapabilities.ToSIMPL,
                JoinType = eJoinType.Serial
            });

        #endregion

        /// <summary>
        /// Plugin device BridgeJoinMap constructor
        /// </summary>
        /// <param name="joinStart">This will be the join it starts on the EISC bridge</param>
        public GenericTcpServerBridgeJoinMap(uint joinStart)
            : base(joinStart, typeof(GenericTcpServerBridgeJoinMap))
		{
		}
	}
}