﻿#if UNITY_STANDALONE && !CLIENT_BUILD
using MySqlConnector;
using System.Collections.Generic;
using LiteNetLibManager;
using MiniJSON;
using System;
using System.IO;
using Cysharp.Threading.Tasks;
#endif
using UnityEngine;

namespace MultiplayerARPG.MMO
{
    public partial class MySQLDatabase : BaseDatabase
    {
        [SerializeField]
        private string address = "127.0.0.1";
        [SerializeField]
        private int port = 3306;
        [SerializeField]
        private string username = "root";
        [SerializeField]
        private string password = "";
        [SerializeField]
        private string dbName = "mmorpgtemplate";

#if UNITY_STANDALONE && !CLIENT_BUILD
        public override void Initialize()
        {
            // Json file read
            string configFilePath = "./config/mySqlConfig.json";
            Dictionary<string, object> jsonConfig = new Dictionary<string, object>();
            Logging.Log("Reading config file from " + configFilePath);
            if (File.Exists(configFilePath))
            {
                Logging.Log("Found config file");
                string dataAsJson = File.ReadAllText(configFilePath);
                jsonConfig = Json.Deserialize(dataAsJson) as Dictionary<string, object>;
            }

            ConfigReader.ReadConfigs(jsonConfig, "mySqlAddress", out address, address);
            ConfigReader.ReadConfigs(jsonConfig, "mySqlPort", out port, port);
            ConfigReader.ReadConfigs(jsonConfig, "mySqlUsername", out username, username);
            ConfigReader.ReadConfigs(jsonConfig, "mySqlPassword", out password, password);
            ConfigReader.ReadConfigs(jsonConfig, "mySqlDbName", out dbName, dbName);

            Migration().Forget();
        }

        private async UniTaskVoid Migration()
        {
            // 1.57b
            string migrationId = "1.57b";
            if (!await HasMigrationId(migrationId))
            {
                Logging.Log("Migrating up to 1.57b");
                // Migrate data
                try
                {
                    // Avoid exception which occuring when `dataId` field not found
                    foreach (BuildingEntity prefab in GameInstance.BuildingEntities.Values)
                    {
                        await ExecuteNonQuery("UPDATE buildings SET entityId=@entityId, dataId=0 WHERE dataId=@dataId",
                            new MySqlParameter("entityId", prefab.EntityId),
                            new MySqlParameter("dataId", prefab.name.GenerateHashId()));
                    }
                }
                catch { }
                // Migrate fields
                try
                {
                    // Avoid exception which occuring when `dataId` field not found
                    await ExecuteNonQuery("ALTER TABLE buildings DROP dataId;");
                }
                catch { }
                // Insert migrate history
                await InsertMigrationId(migrationId);
                Logging.Log("Migrated to 1.57b");
            }
            migrationId = "1.58";
            if (!await HasMigrationId(migrationId))
            {
                Logging.Log("Migrating up to 1.58");
                await ExecuteNonQuery("ALTER TABLE `characterbuff` CHANGE `type` `type` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0';");
                await ExecuteNonQuery("ALTER TABLE `characterhotkey` CHANGE `type` `type` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0';");
                await ExecuteNonQuery("ALTER TABLE `characteritem` CHANGE `inventoryType` `inventoryType` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0';");
                await ExecuteNonQuery("ALTER TABLE `characterskillusage` CHANGE `type` `type` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0';");
                await ExecuteNonQuery("ALTER TABLE `charactersummon` CHANGE `type` `type` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0';");
                await ExecuteNonQuery("ALTER TABLE `storageitem` CHANGE `storageType` `storageType` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0';");
                await ExecuteNonQuery("ALTER TABLE `userlogin` CHANGE `authType` `authType` TINYINT(3) UNSIGNED NOT NULL DEFAULT '1';");
                await ExecuteNonQuery("ALTER TABLE `userlogin` CHANGE `userLevel` `userLevel` TINYINT(3) UNSIGNED NOT NULL DEFAULT '0';");
                await ExecuteNonQuery("ALTER TABLE `characters` ADD `currentRotationX` FLOAT NOT NULL DEFAULT '0' AFTER `currentPositionZ`;");
                await ExecuteNonQuery("ALTER TABLE `characters` ADD `currentRotationY` FLOAT NOT NULL DEFAULT '0' AFTER `currentRotationX`;");
                await ExecuteNonQuery("ALTER TABLE `characters` ADD `currentRotationZ` FLOAT NOT NULL DEFAULT '0' AFTER `currentRotationY`;");
                // Insert migrate history
                await InsertMigrationId(migrationId);
                Logging.Log("Migrated to 1.58");
            }
        }

