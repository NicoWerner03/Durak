using System.Threading.Tasks;
using Unity.Netcode;

namespace DurakGame.Network
{
    public interface INetworkSessionService
    {
        NetworkManager NetworkManager { get; }

        string CurrentJoinCode { get; }

        Task<NetworkSessionInfo> CreateSessionAsync(int maxPlayers);

        Task<NetworkSessionInfo> JoinSessionAsync(string joinCode);

        void LeaveSession();

        bool StartMatch();
    }
}
