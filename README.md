# TCP Server Usage Guide

## Overview
This plugin provides a generic TCP server that listens for client connections and passes received data back to the `TcpServer` class. The server supports multiple simultaneous client connections and allows for configuration of the listening IP address and port.

## Configuration

### Device Configuration

```json
{
  "key": "tcp-server-1",
  "name": "TCP Server",
  "type": "tcpServer",
  "properties": {
    "addressToAcceptConnectionsFrom": "0.0.0.0",
    "port": 5001,
    "maxNumberOfClients": 2
  }
}
```

- **addressToAcceptConnectionsFrom** (string, required): The IP address to listen on
  - Use `"0.0.0.0"` to listen for any connection
  - Use a specific IP address like `"192.168.1.100"` to listen for a particular connection

- **port** (integer, required): The port number to listen on
  - Must be between 1 and 65535
  - Common choices: 5001, 9000, etc.

- **maxNumberOfClients** (integer, optional): Max number of clients to listen for
  - If omitted, will default to 5

### Bridge Configuration

```json
{
  "key": "devices-io-bridge",
  "type": "eiscApiAdvanced",
  "group": "api",
  "properties": {
    "control": { 
      "ipid": "a7", 
      "method": "ipidTcp", 
      "tcpSshProperties": { 
        "address": "127.0.0.2", 
        "port": 0 
      } 
    },
    "devices": [
      { 
        "deviceKey": "tcp-server-1", 
        "joinStart": 171 
      }
    ]
  }
}
```

### Bridge Join Map

#### Digital Joins

| Join | Name                 | I/O           | Description                                                        |
| ---- | -------------------- | ------------- | ------------------------------------------------------------------ |
| 1    | IsListening          | To/From SIMPL | Starts/stops the TCP server listening and reports listening status |
| 2    | DisconnectAllClients | From SIMPL    | Disconnects all connected clients                                  |

#### Analog Joins

| Join | Name             | I/O      | Description                           |
| ---- | ---------------- | -------- | ------------------------------------- |
| 2    | ClientsConnected | To SIMPL | Number of clients currently connected |

#### Serial Joins

| Join | Name         | I/O        | Description               |
| ---- | ------------ | ---------- | ------------------------- |
| 1    | DataReceived | To SIMPL   | Data received from client |
| 1    | DataSend     | From SIMPL | Data sent to client       |
| 2    | DeviceName   | To SIMPL   | Device Name               |
<!-- START Minimum Essentials Framework Versions -->
### Minimum Essentials Framework Versions

- 2.24.4
<!-- END Minimum Essentials Framework Versions -->
<!-- START Config Example -->
### Config Example

```json
{
    "key": "GeneratedKey",
    "uid": 1,
    "name": "GeneratedName",
    "type": "tcpServer",
    "group": "Group",
    "properties": {
        "control": "SampleValue",
        "AddressToAcceptConnectionsFrom": "SampleString",
        "Port": 0,
        "MaxNumberOfClients": 0
    }
}
```
<!-- END Config Example -->
<!-- START Supported Types -->
### Supported Types

- tcpServer
<!-- END Supported Types -->
<!-- START Join Maps -->
### Join Maps

#### Digitals

| Join | Type (RW) | Description |
| --- | --- | --- |
| 1 | R | Starts/stops the TCP server listening and reports listening status |
| 2 | R | Disconnects all connected clients |

#### Analogs

| Join | Type (RW) | Description |
| --- | --- | --- |
| 2 | R | Clients Connected |

#### Serials

| Join | Type (RW) | Description |
| --- | --- | --- |
| 1 | R | Data received from client |
| 1 | R | Data sent to client |
| 2 | R | Device Name |
<!-- END Join Maps -->
<!-- START Interfaces Implemented -->
### Interfaces Implemented

- IDisposable
<!-- END Interfaces Implemented -->
<!-- START Base Classes -->
### Base Classes

- JoinMapBaseAdvanced
- EssentialsBridgeableDevice
<!-- END Base Classes -->
<!-- START Public Methods -->
### Public Methods

- public void SendTextToBridge(string text)
- public void SendBytesToBridge(byte[] bytes)
- public void SendTextToAllClients(string text)
- public void SendBytesToAllClients(byte[] bytes)
- public void StartServer()
- public void StopServer()
- public void DisconnectAllClients()
- public void Dispose()
<!-- END Public Methods -->
<!-- START Bool Feedbacks -->
### Bool Feedbacks

- IsListeningFeedback
<!-- END Bool Feedbacks -->
<!-- START Int Feedbacks -->
### Int Feedbacks

- ClientsConnectedFeedback
<!-- END Int Feedbacks -->
<!-- START String Feedbacks -->

<!-- END String Feedbacks -->
