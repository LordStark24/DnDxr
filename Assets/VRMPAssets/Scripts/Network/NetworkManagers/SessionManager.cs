using System.Collections.Generic;
using Unity.Services.Lobbies;
using UnityEngine;
using System.Threading.Tasks;
using System;
using Unity.XR.CoreUtils.Bindings.Variables;
using UnityEngine.SceneManagement;
using Unity.Services.Multiplayer;
using System.Collections;
using Unity.Netcode.Transports.UTP;
using Unity.Netcode;

namespace XRMultiplayer
{
    public enum SessionType
    {
        DistributedAuthority,
        LocalOnly,
    }

    public class SessionManager : MonoBehaviour
    {
        public const string k_JoinCodeKeyIdentifier = "j";
        public const string k_RegionKeyIdentifier = "r";
        public const string k_BuildIdKeyIdentifier = "b";
        public const string k_SceneKeyIdentifier = "s";
        public const string k_EditorKeyIdentifier = "e";

        const string k_MainPublicHubSessionId = "main-public-hub-v1";
        const string k_MainPublicHubSessionName = "Main Public Hub";

        public SessionType sessionType => m_SessionType;

        [SerializeField, Tooltip("The type of session to create.")]
        SessionType m_SessionType = SessionType.DistributedAuthority;

        public bool hideEditorFromLobby = false;

        public Action<string> OnSessionFailed;

        public ISession currentSession => m_CurrentSession;
        ISession m_CurrentSession;

        public static IReadOnlyBindableVariable<string> status
        {
            get => m_Status;
        }

        readonly static BindableVariable<string> m_Status = new("");

        const string k_DebugPrepend = "<color=#EC0CFA>[Lobby Manager]</color> ";

        UnityTransport m_DATransport;
        UnityTransport m_LocalTransport;

        private void Awake()
        {
            if (!Application.isEditor)
            {
                hideEditorFromLobby = false;
            }

            if (sessionType == SessionType.DistributedAuthority &&
                Application.internetReachability == NetworkReachability.NotReachable)
            {
                Utils.Log($"{k_DebugPrepend}Distributed Authority request, but no internet connection detected. Falling back to Local Only connection.", 1);
                m_SessionType = SessionType.LocalOnly;
            }
        }

        private void Start()
        {
            if (sessionType == SessionType.LocalOnly)
                SetupLocalTransport();
        }

        void SetupLocalTransport()
        {
            if (TryGetComponent(out VoiceChatManager voiceChatManager))
            {
                voiceChatManager.enabled = false;
                XRINetworkGameManager.Instance.positionalVoiceChat = false;
                Utils.LogWarning($"{k_DebugPrepend}VoiceChatManager is not supported in {sessionType} sessions. Disabling it.");
            }

            if (!NetworkManager.Singleton.gameObject.TryGetComponent(out m_DATransport))
            {
                Utils.LogError($"{k_DebugPrepend}No Unity Transport found.");
                return;
            }

            m_LocalTransport = NetworkManager.Singleton.gameObject.AddComponent<UnityTransport>();
            m_LocalTransport.ConnectionData = m_DATransport.ConnectionData;
            m_LocalTransport.ConnectionData.ServerListenAddress = "0.0.0.0";
            NetworkManager.Singleton.NetworkConfig.NetworkTransport = m_LocalTransport;

            Destroy(m_DATransport);
        }

