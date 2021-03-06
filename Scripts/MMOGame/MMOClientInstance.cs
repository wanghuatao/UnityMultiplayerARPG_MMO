﻿using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using UnityEngine;
using LiteNetLib;
using LiteNetLibManager;

namespace MultiplayerARPG.MMO
{
    public partial class MMOClientInstance : MonoBehaviour
    {
        public static MMOClientInstance Singleton { get; protected set; }
        // Client data, May keep these data in player prefs to do auto login system
        public static string SelectedCentralAddress { get; private set; }
        public static int SelectedCentralPort { get; private set; }
        public static string UserId { get; private set; }
        public static string AccessToken { get; private set; }
        public static string SelectCharacterId { get; private set; }

        [Header("Client Components")]
        [SerializeField]
        private CentralNetworkManager centralNetworkManager;
        [SerializeField]
        private MapNetworkManager mapNetworkManager;

        [Header("Settings")]
        [SerializeField]
        private bool useWebSocket = false;
        [SerializeField]
        private MmoNetworkSetting[] networkSettings;

        public CentralNetworkManager CentralNetworkManager { get { return centralNetworkManager; } }
        public MapNetworkManager MapNetworkManager { get { return mapNetworkManager; } }
        public bool UseWebSocket { get { return useWebSocket; } }
        public MmoNetworkSetting[] NetworkSettings { get { return networkSettings; } }

        public System.Action onCentralClientConnected;
        public System.Action<DisconnectInfo> onCentralClientDisconnected;

        public System.Action onMapClientConnected;
        public System.Action<DisconnectInfo> onMapClientDisconnected;

        private void Awake()
        {
            if (Singleton != null)
            {
                Destroy(gameObject);
                return;
            }
            DontDestroyOnLoad(gameObject);
            Singleton = this;

            // Always accept SSL
            ServicePointManager.ServerCertificateValidationCallback += new RemoteCertificateValidationCallback((sender, certificate, chain, policyErrors) => { return true; });

            // Active WebSockets
            CentralNetworkManager.useWebSocket = UseWebSocket;
            MapNetworkManager.useWebSocket = UseWebSocket;
        }

        private void OnEnable()
        {
            centralNetworkManager.onClientConnected += OnCentralServerConnected;
            centralNetworkManager.onClientDisconnected += OnCentralServerDisconnected;
            mapNetworkManager.onClientConnected += OnMapServerConnected;
            mapNetworkManager.onClientDisconnected += OnMapServerDisconnected;
        }

        private void OnDisable()
        {
            centralNetworkManager.onClientConnected -= OnCentralServerConnected;
            centralNetworkManager.onClientDisconnected -= OnCentralServerDisconnected;
            mapNetworkManager.onClientConnected -= OnMapServerConnected;
            mapNetworkManager.onClientDisconnected -= OnMapServerDisconnected;
        }

        public void OnCentralServerConnected()
        {
            if (onCentralClientConnected != null)
                onCentralClientConnected.Invoke();
        }

        public void OnCentralServerDisconnected(DisconnectInfo disconnectInfo)
        {
            if (onCentralClientDisconnected != null)
                onCentralClientDisconnected.Invoke(disconnectInfo);
        }

        public void OnMapServerConnected()
        {
            if (onMapClientConnected != null)
                onMapClientConnected.Invoke();
            // Disconnect from central server when connected to map server
            StopCentralClient();
        }

        public void OnMapServerDisconnected(DisconnectInfo disconnectInfo)
        {
            if (onMapClientDisconnected != null)
                onMapClientDisconnected.Invoke(disconnectInfo);
        }

        #region Client functions
        public void StartCentralClient()
        {
            centralNetworkManager.StartClient();
        }

        public void StartCentralClient(string address, int port)
        {
            centralNetworkManager.networkAddress = SelectedCentralAddress = address;
            centralNetworkManager.networkPort = SelectedCentralPort = port;
            StartCentralClient();
        }

        public void StopCentralClient()
        {
            centralNetworkManager.StopClient();
        }