        private async UniTask<bool> HasMigrationId(string migrationId)
        {
            object result = await ExecuteScalar("SELECT COUNT(*) FROM __migrations WHERE migrationId=@migrationId", new MySqlParameter("@migrationId", migrationId));
            long count = result != null ? (long)result : 0;
            return count > 0;
        }

        public async UniTask InsertMigrationId(string migrationId)
        {
            await ExecuteNonQuery("INSERT INTO __migrations (migrationId) VALUES (@migrationId)", new MySqlParameter("@migrationId", migrationId));
        }

        public string GetConnectionString()
        {
            string connectionString = "Server=" + address + ";" +
                "Port=" + port + ";" +
                "Uid=" + username + ";" +
                (string.IsNullOrEmpty(password) ? "" : "Pwd=\"" + password + "\";") +
                "Database=" + dbName + ";" +
                "SSL Mode=None;";
            return connectionString;
        }

        public MySqlConnection NewConnection()
        {
            return new MySqlConnection(GetConnectionString());
        }

        private async UniTask OpenConnection(MySqlConnection connection)
        {
            try
            {
                await connection.OpenAsync();
            }
            catch (MySqlException ex)
            {
                Logging.LogException(ex);
            }
        }

        private void OpenConnectionSync(MySqlConnection connection)
        {
            try
            {
                connection.Open();
            }
            catch (MySqlException ex)
            {
                Logging.LogException(ex);
            }
        }

        public async UniTask<long> ExecuteInsertData(string sql, params MySqlParameter[] args)
        {
            MySqlConnection connection = NewConnection();
            await OpenConnection(connection);
            long result = await ExecuteInsertData(connection, null, sql, args);
            await connection.CloseAsync();
            return result;
        }

        public async UniTask<long> ExecuteInsertData(MySqlConnection connection, MySqlTransaction transaction, string sql, params MySqlParameter[] args)
        {
            bool createLocalConnection = false;
            if (connection == null)
            {
                connection = NewConnection();
                transaction = null;
                await OpenConnection(connection);
                createLocalConnection = true;
            }
            long result = 0;
            using (MySqlCommand cmd = new MySqlCommand(sql, connection))
            {
                if (transaction != null)
                    cmd.Transaction = transaction;
                foreach (MySqlParameter arg in args)
                {
                    cmd.Parameters.Add(arg);
                }
                try
                {
                    await cmd.ExecuteNonQueryAsync();
                    result = cmd.LastInsertedId;
                }
                catch (MySqlException ex)
                {
                    Logging.LogException(ex);
                }
            }
            if (createLocalConnection)
                await connection.CloseAsync();
            return result;
        }

        public long ExecuteInsertDataSync(string sql, params MySqlParameter[] args)
        {
            MySqlConnection connection = NewConnection();
            OpenConnectionSync(connection);
            long result = ExecuteInsertDataSync(connection, null, sql, args);
            connection.Close();
            return result;
        }

        public long ExecuteInsertDataSync(MySqlConnection connection, MySqlTransaction transaction, string sql, params MySqlParameter[] args)
        {
            bool createLocalConnection = false;
            if (connection == null)
            {
                connection = NewConnection();
                transaction = null;
                OpenConnectionSync(connection);
                createLocalConnection = true;
            }
            long result = 0;
            using (MySqlCommand cmd = new MySqlCommand(sql, connection))
            {
                if (transaction != null)
                    cmd.Transaction = transaction;
                foreach (MySqlParameter arg in args)
                {
                    cmd.Parameters.Add(arg);
                }
                try
                {
                    cmd.ExecuteNonQuery();
                    result = cmd.LastInsertedId;
                }
                catch (MySqlException ex)
                {
                    Logging.LogException(ex);
                }
            }
            if (createLocalConnection)
                connection.Close();
            return result;
        }

        public async UniTask<int> ExecuteNonQuery(string sql, params MySqlParameter[] args)
        {
            MySqlConnection connection = NewConnection();
            await OpenConnection(connection);
            int result = await ExecuteNonQuery(connection, null, sql, args);
            await connection.CloseAsync();
            return result;
        }

        public async UniTask<int> ExecuteNonQuery(MySqlConnection connection, MySqlTransaction transaction, string sql, params MySqlParameter[] args)
        {
            bool createLocalConnection = false;
            if (connection == null)
            {
                connection = NewConnection();
                transaction = null;
                await OpenConnection(connection);
                createLocalConnection = true;
            }
            int numRows = 0;
            using (MySqlCommand cmd = new MySqlCommand(sql, connection))
            {
                if (transaction != null)
                    cmd.Transaction = transaction;
                foreach (MySqlParameter arg in args)
                {
                    cmd.Parameters.Add(arg);
                }
                try
                {
                    numRows = await cmd.ExecuteNonQueryAsync();
                }
                catch (MySqlException ex)
                {
                    Logging.LogException(ex);
                }
            }
            if (createLocalConnection)
                await connection.CloseAsync();
            return numRows;
        }