        public async Task<ISession> QuickJoinLobby()
        {
            m_Status.Value = "Checking For Existing Lobbies.";

            try
            {
                QuerySessionsResults results = await MultiplayerService.Instance.QuerySessionsAsync(GetQuickJoinFilterOptions());

                bool createOwn = true;

                if (results.Sessions.Count > 0)
                {
                    try
                    {
                        foreach (var session in results.Sessions)
                        {
                            if (session.AvailableSlots > 0)
                            {
                                await JoinLobby(session);
                                createOwn = false;
                                break;
                            }

                            Utils.Log($"{k_DebugPrepend}Skipping full session: {session.Name}");
                        }
                    }
                    catch (Exception e)
                    {
                        Utils.LogWarning($"Sessions Exist, but failed to connect: {e}");
                    }
                }

                if (createOwn)
                {
                    m_Status.Value = "No Available Session. Creating New Session.";
                    await CreateSession(maxPlayers: XRINetworkGameManager.maxPlayers);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            return m_CurrentSession;
        }

        public async Task<ISession> CreateOrJoinMainPublicHub()
        {
            try
            {
                m_Status.Value = "Connecting To Main Public Hub";

                SessionOptions options = GetSessionOptions(
                    k_MainPublicHubSessionName,
                    false,
                    XRINetworkGameManager.maxPlayers
                ).WithDistributedAuthorityNetwork();

                StopAllCoroutines();
                StartCoroutine(PlayConnectionMessage());

                m_CurrentSession = await MultiplayerService.Instance.CreateOrJoinSessionAsync(
                    k_MainPublicHubSessionId,
                    options
                );

                ConnectedToSession();

                return m_CurrentSession;
            }
            catch (Exception e)
            {
                string failureMessage = "Failed to connect to Main Public Hub.";
                Utils.Log($"{k_DebugPrepend}{failureMessage}\n\n{e}", 1);
                OnSessionFailed?.Invoke(failureMessage);
                return null;
            }
        }

        public async Task<ISession> JoinLobby(ISessionInfo sessionInfo = null, string roomCode = null)
        {
            try
            {
                if (sessionInfo != null)
                {
                    m_Status.Value = "Connecting To Room: " + sessionInfo.Name;
                    StopAllCoroutines();
                    StartCoroutine(PlayConnectionMessage());
                    m_CurrentSession = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionInfo.Id);
                }
                else if (!string.IsNullOrEmpty(roomCode))
                {
                    m_Status.Value = "Connecting To Room Code: " + roomCode;
                    StopAllCoroutines();
                    StartCoroutine(PlayConnectionMessage());
                    m_CurrentSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(roomCode);
                }
                else
                {
                    m_CurrentSession = await QuickJoinLobby();
                }

                ConnectedToSession();

                return m_CurrentSession;
            }
            catch (Exception e)
            {
                string failureMessage = "Failed to Join Lobby.";
                Utils.Log($"{k_DebugPrepend}{e.Message}", 1);

                if (e is LobbyServiceException)
                {
                    string message = e.Message.ToLower();

                    if (message.Contains("rate limit"))
                        failureMessage = "Rate limit exceeded. Please try again later.";
                    else if (message.Contains("lobby not found"))
                        failureMessage = "Lobby not found. Please try a new Lobby.";
                    else
                        failureMessage = e.Message;
                }

                Utils.Log($"{k_DebugPrepend}{failureMessage}\n\n{e}", 1);
                OnSessionFailed?.Invoke(failureMessage);
                return null;
            }
        }

        public async Task<ISession> CreateSession(string roomName = null, bool isPrivate = false, int maxPlayers = XRINetworkGameManager.maxPlayers)
        {
            string sessionName = roomName;

            if (string.IsNullOrEmpty(sessionName))
                sessionName = $"{XRINetworkGameManager.LocalPlayerName.Value}'s Room";

            try
            {
                m_Status.Value = "Connecting To Room: " + sessionName;

                SessionOptions options = GetSessionOptions(sessionName, isPrivate, maxPlayers).WithDistributedAuthorityNetwork();

                Guid sessionId = Guid.NewGuid();

                StopAllCoroutines();
                StartCoroutine(PlayConnectionMessage());

                m_CurrentSession = await MultiplayerService.Instance.CreateOrJoinSessionAsync(
                    sessionId.ToString(),
                    options
                );

                ConnectedToSession();

                return m_CurrentSession;
            }
            catch (Exception e)
            {
                string failureMessage = $"Failed to connect to {sessionName}. Please try again.";
                Utils.Log($"{k_DebugPrepend}{failureMessage}\n\n{e}", 1);
                OnSessionFailed?.Invoke(failureMessage);
                return null;
            }
        }

        SessionOptions GetSessionOptions(string lobbyName, bool isPrivate = false, int maxPlayers = XRINetworkGameManager.maxPlayers)
        {
            SessionOptions options = new SessionOptions()
            {
                Name = lobbyName,
                MaxPlayers = maxPlayers,
                SessionProperties = new Dictionary<string, SessionProperty>
                {
                    { k_BuildIdKeyIdentifier, new SessionProperty(Application.version) },
                    { k_SceneKeyIdentifier, new SessionProperty(SceneManager.GetActiveScene().name) },
                    { k_EditorKeyIdentifier, new SessionProperty(hideEditorFromLobby.ToString()) }
                },
                IsPrivate = sessionType == SessionType.LocalOnly ? true : isPrivate,
            };

            return options;
        }

        IEnumerator PlayConnectionMessage()
        {
            yield return new WaitForSeconds(2f);
            m_Status.Value = "Systems functional.";
            yield return new WaitForSeconds(1f);
            m_Status.Value = "Checklist protocol initiated.";
            yield return new WaitForSeconds(1f);
            m_Status.Value = "Receiving transmission.";
            yield return new WaitForSeconds(1f);
            m_Status.Value = "Hailing frequencies open.";
            yield return new WaitForSeconds(1f);
            m_Status.Value = "Systems online.";
            yield return new WaitForSeconds(2f);
            m_Status.Value = "Systems validating.";
            yield return new WaitForSeconds(3f);
            m_Status.Value = "Signal unstable.";
        }

        void ConnectedToSession()
        {
            if (m_CurrentSession == null)
            {
                Utils.Log($"{k_DebugPrepend}Connected session is null.", 1);
                return;
            }

            XRINetworkGameManager.ConnectedRoomCode = m_CurrentSession.Code;
            XRINetworkGameManager.ConnectedRoomName.Value = m_CurrentSession.Name;

            m_CurrentSession.SessionPropertiesChanged += OnSessionPropertiesChanged;
        }

        private void OnSessionPropertiesChanged()
        {
            if (m_CurrentSession == null)
                return;

            XRINetworkGameManager.ConnectedRoomCode = m_CurrentSession.Code;
            XRINetworkGameManager.ConnectedRoomName.Value = m_CurrentSession.Name;
        }

        public async Task LeaveSession()
        {
            if (m_CurrentSession != null)
            {
                m_CurrentSession.SessionPropertiesChanged -= OnSessionPropertiesChanged;
                await m_CurrentSession.LeaveAsync();
                m_CurrentSession = null;
            }
            else
            {
                Utils.Log($"{k_DebugPrepend}Connected Lobby is null");
            }
        }

        public static QuerySessionsOptions GetQuickJoinFilterOptions()
        {
            QuerySessionsOptions options = new QuerySessionsOptions();
            return options;
        }

        public async void UpdateLobbyName(string lobbyName)
        {
            if (m_CurrentSession != null && m_CurrentSession.IsHost)
            {
                try
                {
                    IHostSession hostSession = m_CurrentSession as IHostSession;

                    hostSession.Name = lobbyName;
                    await hostSession.SavePropertiesAsync();

                    XRINetworkGameManager.ConnectedRoomName.Value = lobbyName;
                }
                catch (LobbyServiceException e)
                {
                    Utils.Log($"{k_DebugPrepend}{e}");
                }
            }
            else
            {
                Utils.Log($"{k_DebugPrepend}Connected Lobby is null");
            }
        }

        public async void UpdateRoomPrivacy(bool privateRoom)
        {
            if (m_CurrentSession != null)
            {
                try
                {
                    IHostSession hostSession = m_CurrentSession as IHostSession;
                    hostSession.IsPrivate = privateRoom;
                    await hostSession.SavePropertiesAsync();
                }
                catch (LobbyServiceException e)
                {
                    Utils.Log($"{k_DebugPrepend}{e}");
                }
            }
            else
            {
                Utils.Log($"{k_DebugPrepend}Connected Lobby is null");
            }
        }

        public static bool CheckForSessionFilter(ISessionInfo sessionInfo)
        {
            return false;
        }

        public static bool CheckForIncompatibilityFilter(ISessionInfo sessionInfo)
        {
            return false;
        }

        public static bool CanJoinLobby(ISessionInfo session)
        {
            return (XRINetworkGameManager.Instance.sessionManager.currentSession == null) ||
                   (XRINetworkGameManager.Instance.sessionManager.currentSession != null &&
                    session.Id != XRINetworkGameManager.Instance.sessionManager.currentSession.Id);
        }
    }
}