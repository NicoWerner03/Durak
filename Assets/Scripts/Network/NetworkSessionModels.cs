using System;
using System.Collections.Generic;
using DurakGame.Core;

namespace DurakGame.Network
{
    [Serializable]
    public class NetworkSessionInfo
    {
        public bool IsHost;
        public bool IsConnected;
        public int ConnectedPlayers;
        public string JoinCode = string.Empty;
    }

    [Serializable]
    public class NetworkMatchStartData
    {
        public int Seed;
        public int LocalPlayerId = -1;
        public List<PlayerSeat> Seats = new List<PlayerSeat>();
    }

    [Serializable]
    public class LobbyPlayerInfo
    {
        public ulong ClientId;
        public string DisplayName = string.Empty;
        public string PlayerIdentity = string.Empty;
        public bool IsHost;
        public bool IsReady;

        public LobbyPlayerInfo Clone()
        {
            return new LobbyPlayerInfo
            {
                ClientId = ClientId,
                DisplayName = DisplayName,
                PlayerIdentity = PlayerIdentity,
                IsHost = IsHost,
                IsReady = IsReady,
            };
        }
    }

    [Serializable]
    public class LobbyStateSnapshot
    {
        public List<LobbyPlayerInfo> Players = new List<LobbyPlayerInfo>();

        public LobbyStateSnapshot Clone()
        {
            var copy = new LobbyStateSnapshot
            {
                Players = new List<LobbyPlayerInfo>(Players.Count),
            };

            for (var i = 0; i < Players.Count; i++)
            {
                copy.Players.Add(Players[i].Clone());
            }

            return copy;
        }
    }
}