        public int ExecuteNonQuerySync(string sql, params MySqlParameter[] args)
        {
            MySqlConnection connection = NewConnection();
            OpenConnectionSync(connection);
            int result = ExecuteNonQuerySync(connection, null, sql, args);
            connection.Close();
            return result;
        }

        public int ExecuteNonQuerySync(MySqlConnection connection, MySqlTransaction transaction, string sql, params MySqlParameter[] args)
        {
            bool createLocalConnection = false;
            if (connection == null)
            {
                connection = NewConnection();
                transaction = null;
                OpenConnectionSync(connection);
                createLocalConnection = true;
            }
            int numRows = 0;
            using (MySqlCommand cmd = new MySqlCommand(sql, connection))
            {
                if (transaction != null)
                    cmd.Transaction = transaction;
                foreach (MySqlParameter arg in args)
                {
                    cmd.Parameters.Add(arg);
                }
                try
                {
                    numRows = cmd.ExecuteNonQuery();
                }
                catch (MySqlException ex)
                {
                    Logging.LogException(ex);
                }
            }
            if (createLocalConnection)
                connection.Close();
            return numRows;
        }

        public async UniTask<object> ExecuteScalar(string sql, params MySqlParameter[] args)
        {
            MySqlConnection connection = NewConnection();
            await OpenConnection(connection);
            object result = await ExecuteScalar(connection, null, sql, args);
            await connection.CloseAsync();
            return result;
        }

        public async UniTask<object> ExecuteScalar(MySqlConnection connection, MySqlTransaction transaction, string sql, params MySqlParameter[] args)
        {
            bool createLocalConnection = false;
            if (connection == null)
            {
                connection = NewConnection();
                transaction = null;
                await OpenConnection(connection);
                createLocalConnection = true;
            }
            object result = null;
            using (MySqlCommand cmd = new MySqlCommand(sql, connection))
            {
                if (transaction != null)
                    cmd.Transaction = transaction;
                foreach (MySqlParameter arg in args)
                {
                    cmd.Parameters.Add(arg);
                }
                try
                {
                    result = await cmd.ExecuteScalarAsync();
                }
                catch (MySqlException ex)
                {
                    Logging.LogException(ex);
                }
            }
            if (createLocalConnection)
                await connection.CloseAsync();
            return result;
        }

        public object ExecuteScalarSync(string sql, params MySqlParameter[] args)
        {
            MySqlConnection connection = NewConnection();
            OpenConnectionSync(connection);
            object result = ExecuteScalarSync(connection, null, sql, args);
            connection.Close();
            return result;
        }

        public object ExecuteScalarSync(MySqlConnection connection, MySqlTransaction transaction, string sql, params MySqlParameter[] args)
        {
            bool createLocalConnection = false;
            if (connection == null)
            {
                connection = NewConnection();
                transaction = null;
                OpenConnectionSync(connection);
                createLocalConnection = true;
            }
            object result = null;
            using (MySqlCommand cmd = new MySqlCommand(sql, connection))
            {
                if (transaction != null)
                    cmd.Transaction = transaction;
                foreach (MySqlParameter arg in args)
                {
                    cmd.Parameters.Add(arg);
                }
                try
                {
                    result = cmd.ExecuteScalar();
                }
                catch (MySqlException ex)
                {
                    Logging.LogException(ex);
                }
            }
            if (createLocalConnection)
                connection.Close();
            return result;
        }

        public async UniTask ExecuteReader(Action<MySqlDataReader> onRead, string sql, params MySqlParameter[] args)
        {
            MySqlConnection connection = NewConnection();
            await OpenConnection(connection);
            await ExecuteReader(connection, null, onRead, sql, args);
            await connection.CloseAsync();
        }

        public async UniTask ExecuteReader(MySqlConnection connection, MySqlTransaction transaction, Action<MySqlDataReader> onRead, string sql, params MySqlParameter[] args)
        {
            bool createLocalConnection = false;
            if (connection == null)
            {
                connection = NewConnection();
                transaction = null;
                await OpenConnection(connection);
                createLocalConnection = true;
            }
            using (MySqlCommand cmd = new MySqlCommand(sql, connection))
            {
                if (transaction != null)
                    cmd.Transaction = transaction;
                foreach (MySqlParameter arg in args)
                {
                    cmd.Parameters.Add(arg);
                }
                try
                {
                    MySqlDataReader dataReader = await cmd.ExecuteReaderAsync();
                    if (onRead != null) onRead.Invoke(dataReader);
                    dataReader.Close();
                }
                catch (MySqlException ex)
                {
                    Logging.LogException(ex);
                }
            }
            if (createLocalConnection)
                await connection.CloseAsync();
        }

