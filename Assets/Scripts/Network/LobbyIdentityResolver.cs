using System;
using DurakGame.Core;

namespace DurakGame.Network
{
    public static class LobbyIdentityResolver
    {
        public static int ResolveLocalLobbyPlayerIndex(
            LobbyStateSnapshot snapshot,
            ulong localClientId,
            string localPlayerIdentity)
        {
            if (snapshot == null || snapshot.Players == null)
            {
                return -1;
            }

            for (var i = 0; i < snapshot.Players.Count; i++)
            {
                var player = snapshot.Players[i];
                if (player != null && player.ClientId == localClientId)
                {
                    return i;
                }
            }

            return ResolveUniqueLobbyIdentityMatchIndex(snapshot, localPlayerIdentity);
        }

        public static bool TryResolveLocalLobbyPlayer(
            LobbyStateSnapshot snapshot,
            ulong localClientId,
            string localPlayerIdentity,
            out LobbyPlayerInfo player)
        {
            player = null;
            var index = ResolveLocalLobbyPlayerIndex(snapshot, localClientId, localPlayerIdentity);
            if (index < 0 || snapshot == null || snapshot.Players == null || index >= snapshot.Players.Count)
            {
                return false;
            }

            player = snapshot.Players[index];
            return player != null;
        }

        public static bool IsLocalLobbyPlayer(LobbyPlayerInfo player, ulong localClientId, string localPlayerIdentity)
        {
            if (player == null)
            {
                return false;
            }

            if (player.ClientId == localClientId)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(localPlayerIdentity) &&
                   string.Equals(player.PlayerIdentity, localPlayerIdentity, StringComparison.Ordinal);
        }

        public static bool TryGetLocalLobbyReady(
            LobbyStateSnapshot snapshot,
            ulong localClientId,
            string localPlayerIdentity,
            out bool isReady)
        {
            isReady = false;
            if (!TryResolveLocalLobbyPlayer(snapshot, localClientId, localPlayerIdentity, out var player))
            {
                return false;
            }

            isReady = player.IsReady;
            return true;
        }

        public static int ResolveLocalPlayerId(GameState state, ulong localClientId, string localPlayerIdentity)
        {
            if (state == null || state.Players == null)
            {
                return -1;
            }

            for (var i = 0; i < state.Players.Count; i++)
            {
                var player = state.Players[i];
                if (player == null || player.IsBot)
                {
                    continue;
                }

                if (player.OwnerClientId == localClientId)
                {
                    return player.PlayerId;
                }
            }

            if (string.IsNullOrWhiteSpace(localPlayerIdentity))
            {
                return -1;
            }

            var matchedPlayerId = -1;
            for (var i = 0; i < state.Players.Count; i++)
            {
                var player = state.Players[i];
                if (player == null || player.IsBot)
                {
                    continue;
                }

                if (!string.Equals(player.PlayerIdentity, localPlayerIdentity, StringComparison.Ordinal))
                {
                    continue;
                }

                if (matchedPlayerId >= 0)
                {
                    return -1;
                }

                matchedPlayerId = player.PlayerId;
            }

            return matchedPlayerId;
        }

        private static int ResolveUniqueLobbyIdentityMatchIndex(LobbyStateSnapshot snapshot, string localPlayerIdentity)
        {
            if (snapshot == null || snapshot.Players == null || string.IsNullOrWhiteSpace(localPlayerIdentity))
            {
                return -1;
            }

            var matchIndex = -1;
            for (var i = 0; i < snapshot.Players.Count; i++)
            {
                var player = snapshot.Players[i];
                if (player == null ||
                    !string.Equals(player.PlayerIdentity, localPlayerIdentity, StringComparison.Ordinal))
                {
                    continue;
                }

                if (matchIndex >= 0)
                {
                    return -1;
                }

                matchIndex = i;
            }

            return matchIndex;
        }
    }
}
