using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MoneroPool
{
    class Configuration
    {
        // In memory object to store the configuration.
        public JObject ConfigJObject;

        /// <summary>
        /// Constructor
        /// </summary>
        public Configuration()
        {
            if (!Exists())
            {
                Create();
                WriteFromMemory();
            }
            else
            {
                ReadToMemory();
            }
        }

        /// <summary>
        /// Create a new configuration template.
        /// </summary>
        /// <returns></returns>
        public JObject Create()
        {
            // RPC Sub-object
            var rpc = new JObject()
            {
                {"daemon", "http://127.0.0.1:18081"},
                {"wallet", "http://127.0.0.1:8082"},
            };

            // Redis Sub-object.
            var redis = new JObject()
            {
                {"database-id", 0},
                {"address", ""},
                {"port", 6378}
            };

            // Pool Bans Sub-object.
            var poolBans = new JObject()
            {
                {"reject-percentage-threshold", 0.1},
                {"time-to-ban", 60},
            };

            // Pool Difficulty Sub-object.
            var poolDifficulty = new JObject()
            {
                {"starting", 100},
                {"ceiling", 1000},
                {"retarget-time", 1}
            };

            // Pool Shares Sub-object
            var poolShares = new JObject()
            {
                {"target-time", 15},
                {"deviation-min", 5},
                {"deviation-max", 10},
                {"concurrency", 15}
            };

            // Pool root object.
            var pool = new JObject()
            {
                {"stratum-port", 3333},
                {
                    "reward-address",
                    "4JUdGzvrMFDWrUUwY3toJATSeNwjn54LkCnKBPRzDuhzi5vSepHfUckJNxRL2gjkNrSqtCoRUrEDAgRwsQvVCjZbRxjWTA8jzqk5G67neS"
                },
                {"difficulty", poolDifficulty},
                {"bans", poolBans},
                {"shares", poolShares},
                {"idle-kick", 60}
            };

            // Root object.
            return new JObject()
            {
                {"rpc", rpc},
                {"pool", pool},
                {"redis", redis}
            };
        }

        /// <summary>
        /// Read the configuration from the disk into memory.
        /// </summary>
        public void ReadToMemory()
        {
            ConfigJObject = JObject.Parse(File.ReadAllText("config.json"));
        }

        /// <summary>
        /// Write the configuration loaded in memory to the disk.
        /// </summary>
        public void WriteFromMemory()
        {
            File.WriteAllText("config.json", JsonConvert.SerializeObject(ConfigJObject, Formatting.Indented));
        }

        /// <summary>
        /// Check to see if there is a configuration file on the disk.
        /// </summary>
        /// <returns></returns>
        public bool Exists()
        {
            return File.Exists("config.json");
        }

        /// <summary>
        /// Get the wallet RPC url.
        /// </summary>
        /// <returns></returns>
        public string GetWalletRpc() => ConfigJObject["rpc"]["wallet"].ToString();

        /// <summary>
        /// Get the daemon RPC url.
        /// </summary>
        /// <returns></returns>
        public string GetDaemonRpc() => ConfigJObject["rpc"]["daemon"].ToString();

        /// <summary>
        /// Get the address to the redis server in the configuration file.
        /// </summary>
        /// <returns></returns>
        public string GetRedisAddress() => ConfigJObject["redis"]["address"].ToString();

        /// <summary>
        /// Get the port to the redis server in the configuration file.
        /// </summary>
        /// <returns></returns>
        public int GetRedisPort() => int.Parse(ConfigJObject["redis"]["port"].ToString());

        /// <summary>
        /// Get the database number of the redis database in the configuration file.
        /// </summary>
        /// <returns></returns>
        public int GetRedisDatabase() => int.Parse(ConfigJObject["redis"]["database-id"].ToString());

        /// <summary>
        /// Get the starting difficulty of the pool when a miner connectes.
        /// </summary>
        /// <returns></returns>
        public long GetBaseDifficulty() => long.Parse(ConfigJObject["pool"]["starting"].ToString());
    }
}