        public void ExecuteReaderSync(Action<MySqlDataReader> onRead, string sql, params MySqlParameter[] args)
        {
            MySqlConnection connection = NewConnection();
            OpenConnectionSync(connection);
            ExecuteReaderSync(connection, null, onRead, sql, args);
            connection.Close();
        }

        public void ExecuteReaderSync(MySqlConnection connection, MySqlTransaction transaction, Action<MySqlDataReader> onRead, string sql, params MySqlParameter[] args)
        {
            bool createLocalConnection = false;
            if (connection == null)
            {
                connection = NewConnection();
                transaction = null;
                OpenConnectionSync(connection);
                createLocalConnection = true;
            }
            using (MySqlCommand cmd = new MySqlCommand(sql, connection))
            {
                if (transaction != null)
                    cmd.Transaction = transaction;
                foreach (MySqlParameter arg in args)
                {
                    cmd.Parameters.Add(arg);
                }
                try
                {
                    MySqlDataReader dataReader = cmd.ExecuteReader();
                    if (onRead != null) onRead.Invoke(dataReader);
                    dataReader.Close();
                }
                catch (MySqlException ex)
                {
                    Logging.LogException(ex);
                }
            }
            if (createLocalConnection)
                connection.Close();
        }

        public override async UniTask<string> ValidateUserLogin(string username, string password)
        {
            string id = string.Empty;
            await ExecuteReader((reader) =>
            {
                if (reader.Read())
                {
                    id = reader.GetString(0);
                }
            }, "SELECT id FROM userlogin WHERE username=@username AND password=@password AND authType=@authType LIMIT 1",
                new MySqlParameter("@username", username),
                new MySqlParameter("@password", password.GetMD5()),
                new MySqlParameter("@authType", AUTH_TYPE_NORMAL));

            return id;
        }

        public override async UniTask<bool> ValidateAccessToken(string userId, string accessToken)
        {
            object result = await ExecuteScalar("SELECT COUNT(*) FROM userlogin WHERE id=@id AND accessToken=@accessToken",
                new MySqlParameter("@id", userId),
                new MySqlParameter("@accessToken", accessToken));
            return (result != null ? (long)result : 0) > 0;
        }

        public override async UniTask<byte> GetUserLevel(string userId)
        {
            byte userLevel = 0;
            await ExecuteReader((reader) =>
            {
                if (reader.Read())
                    userLevel = reader.GetByte(0);
            }, "SELECT userLevel FROM userlogin WHERE id=@id LIMIT 1",
                new MySqlParameter("@id", userId));
            return userLevel;
        }

        public override async UniTask<int> GetGold(string userId)
        {
            int gold = 0;
            await ExecuteReader((reader) =>
            {
                if (reader.Read())
                    gold = reader.GetInt32(0);
            }, "SELECT gold FROM userlogin WHERE id=@id LIMIT 1",
                new MySqlParameter("@id", userId));
            return gold;
        }

        public override async UniTask UpdateGold(string userId, int gold)
        {
            await ExecuteNonQuery("UPDATE userlogin SET gold=@gold WHERE id=@id",
                new MySqlParameter("@id", userId),
                new MySqlParameter("@gold", gold));
        }

        public override async UniTask<int> GetCash(string userId)
        {
            int cash = 0;
            await ExecuteReader((reader) =>
            {
                if (reader.Read())
                    cash = reader.GetInt32(0);
            }, "SELECT cash FROM userlogin WHERE id=@id LIMIT 1",
                new MySqlParameter("@id", userId));
            return cash;
        }

        public override async UniTask UpdateCash(string userId, int cash)
        {
            await ExecuteNonQuery("UPDATE userlogin SET cash=@cash WHERE id=@id",
                new MySqlParameter("@id", userId),
                new MySqlParameter("@cash", cash));
        }

        public override async UniTask UpdateAccessToken(string userId, string accessToken)
        {
            await ExecuteNonQuery("UPDATE userlogin SET accessToken=@accessToken WHERE id=@id",
                new MySqlParameter("@id", userId),
                new MySqlParameter("@accessToken", accessToken));
        }

        public override async UniTask CreateUserLogin(string username, string password)
        {
            await ExecuteNonQuery("INSERT INTO userlogin (id, username, password, email, authType) VALUES (@id, @username, @password, @email, @authType)",
                new MySqlParameter("@id", GenericUtils.GetUniqueId()),
                new MySqlParameter("@username", username),
                new MySqlParameter("@password", password.GetMD5()),
                new MySqlParameter("@email", ""),
                new MySqlParameter("@authType", AUTH_TYPE_NORMAL));
        }

        public override async UniTask<long> FindUsername(string username)
        {
            object result = await ExecuteScalar("SELECT COUNT(*) FROM userlogin WHERE username LIKE @username",
                new MySqlParameter("@username", username));
            return result != null ? (long)result : 0;
        }
#endif
    }
}