        public void StartMapClient(string sceneName, string address, int port)
        {
            mapNetworkManager.Assets.onlineScene.SceneName = sceneName;
            mapNetworkManager.StartClient(address, port);
        }

        public void StopMapClient()
        {
            mapNetworkManager.StopClient();
        }

        public bool IsConnectedToCentralServer()
        {
            return centralNetworkManager.IsClientConnected;
        }

        public void ClearClientData()
        {
            SelectedCentralAddress = string.Empty;
            SelectedCentralPort = 0;
            UserId = string.Empty;
            AccessToken = string.Empty;
            SelectCharacterId = string.Empty;
        }

        public void RequestUserLogin(string username, string password, AckMessageCallback callback)
        {
            centralNetworkManager.RequestUserLogin(username, password, (responseCode, messageData) => OnRequestUserLogin(responseCode, messageData, callback));
        }

        public void RequestUserRegister(string username, string password, AckMessageCallback callback)
        {
            centralNetworkManager.RequestUserRegister(username, password, callback);
        }

        public void RequestUserLogout(AckMessageCallback callback)
        {
            centralNetworkManager.RequestUserLogout((responseCode, messageData) => OnRequestUserLogout(responseCode, messageData, callback));
        }

        public void RequestValidateAccessToken(string userId, string accessToken, AckMessageCallback callback)
        {
            centralNetworkManager.RequestValidateAccessToken(userId, accessToken, (responseCode, messageData) => OnRequestValidateAccessToken(responseCode, messageData, callback));
        }

        public void RequestCharacters(AckMessageCallback callback)
        {
            centralNetworkManager.RequestCharacters(callback);
        }

        public void RequestCreateCharacter(PlayerCharacterData characterData, AckMessageCallback callback)
        {
            centralNetworkManager.RequestCreateCharacter(characterData, callback);
        }

        public void RequestDeleteCharacter(string characterId, AckMessageCallback callback)
        {
            centralNetworkManager.RequestDeleteCharacter(characterId, callback);
        }

        public void RequestSelectCharacter(string characterId, AckMessageCallback callback)
        {
            centralNetworkManager.RequestSelectCharacter(characterId, (responseCode, messageData) => OnRequestSelectCharacter(responseCode, messageData, characterId, callback));
        }

        private void OnRequestUserLogin(AckResponseCode responseCode, BaseAckMessage messageData, AckMessageCallback callback)
        {
            if (callback != null)
                callback.Invoke(responseCode, messageData);

            UserId = string.Empty;
            AccessToken = string.Empty;
            SelectCharacterId = string.Empty;
            if (messageData.responseCode == AckResponseCode.Success)
            {
                ResponseUserLoginMessage castedMessage = messageData as ResponseUserLoginMessage;
                UserId = castedMessage.userId;
                AccessToken = castedMessage.accessToken;
                SelectCharacterId = string.Empty;
            }
        }

        private void OnRequestUserLogout(AckResponseCode responseCode, BaseAckMessage messageData, AckMessageCallback callback)
        {
            if (callback != null)
                callback(responseCode, messageData);

            UserId = string.Empty;
            AccessToken = string.Empty;
            SelectCharacterId = string.Empty;
        }

        private void OnRequestValidateAccessToken(AckResponseCode responseCode, BaseAckMessage messageData, AckMessageCallback callback)
        {
            if (callback != null)
                callback.Invoke(responseCode, messageData);

            UserId = string.Empty;
            AccessToken = string.Empty;
            if (messageData.responseCode == AckResponseCode.Success)
            {
                ResponseValidateAccessTokenMessage castedMessage = messageData as ResponseValidateAccessTokenMessage;
                UserId = castedMessage.userId;
                AccessToken = castedMessage.accessToken;
            }
        }

        private void OnRequestSelectCharacter(AckResponseCode responseCode, BaseAckMessage messageData, string characterId, AckMessageCallback callback)
        {
            if (callback != null)
                callback.Invoke(responseCode, messageData);

            SelectCharacterId = string.Empty;
            if (messageData.responseCode == AckResponseCode.Success)
            {
                SelectCharacterId = characterId;
            }
        }
        #endregion
    }
}
