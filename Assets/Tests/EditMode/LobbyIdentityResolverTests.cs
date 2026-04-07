using System.Collections.Generic;
using DurakGame.Core;
using DurakGame.Network;
using NUnit.Framework;

namespace DurakGame.Tests
{
    public class LobbyIdentityResolverTests
    {
        [Test]
        public void IsLocalLobbyPlayer_MatchesByClientId()
        {
            var player = new LobbyPlayerInfo
            {
                ClientId = 77,
                PlayerIdentity = "remote",
            };

            Assert.IsTrue(LobbyIdentityResolver.IsLocalLobbyPlayer(player, 77, "local-id"));
        }

        [Test]
        public void IsLocalLobbyPlayer_MatchesByIdentity_WhenClientIdChanged()
        {
            var player = new LobbyPlayerInfo
            {
                ClientId = 91,
                PlayerIdentity = "same-device",
            };

            Assert.IsTrue(LobbyIdentityResolver.IsLocalLobbyPlayer(player, 12, "same-device"));
        }

        [Test]
        public void TryGetLocalLobbyReady_PrefersClientIdOverIdentity()
        {
            var snapshot = new LobbyStateSnapshot
            {
                Players = new List<LobbyPlayerInfo>
                {
                    new LobbyPlayerInfo { ClientId = 5, PlayerIdentity = "old", IsReady = false },
                    new LobbyPlayerInfo { ClientId = 9, PlayerIdentity = "me", IsReady = true },
                },
            };

            var found = LobbyIdentityResolver.TryGetLocalLobbyReady(snapshot, localClientId: 5, localPlayerIdentity: "me", out var isReady);

            Assert.IsTrue(found);
            Assert.IsFalse(isReady);
        }

        [Test]
        public void TryGetLocalLobbyReady_FallsBackToClientId_WhenIdentityMissing()
        {
            var snapshot = new LobbyStateSnapshot
            {
                Players = new List<LobbyPlayerInfo>
                {
                    new LobbyPlayerInfo { ClientId = 42, PlayerIdentity = string.Empty, IsReady = true },
                },
            };

            var found = LobbyIdentityResolver.TryGetLocalLobbyReady(snapshot, localClientId: 42, localPlayerIdentity: string.Empty, out var isReady);

            Assert.IsTrue(found);
            Assert.IsTrue(isReady);
        }

        [Test]
        public void ResolveLocalPlayerId_UsesIdentity_WhenOwnerClientIdStale()
        {
            var state = new GameState
            {
                Players = new List<PlayerState>
                {
                    new PlayerState { PlayerId = 0, IsBot = false, OwnerClientId = 100, PlayerIdentity = "remote" },
                    new PlayerState { PlayerId = 1, IsBot = false, OwnerClientId = 200, PlayerIdentity = "me" },
                },
            };

            var localPlayerId = LobbyIdentityResolver.ResolveLocalPlayerId(state, localClientId: 999, localPlayerIdentity: "me");
            Assert.AreEqual(1, localPlayerId);
        }

        [Test]
        public void TryResolveLocalLobbyPlayer_PrefersClientId_WhenIdentityIsDuplicated()
        {
            var snapshot = new LobbyStateSnapshot
            {
                Players = new List<LobbyPlayerInfo>
                {
                    new LobbyPlayerInfo { ClientId = 1, PlayerIdentity = "same-device", IsReady = true },
                    new LobbyPlayerInfo { ClientId = 2, PlayerIdentity = "same-device", IsReady = false },
                },
            };

            var resolved = LobbyIdentityResolver.TryResolveLocalLobbyPlayer(
                snapshot,
                localClientId: 2,
                localPlayerIdentity: "same-device",
                out var localPlayer);

            Assert.IsTrue(resolved);
            Assert.IsNotNull(localPlayer);
            Assert.AreEqual((ulong)2, localPlayer.ClientId);
        }

        [Test]
        public void TryResolveLocalLobbyPlayer_ReturnsFalse_WhenIdentityDuplicateAndClientIdMissing()
        {
            var snapshot = new LobbyStateSnapshot
            {
                Players = new List<LobbyPlayerInfo>
                {
                    new LobbyPlayerInfo { ClientId = 11, PlayerIdentity = "same-device", IsReady = true },
                    new LobbyPlayerInfo { ClientId = 12, PlayerIdentity = "same-device", IsReady = false },
                },
            };

            var resolved = LobbyIdentityResolver.TryResolveLocalLobbyPlayer(
                snapshot,
                localClientId: 99,
                localPlayerIdentity: "same-device",
                out var localPlayer);

            Assert.IsFalse(resolved);
            Assert.IsNull(localPlayer);
        }

        [Test]
        public void ResolveLocalPlayerId_PrefersOwnerClientId_WhenIdentityIsDuplicated()
        {
            var state = new GameState
            {
                Players = new List<PlayerState>
                {
                    new PlayerState { PlayerId = 0, IsBot = false, OwnerClientId = 1, PlayerIdentity = "same-device" },
                    new PlayerState { PlayerId = 1, IsBot = false, OwnerClientId = 2, PlayerIdentity = "same-device" },
                },
            };

            var localPlayerId = LobbyIdentityResolver.ResolveLocalPlayerId(state, localClientId: 2, localPlayerIdentity: "same-device");
            Assert.AreEqual(1, localPlayerId);
        }

        [Test]
        public void ResolveLocalPlayerId_ReturnsMinusOne_WhenNoMatch()
        {
            var state = new GameState
            {
                Players = new List<PlayerState>
                {
                    new PlayerState { PlayerId = 0, IsBot = false, OwnerClientId = 100, PlayerIdentity = "remote" },
                },
            };

            var localPlayerId = LobbyIdentityResolver.ResolveLocalPlayerId(state, localClientId: 9, localPlayerIdentity: "me");
            Assert.AreEqual(-1, localPlayerId);
        }

        [Test]
        public void Clones_PreservePlayerIdentityFields()
        {
            var seat = new PlayerSeat
            {
                PlayerId = 0,
                PlayerIdentity = "seat-id",
            };

            var playerState = new PlayerState
            {
                PlayerId = 1,
                PlayerIdentity = "player-id",
            };

            var lobbyPlayer = new LobbyPlayerInfo
            {
                ClientId = 123,
                PlayerIdentity = "lobby-id",
            };

            Assert.AreEqual("seat-id", seat.Clone().PlayerIdentity);
            Assert.AreEqual("player-id", playerState.Clone().PlayerIdentity);
            Assert.AreEqual("lobby-id", lobbyPlayer.Clone().PlayerIdentity);
        }
    }
}
